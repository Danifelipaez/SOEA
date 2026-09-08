using System;
using System.Collections.Generic;
using System.Linq;
using SOEA.Application.Features.Horario;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;
using Xunit;

namespace SOEA.Tests.Application.Horario
{
    /// <summary>
    /// Cubre dos bugs de raíz confirmados en el pipeline presencial-first
    /// (<see cref="GenerarHorarioService"/>):
    /// (1) Etapa 1 (<see cref="GenerarHorarioService.AplicarPrioridadPresencial"/>) usaba un umbral
    /// de saturación hardcodeado (40h) en vez de la grilla real de bloques, y nunca excluía las
    /// sesiones fijas del horario base.
    /// (2) Etapa 2 (<see cref="GenerarHorarioService.CederSiguienteCandidatoLab"/>) podía emparejar
    /// dos sesiones de laboratorio del mismo grupo o de la misma asignatura (estructuralmente
    /// infactible para CP-SAT: HC-ALT las fuerza al mismo bloque, HC-C01 exige NoOverlap por
    /// (grupo, semana)), y no protegía la última sesión presencial de un (asignatura, grupo).
    /// </summary>
    public class CederSiguienteCandidatoLabTests
    {
        private static Sesion Pres(Guid asig, Guid grupo, decimal dur, bool bloqueada = false) =>
            new(Guid.NewGuid(), asig, null, Guid.NewGuid(), null, grupo,
                TipoAlternancia.SinAlternancia, Modalidad.Presencial, dur, false, false,
                tipoFlujo: TipoFlujo.AulaVirtual, bloqueada: bloqueada);

        private static Sesion PresLab(Guid asig, Guid grupo, decimal dur) =>
            new(Guid.NewGuid(), asig, null, Guid.NewGuid(), null, grupo,
                TipoAlternancia.SinAlternancia, Modalidad.Presencial, dur, false, false,
                tipoFlujo: TipoFlujo.Laboratorio);

        private static List<BloqueTiempo> BloquesFalsos(int n) =>
            Enumerable.Range(0, n)
                .Select(_ => new BloqueTiempo(Guid.NewGuid(), DiaDeSemana.Lunes, new TimeOnly(6, 0), new TimeOnly(7, 0)))
                .ToList();

        private static (CriterioElegibilidadAlternancia, Func<Sesion, bool>) CriterioElectiva(Dictionary<Guid, CategoriaAsignatura> categoriaPorAsig) =>
            (CriterioElegibilidadAlternancia.Electiva,
             s => categoriaPorAsig.TryGetValue(s.AsignaturaId, out var cat) && cat == CategoriaAsignatura.Electiva);

        private static (CriterioElegibilidadAlternancia, Func<Sesion, bool>) CriterioElegible(HashSet<Guid> elegibles) =>
            (CriterioElegibilidadAlternancia.Elegible, s => elegibles.Contains(s.AsignaturaId));

        // ── Etapa 1 (M-bug1): capacidad real de bloques, no umbral hardcodeado ──────────────────

        [Fact]
        public void AplicarPrioridadPresencial_ConBloquesReales_NoSobreCedeBajoElUmbralLegacy()
        {
            var grupo = Guid.NewGuid();
            var asig = Guid.NewGuid();
            var espacios = new List<Espacio> { new(Guid.NewGuid(), "E", TipoEspacio.Salon, 30) };
            // 48h de demanda: supera el umbral legado hardcodeado (1 espacio × 40h) pero está muy por
            // debajo de la capacidad real de una grilla de 87 bloques/semana (1 × 87h).
            var sesiones = Enumerable.Range(0, 6).Select(_ => Pres(asig, grupo, 8m)).ToList();
            var categoria = new Dictionary<Guid, CategoriaAsignatura> { [asig] = CategoriaAsignatura.Electiva };
            var predicados = new List<(CriterioElegibilidadAlternancia, Func<Sesion, bool>)> { CriterioElectiva(categoria) };

            var cedidasIds = GenerarHorarioService.AplicarPrioridadPresencial(
                sesiones, espacios, predicados, bloques: BloquesFalsos(87));

            Assert.Empty(cedidasIds);
            Assert.All(sesiones, s => Assert.Equal(TipoAlternancia.SinAlternancia, s.Alternancia));
        }

