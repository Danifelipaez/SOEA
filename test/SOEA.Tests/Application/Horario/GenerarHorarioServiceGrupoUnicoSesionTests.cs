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
    /// Un grupo con una única sesión total de una asignatura Electiva, bajo saturación de espacio,
    /// puede ser el último recurso de cesión del Pase 2 de
    /// <see cref="GenerarHorarioService.AplicarPrioridadPresencial"/> (VirtualizarSesion) — por
    /// diseño, documentado en el propio método ("aquí sí pueden caer las sesiones únicas").
    /// Este test confirma que "ceder" nunca significa "desaparecer": la sesión sigue presente en
    /// la lista de sesiones (mismo objeto, mismo Id), solo con Modalidad=Virtual — nunca se
    /// remueve de la colección que después se persiste y se mapea a la respuesta HTTP.
    /// </summary>
    public class GenerarHorarioServiceGrupoUnicoSesionTests
    {
        private static Sesion Pres(Guid asig, Guid grupo, decimal dur) =>
            new(Guid.NewGuid(), asig, null, Guid.NewGuid(), null, grupo,
                TipoAlternancia.SinAlternancia, Modalidad.Presencial, dur, false, false,
                tipoFlujo: TipoFlujo.AulaVirtual);

        [Fact]
        public void GrupoConUnaSolaSesionEnTodoElRun_SiSeCede_QuedaVirtualNoDesaparece()
        {
            var grupoUnico = Guid.NewGuid();
            var asigX = Guid.NewGuid(); // única asignatura del grupo, Electiva

            var espacios = new List<Espacio> { new(Guid.NewGuid(), "E", TipoEspacio.Salon, 30) };

            // Filler de otro grupo/asignatura (Obligatoria, no candidata): 40h exactas del umbral.
            var grupoFiller = Guid.NewGuid();
            var filler = new[] { 8m, 8m, 8m, 8m, 8m }.Select(h => Pres(Guid.NewGuid(), grupoFiller, h)).ToList();

            // La única sesión de todo el run para grupoUnico: 1h, empuja la demanda a 41h > 40h.
            var sesionUnica = Pres(asigX, grupoUnico, 1m);

            var sesiones = new List<Sesion>(filler) { sesionUnica };
            var totalAntes = sesiones.Count;

            var categoria = new Dictionary<Guid, CategoriaAsignatura> { [asigX] = CategoriaAsignatura.Electiva };
            var predicados = new List<(CriterioElegibilidadAlternancia, Func<Sesion, bool>)>
            {
                (CriterioElegibilidadAlternancia.Electiva,
                 s => categoria.TryGetValue(s.AsignaturaId, out var cat) && cat == CategoriaAsignatura.Electiva)
            };

            var cedidasIds = GenerarHorarioService.AplicarPrioridadPresencial(sesiones, espacios, predicados);

            // Se cedió (es la única candidata posible: el filler no matchea ningún criterio).
            Assert.Contains(sesionUnica.Id, cedidasIds);
            // Pero sigue en la colección — "ceder" nunca es "remover".
            Assert.Equal(totalAntes, sesiones.Count);
            Assert.Contains(sesiones, s => s.Id == sesionUnica.Id);
            Assert.Equal(Modalidad.Virtual, sesionUnica.Modalidad);
            Assert.True(sesionUnica.CedidaPorSaturacion);
        }
    }
}
