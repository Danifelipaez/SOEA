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
    /// bajo saturación de aforo (heurística de pre-pase):
    ///   Nivel 1 (estructural): la 2ª sesión de una materia con 2 sesiones/sem cede antes que una
    ///     materia de sesión única (último recurso).
    ///   Nivel 2 (categoría): Electiva → Optativa → Obligatoria.
    /// La cesión "Tipo C" dinámica alterna (presencial 1 semana) en vez de virtualizar; respeta Bloqueada.
    /// </summary>
    public class PresencialFirstTests
    {
        private static Sesion Pres(Guid asig, Guid grupo, decimal dur, bool bloqueada = false) =>
            new(Guid.NewGuid(), asig, null, Guid.NewGuid(), null, grupo,
                TipoAlternancia.SinAlternancia, Modalidad.Presencial, dur, false, false,
                bloqueada: bloqueada);

        [Fact]
        public void Cesion_RespetaPrioridadDosNiveles_YBloqueada()
        {
            var grupo = Guid.NewGuid();
            var asigFiller = Guid.NewGuid(); // Obligatoria, multi: absorbe el exceso en pase 1
            var asigEle    = Guid.NewGuid(); // Electiva, 2 sesiones: candidata primaria
            var asigObl    = Guid.NewGuid(); // Obligatoria, 1 sesión: último recurso (no debe ceder)
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
                [asigFiller] = CategoriaAsignatura.Obligatoria,
                [asigEle]    = CategoriaAsignatura.Electiva,
                [asigObl]    = CategoriaAsignatura.Obligatoria,
                [asigBlk]    = CategoriaAsignatura.Electiva
            };

            int cedidas = GenerarHorarioService.AplicarPrioridadPresencial(sesiones, espacios, categoria);

            Assert.True(cedidas > 0);

            // La Electiva (2 sesiones) cede exactamente UNA por alternancia; conserva ≥1 presencial pura.
            Assert.Equal(1, ele.Count(s => s.Alternancia != TipoAlternancia.SinAlternancia));
            Assert.Equal(1, ele.Count(s => s.Alternancia == TipoAlternancia.SinAlternancia));

            // La Obligatoria de sesión única (último recurso) NO cede: sigue presencial pura.
            Assert.Equal(TipoAlternancia.SinAlternancia, obl.Alternancia);
            Assert.Equal(Modalidad.Presencial, obl.Modalidad);

            // Las sesiones bloqueadas quedan intactas pese a ser Electivas multi (candidatas ideales).
            Assert.All(blk, s =>
            {
                Assert.Equal(TipoAlternancia.SinAlternancia, s.Alternancia);
                Assert.Equal(Modalidad.Presencial, s.Modalidad);
            });
        }

        [Fact]
        public void SinSaturacion_NoCedeNada()
        {
            var grupo = Guid.NewGuid();
            var asig = Guid.NewGuid();
            var espacios = new List<Espacio> { new(Guid.NewGuid(), "E", TipoEspacio.Laboratorio, 30) };
            var sesiones = new List<Sesion> { Pres(asig, grupo, 2m) }; // 2h << 40h umbral
            var categoria = new Dictionary<Guid, CategoriaAsignatura> { [asig] = CategoriaAsignatura.Obligatoria };

            int cedidas = GenerarHorarioService.AplicarPrioridadPresencial(sesiones, espacios, categoria);

            Assert.Equal(0, cedidas);
            Assert.All(sesiones, s => Assert.Equal(TipoAlternancia.SinAlternancia, s.Alternancia));
        }
    }
}