        [Fact]
        public void AplicarPrioridadPresencial_ConBloquesReales_CedeCuandoSuperaLaCapacidadReal()
        {
            var grupo = Guid.NewGuid();
            var asigA = Guid.NewGuid();
            var asigB = Guid.NewGuid();
            var espacios = new List<Espacio> { new(Guid.NewGuid(), "E", TipoEspacio.Salon, 30) };
            // 1 espacio × 10 bloques/semana = 10h de capacidad real. Demanda: 12h (2 asignaturas ×
            // 2 sesiones de 3h) supera la capacidad real aunque esté muy por debajo del umbral legado (40h).
            var sesiones = new List<Sesion>
            {
                Pres(asigA, grupo, 3m), Pres(asigA, grupo, 3m),
                Pres(asigB, grupo, 3m), Pres(asigB, grupo, 3m),
            };
            var categoria = new Dictionary<Guid, CategoriaAsignatura>
            {
                [asigA] = CategoriaAsignatura.Electiva,
                [asigB] = CategoriaAsignatura.Electiva
            };
            var predicados = new List<(CriterioElegibilidadAlternancia, Func<Sesion, bool>)> { CriterioElectiva(categoria) };

            var cedidasIds = GenerarHorarioService.AplicarPrioridadPresencial(
                sesiones, espacios, predicados, bloques: BloquesFalsos(10));

            Assert.NotEmpty(cedidasIds);
        }

        [Fact]
        public void AplicarPrioridadPresencial_SinBloques_UsaFallbackLegacyDe40Horas()
        {
            // Firma de 3 argumentos (bloques omitido): PresencialFirstTests.cs depende de que esto
            // siga usando el umbral legado (40h) — este test fija ese comportamiento explícitamente
            // para que no se "limpie" el shim de compatibilidad y rompa esos tests.
            var grupo = Guid.NewGuid();
            var asig = Guid.NewGuid();
            var espacios = new List<Espacio> { new(Guid.NewGuid(), "E", TipoEspacio.Salon, 30) };
            var sesiones = Enumerable.Range(0, 6).Select(_ => Pres(asig, grupo, 8m)).ToList(); // 48h > 40h
            var categoria = new Dictionary<Guid, CategoriaAsignatura> { [asig] = CategoriaAsignatura.Electiva };
            var predicados = new List<(CriterioElegibilidadAlternancia, Func<Sesion, bool>)> { CriterioElectiva(categoria) };

            var cedidasIds = GenerarHorarioService.AplicarPrioridadPresencial(sesiones, espacios, predicados);

            Assert.NotEmpty(cedidasIds);
        }

        // ── Etapa 1 (M-bug2): nunca toca sesiones fijas del horario base ────────────────────────

        [Fact]
        public void AplicarPrioridadPresencial_NuncaTocaSesionFija_AunqueMatcheeCriterioActivo()
        {
            var grupo = Guid.NewGuid();
            var asigFija = Guid.NewGuid();
            var espacios = new List<Espacio> { new(Guid.NewGuid(), "E", TipoEspacio.Salon, 30) };
            var fija = Pres(asigFija, grupo, 8m);
            var otraFija = Pres(asigFija, grupo, 8m);
            var sesionesFijasIds = new HashSet<Guid> { fija.Id, otraFija.Id };
            var sesiones = new List<Sesion> { fija, otraFija };
            var categoria = new Dictionary<Guid, CategoriaAsignatura> { [asigFija] = CategoriaAsignatura.Electiva };
            var predicados = new List<(CriterioElegibilidadAlternancia, Func<Sesion, bool>)> { CriterioElectiva(categoria) };

            var cedidasIds = GenerarHorarioService.AplicarPrioridadPresencial(
                sesiones, espacios, predicados, sesionesFijasIds: sesionesFijasIds);

            Assert.Empty(cedidasIds);
            Assert.Equal(TipoAlternancia.SinAlternancia, fija.Alternancia);
            Assert.Equal(Modalidad.Presencial, fija.Modalidad);
            Assert.False(fija.CedidaPorSaturacion);
            Assert.Equal(TipoAlternancia.SinAlternancia, otraFija.Alternancia);
        }

