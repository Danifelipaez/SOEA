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
    /// Verifica la prioridad de cesión presencial-first de <see cref="GenerarHorarioService.AplicarPrioridadPresencial"/>
    /// bajo saturación de aforo (heurística de pre-pase): el primer criterio activo (en el orden
    /// configurado) que matchea la sesión gana. Una sesión que no matchea NINGÚN criterio de
    /// elegibilidad (Electiva/Optativa/Elegible) nunca es candidata (sin regla implícita). El criterio
    /// "MultiplesSesiones" es la excepción: no otorga elegibilidad por sí solo, solo desempata el orden
    /// entre sesiones ya elegibles por otro criterio (reproduce la prioridad estructural histórica —
    /// 2+ sesiones/semana antes que sesión única — cuando está activo y en primer lugar).
    /// La cesión "Tipo C" dinámica alterna (presencial 1 semana) en vez de virtualizar; respeta Bloqueada.
    /// </summary>
    public class PresencialFirstTests
    {
        // TipoFlujo.AulaVirtual (teoría): tras el desglose por tipo de sesión, la Etapa 1 solo cede
        // presencialidad en el track de teoría — el de laboratorio lo cubre la Etapa 2 (reactiva).
        private static Sesion Pres(Guid asig, Guid grupo, decimal dur, bool bloqueada = false) =>
            new(Guid.NewGuid(), asig, null, Guid.NewGuid(), null, grupo,
                TipoAlternancia.SinAlternancia, Modalidad.Presencial, dur, false, false,
                tipoFlujo: TipoFlujo.AulaVirtual, bloqueada: bloqueada);

        private static Sesion PresLab(Guid asig, Guid grupo, decimal dur) =>
            new(Guid.NewGuid(), asig, null, Guid.NewGuid(), null, grupo,
                TipoAlternancia.SinAlternancia, Modalidad.Presencial, dur, false, false,
                tipoFlujo: TipoFlujo.Laboratorio);

        private static (CriterioElegibilidadAlternancia, Func<Sesion, bool>) CriterioElectiva(Dictionary<Guid, CategoriaAsignatura> categoriaPorAsig) =>
            (CriterioElegibilidadAlternancia.Electiva,
             s => categoriaPorAsig.TryGetValue(s.AsignaturaId, out var cat) && cat == CategoriaAsignatura.Electiva);

        private static (CriterioElegibilidadAlternancia, Func<Sesion, bool>) CriterioElegible(HashSet<Guid> elegibles) =>
            (CriterioElegibilidadAlternancia.Elegible, s => elegibles.Contains(s.AsignaturaId));

        private static (CriterioElegibilidadAlternancia, Func<Sesion, bool>) CriterioMultiplesSesiones(Dictionary<Guid, int> totalPorAsig) =>
            (CriterioElegibilidadAlternancia.MultiplesSesiones,
             s => totalPorAsig.TryGetValue(s.AsignaturaId, out var n) && n >= 2);

        [Fact]
        public void Cesion_RespetaPrioridadDosNiveles_YBloqueada()
        {
            var grupo = Guid.NewGuid();
            var asigFiller = Guid.NewGuid(); // Marcada "Elegible" (rank 1), multi: absorbe el resto del exceso
            var asigEle    = Guid.NewGuid(); // Electiva (rank 0), 2 sesiones: candidata primaria
            var asigObl    = Guid.NewGuid(); // Obligatoria, 1 sesión: no matchea ningún criterio, no cede
            var asigBlk    = Guid.NewGuid(); // Electiva, 2 sesiones bloqueadas: nunca se tocan

            // 1 espacio → umbral de saturación = 1*5*8 = 40h. Demanda > 40h dispara la cesión.
            var espacios = new List<Espacio> { new(Guid.NewGuid(), "E", TipoEspacio.Laboratorio, 30) };

            var filler = Enumerable.Range(0, 5).Select(_ => Pres(asigFiller, grupo, 8m)).ToList(); // 40h
            var ele    = new List<Sesion> { Pres(asigEle, grupo, 1m), Pres(asigEle, grupo, 1m) };
            var obl    = Pres(asigObl, grupo, 1m);
            var blk    = new List<Sesion> { Pres(asigBlk, grupo, 1m, bloqueada: true),
                                            Pres(asigBlk, grupo, 1m, bloqueada: true) };

            var sesiones = new List<Sesion>();
            sesiones.AddRange(filler);
            sesiones.AddRange(ele);
            sesiones.Add(obl);
            sesiones.AddRange(blk);

            var categoria = new Dictionary<Guid, CategoriaAsignatura>
            {
                [asigEle] = CategoriaAsignatura.Electiva,
                [asigBlk] = CategoriaAsignatura.Electiva
            };
            // Criterio "Electiva" tiene prioridad 0 (rank más bajo = cede primero); "Elegible" es rank 1
            // — asigFiller solo matchea Elegible, así que ele SIEMPRE cede antes que filler.
            var elegibles = new HashSet<Guid> { asigFiller };
            var predicados = new List<(CriterioElegibilidadAlternancia, Func<Sesion, bool>)>
                { CriterioElectiva(categoria), CriterioElegible(elegibles) };

            var cedidasIds = GenerarHorarioService.AplicarPrioridadPresencial(sesiones, espacios, predicados);

            Assert.True(cedidasIds.Count > 0);

            // La Electiva "ele" (2 sesiones) cede exactamente UNA por alternancia; conserva ≥1 presencial.
            Assert.Equal(1, ele.Count(s => s.Alternancia != TipoAlternancia.SinAlternancia));
            Assert.Equal(1, ele.Count(s => s.Alternancia == TipoAlternancia.SinAlternancia));
            // Y queda marcada como cedida por saturación (no por elección manual del usuario).
            Assert.Contains(ele, s => s.CedidaPorSaturacion);

            // La Obligatoria de sesión única no matchea ningún criterio activo: NUNCA es candidata.
            Assert.Equal(TipoAlternancia.SinAlternancia, obl.Alternancia);
            Assert.Equal(Modalidad.Presencial, obl.Modalidad);
            Assert.False(obl.CedidaPorSaturacion);

            // Las sesiones bloqueadas quedan intactas pese a ser Electivas multi (candidatas ideales).
            Assert.All(blk, s =>
            {
                Assert.Equal(TipoAlternancia.SinAlternancia, s.Alternancia);
                Assert.Equal(Modalidad.Presencial, s.Modalidad);
            });
        }

        [Fact]
        public void SinCriteriosActivos_NoCedeNada()
        {
            var grupo = Guid.NewGuid();
            var asig = Guid.NewGuid();
            var espacios = new List<Espacio> { new(Guid.NewGuid(), "E", TipoEspacio.Laboratorio, 30) };
            // Demanda muy por encima del umbral (40h), pero sin criterios activos.
            var sesiones = Enumerable.Range(0, 6).Select(_ => Pres(asig, grupo, 8m)).ToList(); // 48h > 40h

            var cedidasIds = GenerarHorarioService.AplicarPrioridadPresencial(
                sesiones, espacios, Array.Empty<(CriterioElegibilidadAlternancia, Func<Sesion, bool>)>());

            Assert.Empty(cedidasIds);
            Assert.All(sesiones, s => Assert.Equal(TipoAlternancia.SinAlternancia, s.Alternancia));
        }

        [Fact]
        public void SinSaturacion_NoCedeNada()
        {
            var grupo = Guid.NewGuid();
            var asig = Guid.NewGuid();
            var espacios = new List<Espacio> { new(Guid.NewGuid(), "E", TipoEspacio.Laboratorio, 30) };
            var sesiones = new List<Sesion> { Pres(asig, grupo, 2m) }; // 2h << 40h umbral
            var categoria = new Dictionary<Guid, CategoriaAsignatura> { [asig] = CategoriaAsignatura.Electiva };
            var predicados = new List<(CriterioElegibilidadAlternancia, Func<Sesion, bool>)> { CriterioElectiva(categoria) };

            var cedidasIds = GenerarHorarioService.AplicarPrioridadPresencial(sesiones, espacios, predicados);

            Assert.Empty(cedidasIds);
            Assert.All(sesiones, s => Assert.Equal(TipoAlternancia.SinAlternancia, s.Alternancia));
        }

        [Fact]
        public void OrdenDeCriteriosInvertido_CambiaQuienCedePrimero()
        {
            var grupo = Guid.NewGuid();
            var asigEle = Guid.NewGuid(); // Electiva, sesión única
            var asigElg = Guid.NewGuid(); // Marcada "Elegible" (no Electiva), sesión única

            var espacios = new List<Espacio> { new(Guid.NewGuid(), "E", TipoEspacio.Laboratorio, 30) };
            // Filler sin criterio (nunca candidata) + ele/elg de 1h cada una ⇒ demanda = 41h,
            // 1h por encima del umbral (40h): el exceso alcanza para ceder EXACTAMENTE una de las dos.
            var filler = new[] { 8m, 8m, 8m, 8m, 7m }.Select(h => Pres(Guid.NewGuid(), grupo, h)).ToList(); // 39h
            var ele = Pres(asigEle, grupo, 1m);
            var elg = Pres(asigElg, grupo, 1m);

            var categoria = new Dictionary<Guid, CategoriaAsignatura>
            {
                [asigEle] = CategoriaAsignatura.Electiva,
                [asigElg] = CategoriaAsignatura.Obligatoria
            };
            var elegibles = new HashSet<Guid> { asigElg };

            // Orden A: Electiva primero ⇒ cede la Electiva, la Elegible queda intacta.
            var sesionesA = new List<Sesion>(filler.Select(Clonar)) { Clonar(ele), Clonar(elg) };
            GenerarHorarioService.AplicarPrioridadPresencial(sesionesA, espacios,
                new List<(CriterioElegibilidadAlternancia, Func<Sesion, bool>)>
                    { CriterioElectiva(categoria), CriterioElegible(elegibles) });
            Assert.True(sesionesA.Single(s => s.AsignaturaId == asigEle).CedidaPorSaturacion);
            Assert.False(sesionesA.Single(s => s.AsignaturaId == asigElg).CedidaPorSaturacion);

            // Orden B: Elegible primero — el resultado se invierte.
            var sesionesB = new List<Sesion>(filler.Select(Clonar)) { Clonar(ele), Clonar(elg) };
            GenerarHorarioService.AplicarPrioridadPresencial(sesionesB, espacios,
                new List<(CriterioElegibilidadAlternancia, Func<Sesion, bool>)>
                    { CriterioElegible(elegibles), CriterioElectiva(categoria) });
            Assert.True(sesionesB.Single(s => s.AsignaturaId == asigElg).CedidaPorSaturacion);
            Assert.False(sesionesB.Single(s => s.AsignaturaId == asigEle).CedidaPorSaturacion);
        }

        [Fact]
        public void Laboratorio_NuncaSeAlternaNiVirtualiza()
        {
            var grupo = Guid.NewGuid();
            var asigLab = Guid.NewGuid();

            // 1 espacio → umbral de saturación = 40h. Todas las sesiones son de laboratorio,
            // así que ninguna es candidata a cesión en la Etapa 1 (los labs los cubre la Etapa 2 reactiva).
            var espacios = new List<Espacio> { new(Guid.NewGuid(), "E", TipoEspacio.Laboratorio, 30) };
            var labs = Enumerable.Range(0, 6).Select(_ => PresLab(asigLab, grupo, 8m)).ToList(); // 48h > 40h
            var categoria = new Dictionary<Guid, CategoriaAsignatura> { [asigLab] = CategoriaAsignatura.Electiva };
            var predicados = new List<(CriterioElegibilidadAlternancia, Func<Sesion, bool>)> { CriterioElectiva(categoria) };

            var cedidasIds = GenerarHorarioService.AplicarPrioridadPresencial(labs, espacios, predicados);

            Assert.Empty(cedidasIds);
            Assert.All(labs, s =>
            {
                Assert.Equal(TipoAlternancia.SinAlternancia, s.Alternancia);
                Assert.Equal(Modalidad.Presencial, s.Modalidad);
            });
        }

        [Fact]
        public void CriterioOptativa_FuncionaIgualQueElectiva()
        {
            var grupo = Guid.NewGuid();
            var asigOpt = Guid.NewGuid();
            var espacios = new List<Espacio> { new(Guid.NewGuid(), "E", TipoEspacio.Laboratorio, 30) };
            var sesiones = Enumerable.Range(0, 6).Select(_ => Pres(asigOpt, grupo, 8m)).ToList(); // 48h > 40h
            var categoria = new Dictionary<Guid, CategoriaAsignatura> { [asigOpt] = CategoriaAsignatura.Optativa };
            var predicados = new List<(CriterioElegibilidadAlternancia, Func<Sesion, bool>)>
                { (CriterioElegibilidadAlternancia.Optativa,
                   s => categoria.TryGetValue(s.AsignaturaId, out var cat) && cat == CategoriaAsignatura.Optativa) };

            var cedidasIds = GenerarHorarioService.AplicarPrioridadPresencial(sesiones, espacios, predicados);

            Assert.NotEmpty(cedidasIds);
        }

        [Fact]
        public void MultiplesSesiones_NoOtorgaElegibilidadPorSiSola()
        {
            var grupo = Guid.NewGuid();
            var asig = Guid.NewGuid(); // Obligatoria, 2 sesiones/sem: NO matchea ningún criterio real.
            var espacios = new List<Espacio> { new(Guid.NewGuid(), "E", TipoEspacio.Laboratorio, 30) };
            var sesiones = Enumerable.Range(0, 6).Select(_ => Pres(asig, grupo, 8m)).ToList(); // 48h > 40h, todas de la misma asignatura (multi)
            var totalPorAsig = new Dictionary<Guid, int> { [asig] = 6 };
            var predicados = new List<(CriterioElegibilidadAlternancia, Func<Sesion, bool>)>
                { CriterioMultiplesSesiones(totalPorAsig) };

            var cedidasIds = GenerarHorarioService.AplicarPrioridadPresencial(sesiones, espacios, predicados);

            Assert.Empty(cedidasIds);
            Assert.All(sesiones, s => Assert.Equal(TipoAlternancia.SinAlternancia, s.Alternancia));
        }

        [Fact]
        public void MultiplesSesiones_PriorizaEntreCandidatosYaElegibles()
        {
            var grupo = Guid.NewGuid();
            var asigMulti  = Guid.NewGuid(); // Elegible + 2 sesiones/sem
            var asigUnica  = Guid.NewGuid(); // Electiva + sesión única

            var espacios = new List<Espacio> { new(Guid.NewGuid(), "E", TipoEspacio.Laboratorio, 30) };
            // Filler sin criterio + multi/unica de 1h cada sesión ⇒ demanda = 41h, 1h sobre el umbral
            // (40h): el exceso alcanza para ceder EXACTAMENTE una sesión.
            var filler = new[] { 8m, 8m, 8m, 8m, 7m }.Select(h => Pres(Guid.NewGuid(), grupo, h)).ToList(); // 39h
            var multi = new List<Sesion> { Pres(asigMulti, grupo, 1m), Pres(asigMulti, grupo, 1m) };
            var unica = Pres(asigUnica, grupo, 1m);

            var categoria = new Dictionary<Guid, CategoriaAsignatura> { [asigUnica] = CategoriaAsignatura.Electiva };
            var elegibles = new HashSet<Guid> { asigMulti };
            var totalPorAsig = new Dictionary<Guid, int> { [asigMulti] = 2, [asigUnica] = 1 };

            var sesiones = new List<Sesion>(filler);
            sesiones.AddRange(multi);
            sesiones.Add(unica);

            // MultiplesSesiones primero ⇒ asigMulti (2 sesiones) cede antes que asigUnica (sesión única),
            // aunque Electiva tenga mejor rank "de criterio" que Elegible.
            var predicados = new List<(CriterioElegibilidadAlternancia, Func<Sesion, bool>)>
                { CriterioMultiplesSesiones(totalPorAsig), CriterioElectiva(categoria), CriterioElegible(elegibles) };

            GenerarHorarioService.AplicarPrioridadPresencial(sesiones, espacios, predicados);

            Assert.Contains(multi, s => s.CedidaPorSaturacion);
            Assert.False(unica.CedidaPorSaturacion);
        }

        // Clona una sesión "presencial pura" con el mismo AsignaturaId/GrupoId/Duración, para poder
        // correr dos veces AplicarPrioridadPresencial sobre datos independientes en el mismo test.
        private static Sesion Clonar(Sesion s) =>
            new(Guid.NewGuid(), s.AsignaturaId, null, Guid.NewGuid(), null, s.GrupoId,
                TipoAlternancia.SinAlternancia, Modalidad.Presencial, s.DuracionHoras, false, false,
                tipoFlujo: s.TipoFlujo);
    }
}
