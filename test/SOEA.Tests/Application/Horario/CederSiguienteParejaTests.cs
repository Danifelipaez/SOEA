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
    /// Cesión a alternancia = activación de la Semana B. Es reactiva: este método solo se invoca
    /// cuando CP-SAT ya demostró que no hay configuración válida por falta de aulas, así que aquí
    /// no hay ninguna heurística de saturación que probar — solo la regla de emparejamiento.
    ///
    /// Reemplaza a AplicarPrioridadPresencial (heurística preventiva, eliminada) y a
    /// CederSiguienteCandidatoLab (solo laboratorios). Ahora hay una sola regla para laboratorio y
    /// teoría presencial.
    /// </summary>
    public class CederSiguienteParejaTests
    {
        private static Sesion Pres(Guid asig, Guid grupo, decimal dur = 2m,
            TipoFlujo flujo = TipoFlujo.AulaVirtual, bool bloqueada = false) =>
            new(Guid.NewGuid(), asig, null, Guid.NewGuid(), null, grupo,
                TipoAlternancia.SinAlternancia, Modalidad.Presencial, dur, false, false,
                tipoFlujo: flujo, bloqueada: bloqueada);

        private static Sesion PresLab(Guid asig, Guid grupo, decimal dur = 2m) =>
            Pres(asig, grupo, dur, TipoFlujo.Laboratorio);

        private static (CriterioElegibilidadAlternancia, Func<Sesion, bool>) CriterioElectiva(
            Dictionary<Guid, CategoriaAsignatura> categoriaPorAsig) =>
            (CriterioElegibilidadAlternancia.Electiva,
             s => categoriaPorAsig.TryGetValue(s.AsignaturaId, out var cat) && cat == CategoriaAsignatura.Electiva);

        private static (CriterioElegibilidadAlternancia, Func<Sesion, bool>) CriterioElegible(HashSet<Guid> elegibles) =>
            (CriterioElegibilidadAlternancia.Elegible, s => elegibles.Contains(s.AsignaturaId));

        /// <summary>Contexto permisivo: todas comparten franjas y aulas. Aísla la regla bajo prueba.</summary>
        private static GenerarHorarioService.ContextoCesion Ctx(
            IEnumerable<Sesion> sesiones,
            IReadOnlyDictionary<Guid, int[]>? dominios = null,
            IReadOnlyDictionary<Guid, IReadOnlyList<int>>? aulas = null)
        {
            var todas = sesiones.ToList();
            var todo = new[] { 0, 1, 2, 3 };
            return new GenerarHorarioService.ContextoCesion(
                new Dictionary<Guid, Grupo>(),
                dominios ?? todas.ToDictionary(s => s.Id, _ => todo),
                aulas ?? todas.ToDictionary(s => s.Id, _ => (IReadOnlyList<int>)todo));
        }

        private static GenerarHorarioService.ResultadoCesion Ceder(
            List<Sesion> sesiones,
            List<(CriterioElegibilidadAlternancia, Func<Sesion, bool>)> criterios,
            GenerarHorarioService.ContextoCesion? ctx = null,
            HashSet<Guid>? fijas = null) =>
            GenerarHorarioService.CederSiguientePareja(
                sesiones, criterios, fijas ?? new HashSet<Guid>(), ctx ?? Ctx(sesiones),
                new HashSet<(Guid, Guid)>());

        private static Dictionary<Guid, CategoriaAsignatura> Electivas(params Guid[] asigs) =>
            asigs.ToDictionary(a => a, _ => CategoriaAsignatura.Electiva);

        // ── Reglas de emparejamiento ────────────────────────────────────────────────

        [Fact]
        public void NoEmpareja_MismaAsignatura_AunqueSeaOtroGrupo()
        {
            var asig = Guid.NewGuid();
            var sesiones = new List<Sesion> { PresLab(asig, Guid.NewGuid()), PresLab(asig, Guid.NewGuid()) };
            var criterios = new List<(CriterioElegibilidadAlternancia, Func<Sesion, bool>)>
                { CriterioElectiva(Electivas(asig)) };

            var r = Ceder(sesiones, criterios);

            Assert.False(r.Cedio);
            Assert.All(sesiones, s => Assert.Equal(TipoAlternancia.SinAlternancia, s.Alternancia));
        }

        [Fact]
        public void NoEmpareja_MismoGrupo_AunqueSeaOtraAsignatura()
        {
            // HC-C01 es independiente de la semana y HC-ALT fuerza el mismo bloque: la cohorte
            // tendría dos sesiones a la misma hora. Estructuralmente infactible.
            var grupo = Guid.NewGuid();
            var s1 = PresLab(Guid.NewGuid(), grupo);
            var s2 = PresLab(Guid.NewGuid(), grupo);
            var sesiones = new List<Sesion> { s1, s2 };
            var criterios = new List<(CriterioElegibilidadAlternancia, Func<Sesion, bool>)>
                { CriterioElectiva(Electivas(s1.AsignaturaId, s2.AsignaturaId)) };

            var r = Ceder(sesiones, criterios);

            Assert.False(r.Cedio);
            Assert.All(sesiones, s => Assert.Equal(TipoAlternancia.SinAlternancia, s.Alternancia));
        }

        [Fact]
        public void Empareja_CuandoAsignaturaYGrupoDifieren_ConTiposOpuestosYMismaPareja()
        {
            var asigA = Guid.NewGuid(); var grupoX = Guid.NewGuid();
            var asigB = Guid.NewGuid(); var grupoY = Guid.NewGuid();
            // 2 por lado: el invariante "≥1 presencial restante por (asignatura, grupo)" es montaje.
            var s1a = PresLab(asigA, grupoX); var s1b = PresLab(asigA, grupoX);
            var s2a = PresLab(asigB, grupoY); var s2b = PresLab(asigB, grupoY);
            var sesiones = new List<Sesion> { s1a, s1b, s2a, s2b };
            var criterios = new List<(CriterioElegibilidadAlternancia, Func<Sesion, bool>)>
                { CriterioElectiva(Electivas(asigA, asigB)) };

            var r = Ceder(sesiones, criterios);

            Assert.True(r.Cedio);
            var cedidaA = new[] { s1a, s1b }.Single(s => s.Alternancia != TipoAlternancia.SinAlternancia);
            var cedidaB = new[] { s2a, s2b }.Single(s => s.Alternancia != TipoAlternancia.SinAlternancia);
            Assert.NotEqual(cedidaA.Alternancia, cedidaB.Alternancia);
            Assert.NotNull(cedidaA.ParejaAlternanciaId);
            Assert.Equal(cedidaA.ParejaAlternanciaId, cedidaB.ParejaAlternanciaId);
        }

        [Fact]
        public void Empareja_TambienTeoriaPresencial_NoSoloLaboratorio()
        {
            // La vía reactiva antes solo aceptaba laboratorios: un cuello de botella de salones no
            // tenía salida. Ahora la teoría presencial elegible también puede alternar.
            var asigA = Guid.NewGuid(); var grupoX = Guid.NewGuid();
            var asigB = Guid.NewGuid(); var grupoY = Guid.NewGuid();
            var s1a = Pres(asigA, grupoX); var s1b = Pres(asigA, grupoX);
            var s2a = Pres(asigB, grupoY); var s2b = Pres(asigB, grupoY);
            var sesiones = new List<Sesion> { s1a, s1b, s2a, s2b };
            var criterios = new List<(CriterioElegibilidadAlternancia, Func<Sesion, bool>)>
                { CriterioElectiva(Electivas(asigA, asigB)) };

            var r = Ceder(sesiones, criterios);

            Assert.True(r.Cedio);
            Assert.Equal(TipoFlujo.AulaVirtual, r.S1!.TipoFlujo);
        }

        [Fact]
        public void NoEmpareja_SinFranjaComun_YLoExplica()
        {
            // Bioquímica solo puede en los bloques 0-1, Química Orgánica solo en 8-9.
            var asigA = Guid.NewGuid(); var asigB = Guid.NewGuid();
            var s1a = Pres(asigA, Guid.NewGuid()); var s1b = Pres(asigA, s1a.GrupoId!.Value);
            var s2a = Pres(asigB, Guid.NewGuid()); var s2b = Pres(asigB, s2a.GrupoId!.Value);
            var sesiones = new List<Sesion> { s1a, s1b, s2a, s2b };
            var criterios = new List<(CriterioElegibilidadAlternancia, Func<Sesion, bool>)>
                { CriterioElectiva(Electivas(asigA, asigB)) };

            var dominios = new Dictionary<Guid, int[]>
            {
                [s1a.Id] = new[] { 0, 1 }, [s1b.Id] = new[] { 0, 1 },
                [s2a.Id] = new[] { 8, 9 }, [s2b.Id] = new[] { 8, 9 }
            };

            var r = Ceder(sesiones, criterios, Ctx(sesiones, dominios));

            Assert.False(r.Cedio);
            Assert.Contains(r.Diagnostico, m => m.Contains("franja", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void NoEmpareja_SinAulaComun_YLoExplica()
        {
            var asigA = Guid.NewGuid(); var asigB = Guid.NewGuid();
            var s1a = Pres(asigA, Guid.NewGuid()); var s1b = Pres(asigA, s1a.GrupoId!.Value);
            var s2a = Pres(asigB, Guid.NewGuid()); var s2b = Pres(asigB, s2a.GrupoId!.Value);
            var sesiones = new List<Sesion> { s1a, s1b, s2a, s2b };
            var criterios = new List<(CriterioElegibilidadAlternancia, Func<Sesion, bool>)>
                { CriterioElectiva(Electivas(asigA, asigB)) };

            var aulas = new Dictionary<Guid, IReadOnlyList<int>>
            {
                [s1a.Id] = new[] { 0 }, [s1b.Id] = new[] { 0 },
                [s2a.Id] = new[] { 1 }, [s2b.Id] = new[] { 1 }
            };

            var r = Ceder(sesiones, criterios, Ctx(sesiones, aulas: aulas));

            Assert.False(r.Cedio);
            Assert.Contains(r.Diagnostico, m => m.Contains("aula", StringComparison.OrdinalIgnoreCase));
        }

        // ── Invariantes de seguridad ────────────────────────────────────────────────

        [Fact]
        public void UnicaSesionDeSuGrupo_SiPuedeCeder_PorqueAlternarSigueSiendoPresencial()
        {
            // Ceder ya no virtualiza: la sesión sigue siendo presencial una semana de cada dos, que
            // es el modelo de alternancia de la institución. Vetar la última sesión de una materia
            // (herencia de cuando ceder la volvía virtual para siempre) dejaba sin salida a los
            // horarios que la alternancia sí resuelve.
            var asigA = Guid.NewGuid(); var asigB = Guid.NewGuid();
            var sesiones = new List<Sesion>
            {
                PresLab(asigA, Guid.NewGuid()),   // única de su (asignatura, grupo)
                PresLab(asigB, Guid.NewGuid())    // única de su (asignatura, grupo)
            };
            var criterios = new List<(CriterioElegibilidadAlternancia, Func<Sesion, bool>)>
                { CriterioElectiva(Electivas(asigA, asigB)) };

            var r = Ceder(sesiones, criterios);

            Assert.True(r.Cedio);
        }

        [Fact]
        public void PrefiereCederUnaSesionConHermanas_AntesQueLaUnicaDeSuGrupo()
        {
            // La cuenta de hermanas es una PREFERENCIA de orden, no un veto: a igual criterio,
            // cede antes quien deja otra sesión presencial pura en su (asignatura, grupo).
            var asig = Guid.NewGuid();
            var grupoConVarias = Guid.NewGuid();
            var otraAsig = Guid.NewGuid();
            var unica = Pres(asig, Guid.NewGuid());
            var sesiones = new List<Sesion>
            {
                Pres(asig, grupoConVarias), Pres(asig, grupoConVarias),
                unica,
                Pres(otraAsig, Guid.NewGuid()), Pres(otraAsig, Guid.NewGuid())
            };
            var criterios = new List<(CriterioElegibilidadAlternancia, Func<Sesion, bool>)>
                { CriterioElectiva(Electivas(asig, otraAsig)) };

            var r = Ceder(sesiones, criterios);

            Assert.True(r.Cedio);
            Assert.Equal(TipoAlternancia.SinAlternancia, unica.Alternancia);
        }

        [Fact]
        public void ExcluyeSesionesFijasDelHorarioBase()
        {
            var asigA = Guid.NewGuid(); var asigB = Guid.NewGuid();
            var fija = PresLab(asigA, Guid.NewGuid());
            var sesiones = new List<Sesion>
            {
                fija, PresLab(asigA, fija.GrupoId!.Value),
                PresLab(asigB, Guid.NewGuid())
            };
            var criterios = new List<(CriterioElegibilidadAlternancia, Func<Sesion, bool>)>
                { CriterioElectiva(Electivas(asigA, asigB)) };

            Ceder(sesiones, criterios, fijas: new HashSet<Guid> { fija.Id });

            Assert.Equal(TipoAlternancia.SinAlternancia, fija.Alternancia);
        }

        [Fact]
        public void SesionBloqueada_NoCede()
        {
            var asigA = Guid.NewGuid(); var asigB = Guid.NewGuid();
            var bloqueada = Pres(asigA, Guid.NewGuid(), bloqueada: true);
            var sesiones = new List<Sesion>
            {
                bloqueada, Pres(asigA, bloqueada.GrupoId!.Value),
                Pres(asigB, Guid.NewGuid())
            };
            var criterios = new List<(CriterioElegibilidadAlternancia, Func<Sesion, bool>)>
                { CriterioElectiva(Electivas(asigA, asigB)) };

            Ceder(sesiones, criterios);

            Assert.Equal(TipoAlternancia.SinAlternancia, bloqueada.Alternancia);
        }

        [Fact]
        public void SinCriteriosActivos_NoCedeNada()
        {
            var sesiones = new List<Sesion>
            {
                Pres(Guid.NewGuid(), Guid.NewGuid()), Pres(Guid.NewGuid(), Guid.NewGuid())
            };

            var r = Ceder(sesiones, new List<(CriterioElegibilidadAlternancia, Func<Sesion, bool>)>());

            Assert.False(r.Cedio);
            Assert.All(sesiones, s => Assert.Equal(TipoAlternancia.SinAlternancia, s.Alternancia));
        }

        [Fact]
        public void MultiplesSesiones_NoOtorgaElegibilidadPorSiSola()
        {
            var asigA = Guid.NewGuid(); var asigB = Guid.NewGuid();
            var sesiones = new List<Sesion>
            {
                Pres(asigA, Guid.NewGuid()), Pres(asigA, Guid.NewGuid()),
                Pres(asigB, Guid.NewGuid()), Pres(asigB, Guid.NewGuid())
            };
            var criterios = new List<(CriterioElegibilidadAlternancia, Func<Sesion, bool>)>
            {
                (CriterioElegibilidadAlternancia.MultiplesSesiones, _ => true)
            };

            var r = Ceder(sesiones, criterios);

            Assert.False(r.Cedio);
        }

        [Fact]
        public void RespetaElOrdenDeCriterios()
        {
            var electiva = Guid.NewGuid();
            var elegible = Guid.NewGuid();
            var grupoE = Guid.NewGuid(); var grupoG = Guid.NewGuid();
            var otra = Guid.NewGuid();

            var sesiones = new List<Sesion>
            {
                Pres(electiva, grupoE), Pres(electiva, grupoE),
                Pres(elegible, grupoG), Pres(elegible, grupoG),
                Pres(otra, Guid.NewGuid()), Pres(otra, Guid.NewGuid())
            };

            // Elegible antes que Electiva ⇒ la sesión "elegible" cede primero.
            var criterios = new List<(CriterioElegibilidadAlternancia, Func<Sesion, bool>)>
            {
                CriterioElegible(new HashSet<Guid> { elegible }),
                CriterioElectiva(Electivas(electiva))
            };

            var r = Ceder(sesiones, criterios);

            Assert.True(r.Cedio);
            Assert.Equal(elegible, r.S1!.AsignaturaId);
        }

        [Fact]
        public void UnSoloCandidato_NoCede_YLoExplica()
        {
            var asig = Guid.NewGuid();
            var sesiones = new List<Sesion> { Pres(asig, Guid.NewGuid()) };
            var criterios = new List<(CriterioElegibilidadAlternancia, Func<Sesion, bool>)>
                { CriterioElectiva(Electivas(asig)) };

            var r = Ceder(sesiones, criterios);

            Assert.False(r.Cedio);
            Assert.NotEmpty(r.Diagnostico);
        }

        [Fact]
        public void ParejaDescartada_NoSeVuelveAProponer()
        {
            // El bucle descarta una pareja que volvió el modelo infactible por otra causa y sigue
            // con la siguiente combinación en vez de abandonar.
            var asigA = Guid.NewGuid(); var asigB = Guid.NewGuid(); var asigC = Guid.NewGuid();
            var a1 = Pres(asigA, Guid.NewGuid()); var a2 = Pres(asigA, a1.GrupoId!.Value);
            var b1 = Pres(asigB, Guid.NewGuid()); var b2 = Pres(asigB, b1.GrupoId!.Value);
            var c1 = Pres(asigC, Guid.NewGuid()); var c2 = Pres(asigC, c1.GrupoId!.Value);
            var sesiones = new List<Sesion> { a1, a2, b1, b2, c1, c2 };
            var criterios = new List<(CriterioElegibilidadAlternancia, Func<Sesion, bool>)>
                { CriterioElectiva(Electivas(asigA, asigB, asigC)) };

            var descartadas = new HashSet<(Guid, Guid)>
            {
                GenerarHorarioService.ClaveDePareja(a1.Id, b1.Id)
            };

            var r = GenerarHorarioService.CederSiguientePareja(
                sesiones, criterios, new HashSet<Guid>(), Ctx(sesiones), descartadas);

            Assert.True(r.Cedio);
            Assert.False(r.S1!.Id == a1.Id && r.S2!.Id == b1.Id);
        }

        [Fact]
        public void ClaveDePareja_EsSimetrica()
        {
            var a = Guid.NewGuid(); var b = Guid.NewGuid();
            Assert.Equal(GenerarHorarioService.ClaveDePareja(a, b), GenerarHorarioService.ClaveDePareja(b, a));
        }
    }
}