        // ── Etapa 2 (M-bug3): nunca empareja misma asignatura o mismo grupo ─────────────────────

        [Fact]
        public void CederSiguienteCandidatoLab_NoEmpareja_MismaAsignatura_AunqueSeaOtroGrupo()
        {
            var asig = Guid.NewGuid();
            var s1 = PresLab(asig, Guid.NewGuid(), 2m);
            var s2 = PresLab(asig, Guid.NewGuid(), 2m); // misma asignatura, otro grupo
            var sesiones = new List<Sesion> { s1, s2 };
            var categoria = new Dictionary<Guid, CategoriaAsignatura> { [asig] = CategoriaAsignatura.Electiva };
            var predicados = new List<(CriterioElegibilidadAlternancia, Func<Sesion, bool>)> { CriterioElectiva(categoria) };

            var cedio = GenerarHorarioService.CederSiguienteCandidatoLab(
                sesiones, predicados, new HashSet<Guid>(), new List<Guid>());

            Assert.False(cedio);
            Assert.Equal(TipoAlternancia.SinAlternancia, s1.Alternancia);
            Assert.Equal(TipoAlternancia.SinAlternancia, s2.Alternancia);
        }

        [Fact]
        public void CederSiguienteCandidatoLab_NoEmpareja_MismoGrupo_AunqueSeaOtraAsignatura()
        {
            var grupo = Guid.NewGuid();
            var s1 = PresLab(Guid.NewGuid(), grupo, 2m);
            var s2 = PresLab(Guid.NewGuid(), grupo, 2m); // misma grupo, otra asignatura
            var sesiones = new List<Sesion> { s1, s2 };
            var categoria = new Dictionary<Guid, CategoriaAsignatura>
            {
                [s1.AsignaturaId] = CategoriaAsignatura.Electiva,
                [s2.AsignaturaId] = CategoriaAsignatura.Electiva
            };
            var predicados = new List<(CriterioElegibilidadAlternancia, Func<Sesion, bool>)> { CriterioElectiva(categoria) };

            var cedio = GenerarHorarioService.CederSiguienteCandidatoLab(
                sesiones, predicados, new HashSet<Guid>(), new List<Guid>());

            Assert.False(cedio);
            Assert.Equal(TipoAlternancia.SinAlternancia, s1.Alternancia);
            Assert.Equal(TipoAlternancia.SinAlternancia, s2.Alternancia);
        }

