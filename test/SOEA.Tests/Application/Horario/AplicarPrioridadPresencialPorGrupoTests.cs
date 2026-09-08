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
    /// Bug confirmado y corregido: en el Pase 1 (alternancia por parejas) de
    /// <see cref="GenerarHorarioService.AplicarPrioridadPresencial"/>, `PuedeCeder` indexaba
    /// `totalPorAsig`/`presencialesPurasPorAsig` SOLO por AsignaturaId, no por (AsignaturaId,
    /// GrupoId) — a diferencia de `PuedeCederLab` en `CederSiguienteCandidatoLab`, cuyo propio
    /// comentario dice ser "espejo de PuedeCeder en AplicarPrioridadPresencial" (nunca lo fue).
    /// Si dos grupos comparten una asignatura, sus sesiones se sumaban juntas: un grupo con una
    /// única sesión de esa asignatura podía calificar como candidato a ALTERNANCIA (Pase 1) solo
    /// porque OTRO grupo tenía otra sesión de la misma materia — emparejando la única sesión
    /// presencial de ese grupo en una rotación quincenal, pese a que el propio comentario del
    /// método (línea ~894) dice que Pase 1 debe conservar "SIEMPRE ≥1 sesión presencial pura por
    /// asignatura". Fix: indexar por (AsignaturaId, GrupoId), igual que en CederSiguienteCandidatoLab.
    /// </summary>
    public class AplicarPrioridadPresencialPorGrupoTests
    {
        private static Sesion Pres(Guid asig, Guid grupo, decimal dur) =>
            new(Guid.NewGuid(), asig, null, Guid.NewGuid(), null, grupo,
                TipoAlternancia.SinAlternancia, Modalidad.Presencial, dur, false, false,
                tipoFlujo: TipoFlujo.AulaVirtual);

        [Fact]
        public void GrupoConUnaSolaSesionDeLaAsignatura_NoEntraEnAlternanciaPorPase1_AunqueOtroGrupoComparteLaMateria()
        {
            var grupoA = Guid.NewGuid(); // 1 sola sesión de la Asignatura X
            var grupoB = Guid.NewGuid(); // otra sesión de la misma Asignatura X (infla el total compartido)
            var grupoD = Guid.NewGuid(); // 2 sesiones de la Asignatura Y: pareja de espacio/duración compatible

            var asigX = Guid.NewGuid(); // Electiva
            var asigY = Guid.NewGuid(); // Electiva

            var espacios = new List<Espacio> { new(Guid.NewGuid(), "E", TipoEspacio.Salon, 30) };

            // Filler de otra asignatura (no candidata): 39h. Sumado a X (1h+1h=2h) + Y (1h+1h=2h) =
            // 43h > 40h umbral ⇒ exceso = 3h, suficiente para que el Pase 1 intente al menos una pareja.
            var filler = new[] { 8m, 8m, 8m, 8m, 7m }.Select(h => Pres(Guid.NewGuid(), grupoA, h)).ToList();

            var sesionUnicaGrupoA = Pres(asigX, grupoA, 1m);
            var sesionGrupoB = Pres(asigX, grupoB, 1m);
            var sesionesGrupoD = new List<Sesion> { Pres(asigY, grupoD, 1m), Pres(asigY, grupoD, 1m) };

            var sesiones = new List<Sesion>();
            sesiones.AddRange(filler);
            sesiones.Add(sesionUnicaGrupoA);
            sesiones.Add(sesionGrupoB);
            sesiones.AddRange(sesionesGrupoD);

            var categoria = new Dictionary<Guid, CategoriaAsignatura>
            {
                [asigX] = CategoriaAsignatura.Electiva,
                [asigY] = CategoriaAsignatura.Electiva
            };
            var predicados = new List<(CriterioElegibilidadAlternancia, Func<Sesion, bool>)>
            {
                (CriterioElegibilidadAlternancia.Electiva,
                 s => categoria.TryGetValue(s.AsignaturaId, out var cat) && cat == CategoriaAsignatura.Electiva)
            };

            GenerarHorarioService.AplicarPrioridadPresencial(sesiones, espacios, predicados);

            // Con el bug: totalPorAsig[X] = 2 (grupoA + grupoB) satisfacía "n >= 2", así que la
            // única sesión de grupoA calificaba como candidata a emparejarse (alternancia) con una
            // sesión de grupoD (asignatura Y, duración compatible) — perdiendo su única sesión
            // presencial de X a una rotación quincenal. Con el fix, totalPorAsigYGrupo[(X, grupoA)]
            // = 1, así que nunca califica para el Pase 1.
            Assert.Equal(TipoAlternancia.SinAlternancia, sesionUnicaGrupoA.Alternancia);
            Assert.Null(sesionUnicaGrupoA.ParejaAlternanciaId);
        }
    }
}