        [Fact]
        public void CederSiguienteCandidatoLab_EmparejaCuandoAsignaturaYGrupoDifieren()
        {
            var asigA = Guid.NewGuid(); var grupoX = Guid.NewGuid();
            var asigB = Guid.NewGuid(); var grupoY = Guid.NewGuid();
            // 2 sesiones por lado (asignatura, grupo) para satisfacer el invariante "≥1 presencial
            // restante" — no es lo que este test verifica, solo un requisito de montaje.
            var s1a = PresLab(asigA, grupoX, 2m);
            var s1b = PresLab(asigA, grupoX, 2m);
            var s2a = PresLab(asigB, grupoY, 2m);
            var s2b = PresLab(asigB, grupoY, 2m);
            var sesiones = new List<Sesion> { s1a, s1b, s2a, s2b };
            var categoria = new Dictionary<Guid, CategoriaAsignatura>
            {
                [asigA] = CategoriaAsignatura.Electiva,
                [asigB] = CategoriaAsignatura.Electiva
            };
            var predicados = new List<(CriterioElegibilidadAlternancia, Func<Sesion, bool>)> { CriterioElectiva(categoria) };
            var cedidasEnOrden = new List<Guid>();

            var cedio = GenerarHorarioService.CederSiguienteCandidatoLab(
                sesiones, predicados, new HashSet<Guid>(), cedidasEnOrden);

            Assert.True(cedio);
            Assert.Equal(2, cedidasEnOrden.Count);
            var cedidaA = new[] { s1a, s1b }.Single(s => s.Alternancia != TipoAlternancia.SinAlternancia);
            var cedidaB = new[] { s2a, s2b }.Single(s => s.Alternancia != TipoAlternancia.SinAlternancia);
            Assert.NotEqual(cedidaA.Alternancia, cedidaB.Alternancia);
            Assert.NotNull(cedidaA.ParejaAlternanciaId);
            Assert.Equal(cedidaA.ParejaAlternanciaId, cedidaB.ParejaAlternanciaId);
        }

        // ── Etapa 2 (M-bug4): nunca ceder la única sesión presencial de un (asignatura, grupo) ──

        [Fact]
        public void CederSiguienteCandidatoLab_NuncaCedeLaUnicaSesionDeSuAsignaturaYGrupo()
        {
            var asigA = Guid.NewGuid(); var grupoX = Guid.NewGuid();
            var asigB = Guid.NewGuid(); var grupoY = Guid.NewGuid();
            var asigC = Guid.NewGuid(); var grupoZ = Guid.NewGuid();

            // soloLab es la ÚNICA sesión de (asigA, grupoX) — nunca debe ceder, ni como ancla ni
            // como pareja, aunque sea la primera candidata en la lista.
            var soloLab = PresLab(asigA, grupoX, 2m);
            var labB1 = PresLab(asigB, grupoY, 2m);
            var labB2 = PresLab(asigB, grupoY, 2m);
            var labC1 = PresLab(asigC, grupoZ, 2m);
            var labC2 = PresLab(asigC, grupoZ, 2m);
            var sesiones = new List<Sesion> { soloLab, labB1, labB2, labC1, labC2 };

            var categoria = new Dictionary<Guid, CategoriaAsignatura>
            {
                [asigA] = CategoriaAsignatura.Electiva,
                [asigB] = CategoriaAsignatura.Electiva,
                [asigC] = CategoriaAsignatura.Electiva
            };
            var predicados = new List<(CriterioElegibilidadAlternancia, Func<Sesion, bool>)> { CriterioElectiva(categoria) };

            var cedio = GenerarHorarioService.CederSiguienteCandidatoLab(
                sesiones, predicados, new HashSet<Guid>(), new List<Guid>());

            Assert.True(cedio);
            Assert.Equal(TipoAlternancia.SinAlternancia, soloLab.Alternancia);
            Assert.False(soloLab.CedidaPorSaturacion);
            // La pareja formada es entre las dos asignaturas que sí tienen ≥2 sesiones.
            Assert.Equal(TipoAlternancia.SinAlternancia, labB2.Alternancia);
            Assert.Equal(TipoAlternancia.SinAlternancia, labC2.Alternancia);
            Assert.True(labB1.Alternancia != TipoAlternancia.SinAlternancia || labC1.Alternancia != TipoAlternancia.SinAlternancia);
        }

        // ── Cobertura base que no existía hoy ────────────────────────────────────────────────────

        [Fact]
        public void CederSiguienteCandidatoLab_MenosDeDosCandidatos_RetornaFalse()
        {
            var s1 = PresLab(Guid.NewGuid(), Guid.NewGuid(), 2m);
            var categoria = new Dictionary<Guid, CategoriaAsignatura> { [s1.AsignaturaId] = CategoriaAsignatura.Electiva };
            var predicados = new List<(CriterioElegibilidadAlternancia, Func<Sesion, bool>)> { CriterioElectiva(categoria) };

            var cedio = GenerarHorarioService.CederSiguienteCandidatoLab(
                new List<Sesion> { s1 }, predicados, new HashSet<Guid>(), new List<Guid>());

            Assert.False(cedio);
        }

        [Fact]
        public void CederSiguienteCandidatoLab_RespetaOrdenDeCriterios()
        {
            var asigEle = Guid.NewGuid(); var grupoEle = Guid.NewGuid();
            var asigElg = Guid.NewGuid(); var grupoElg = Guid.NewGuid();
            var categoria = new Dictionary<Guid, CategoriaAsignatura> { [asigEle] = CategoriaAsignatura.Electiva };
            var elegibles = new HashSet<Guid> { asigElg };

            // 2 sesiones por (asignatura, grupo) — requisito de montaje del invariante "≥1 restante",
            // no lo que este test verifica.
            var sesionesA = new List<Sesion>
            {
                PresLab(asigEle, grupoEle, 2m), PresLab(asigEle, grupoEle, 2m),
                PresLab(asigElg, grupoElg, 2m), PresLab(asigElg, grupoElg, 2m),
            };
            var ordenA = new List<Guid>();
            GenerarHorarioService.CederSiguienteCandidatoLab(
                sesionesA, new List<(CriterioElegibilidadAlternancia, Func<Sesion, bool>)>
                    { CriterioElectiva(categoria), CriterioElegible(elegibles) },
                new HashSet<Guid>(), ordenA);
            Assert.NotEmpty(ordenA);
            Assert.Equal(asigEle, sesionesA.Single(s => s.Id == ordenA[0]).AsignaturaId); // Electiva primero ⇒ ancla

            var sesionesB = new List<Sesion>
            {
                PresLab(asigEle, grupoEle, 2m), PresLab(asigEle, grupoEle, 2m),
                PresLab(asigElg, grupoElg, 2m), PresLab(asigElg, grupoElg, 2m),
            };
            var ordenB = new List<Guid>();
            GenerarHorarioService.CederSiguienteCandidatoLab(
                sesionesB, new List<(CriterioElegibilidadAlternancia, Func<Sesion, bool>)>
                    { CriterioElegible(elegibles), CriterioElectiva(categoria) },
                new HashSet<Guid>(), ordenB);
            Assert.NotEmpty(ordenB);
            Assert.Equal(asigElg, sesionesB.Single(s => s.Id == ordenB[0]).AsignaturaId); // Elegible primero ⇒ ancla
        }

        [Fact]
        public void CederSiguienteCandidatoLab_ExcluyeSesionesFijas()
        {
            var asig = Guid.NewGuid();
            var fija = PresLab(asig, Guid.NewGuid(), 2m);
            var libre = PresLab(Guid.NewGuid(), Guid.NewGuid(), 2m);
            var sesiones = new List<Sesion> { fija, libre };
            var categoria = new Dictionary<Guid, CategoriaAsignatura>
            {
                [asig] = CategoriaAsignatura.Electiva,
                [libre.AsignaturaId] = CategoriaAsignatura.Electiva
            };
            var predicados = new List<(CriterioElegibilidadAlternancia, Func<Sesion, bool>)> { CriterioElectiva(categoria) };

            var cedio = GenerarHorarioService.CederSiguienteCandidatoLab(
                sesiones, predicados, new HashSet<Guid> { fija.Id }, new List<Guid>());

            Assert.False(cedio); // solo queda 1 candidato real tras excluir la fija
            Assert.Equal(TipoAlternancia.SinAlternancia, fija.Alternancia);
            Assert.Equal(TipoAlternancia.SinAlternancia, libre.Alternancia);
        }
    }
}
