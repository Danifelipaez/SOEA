using System;
using SOEA.Domain.Enums;
using SOEA.Domain.ValueObjects;
using Xunit;

namespace SOEA.Tests.Domain.ValueObjects
{
    /// <summary>
    /// A1: fuente única de "¿puede este bloque recibir clase?" para Docente y Grupo, ahora
    /// derivada de un solo parser (antes vivía sólo para docentes, con un bug de acento que
    /// hacía que "Franja específica" nunca calzara — <see cref="DesdeJson_FranjaEspecifica_RespetaVentana"/>
    /// prueba que el caso que antes fallaba en silencio ahora sí restringe).
    /// </summary>
    public class DisponibilidadSemanalTests
    {
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("no es json")]
        public void DesdeJson_SinDatosOMalformado_EsSinRestriccion(string? json)
        {
            var disp = DisponibilidadSemanal.DesdeJson(json);

            Assert.True(disp.PermiteBloque(DiaDeSemana.Lunes, new TimeOnly(3, 0), new TimeOnly(4, 0)));
            Assert.True(disp.PermiteBloque(DiaDeSemana.Sábado, new TimeOnly(21, 0), new TimeOnly(22, 0)));
        }

        [Fact]
        public void DesdeJson_DiaNoDisponible_RechazaCualquierBloqueDeEseDia()
        {
            var json = """{"lunes":{"noDisponible":true}}""";
            var disp = DisponibilidadSemanal.DesdeJson(json);

            Assert.False(disp.PermiteBloque(DiaDeSemana.Lunes, new TimeOnly(8, 0), new TimeOnly(9, 0)));
        }

        [Fact]
        public void DesdeJson_DiaSinEntrada_PermiteCualquierBloque()
        {
            // Sólo lunes está declarado; martes no aparece → sin información, se permite.
            var json = """{"lunes":{"noDisponible":true}}""";
            var disp = DisponibilidadSemanal.DesdeJson(json);

            Assert.True(disp.PermiteBloque(DiaDeSemana.Martes, new TimeOnly(20, 0), new TimeOnly(21, 0)));
        }

        [Fact]
        public void DesdeJson_FranjaGeneralMatutino_SoloPermiteAntesDeLas13()
        {
            var json = """{"lunes":{"noDisponible":false,"tipo":"Franja general","franjaGeneral":"Matutino (06:00–12:00)"}}""";
            var disp = DisponibilidadSemanal.DesdeJson(json);

            Assert.True(disp.PermiteBloque(DiaDeSemana.Lunes, new TimeOnly(7, 0), new TimeOnly(8, 0)));
            Assert.False(disp.PermiteBloque(DiaDeSemana.Lunes, new TimeOnly(14, 0), new TimeOnly(15, 0)));
        }

        // Bug de acento (R3c): el parser viejo de horario-api.service.ts comparaba "especific"
        // (sin tilde) contra "Franja específica" (con tilde) y nunca calzaba. La comparación
        // exacta de esta clase sí reconoce el tipo tal como lo manda la UI.
        [Fact]
        public void DesdeJson_FranjaEspecifica_RespetaVentana()
        {
            var json = """{"lunes":{"noDisponible":false,"tipo":"Franja específica","desde":"14:00","hasta":"16:00"}}""";
            var disp = DisponibilidadSemanal.DesdeJson(json);

            Assert.True(disp.PermiteBloque(DiaDeSemana.Lunes, new TimeOnly(14, 0), new TimeOnly(15, 0)));
            Assert.False(disp.PermiteBloque(DiaDeSemana.Lunes, new TimeOnly(9, 0), new TimeOnly(10, 0)));
            Assert.False(disp.PermiteBloque(DiaDeSemana.Lunes, new TimeOnly(16, 0), new TimeOnly(17, 0)));
        }

        [Fact]
        public void ComoFranjasCoarse_SinRestriccion_DevuelveListaVacia()
        {
            Assert.Empty(DisponibilidadSemanal.SinRestriccion.ComoFranjasCoarse());
        }

        [Fact]
        public void ComoFranjasCoarse_SoloNocturno_NoIncluyeMatutino()
        {
            // Nocturno (18:00–22:00) no cruza mediodía, a diferencia de Matutino (06:00–13:00,
            // ventana fija histórica que sí toca la hora 12 — ver VentanaDe).
            var json = """{"lunes":{"noDisponible":false,"tipo":"Franja general","franjaGeneral":"Nocturno (18:00–22:00)"}}""";
            var disp = DisponibilidadSemanal.DesdeJson(json);

            var franjas = disp.ComoFranjasCoarse();
            Assert.Contains(FranjaHoraria.Vespertino, franjas);
            Assert.DoesNotContain(FranjaHoraria.Matutino, franjas);
        }

        // Verificación P1 (plan maestro): dos grupos con disponibilidad distinta sobre la misma
        // asignatura deben producir dominios de inicio distintos — antes la disponibilidad del
        // grupo nunca llegaba al motor (R3), así que este caso no podía diferir.
        [Fact]
        public void DosGruposConDisponibilidadDistinta_ProducenDominiosDistintos()
        {
            var grupoMatutino = DisponibilidadSemanal.DesdeJson(
                """{"lunes":{"noDisponible":false,"tipo":"Franja general","franjaGeneral":"Matutino (06:00–12:00)"}}""");
            var grupoVespertino = DisponibilidadSemanal.DesdeJson(
                """{"lunes":{"noDisponible":false,"tipo":"Franja general","franjaGeneral":"Vespertino (12:00–18:00)"}}""");

            var bloque = (new TimeOnly(14, 0), new TimeOnly(15, 0));

            Assert.False(grupoMatutino.PermiteBloque(DiaDeSemana.Lunes, bloque.Item1, bloque.Item2));
            Assert.True(grupoVespertino.PermiteBloque(DiaDeSemana.Lunes, bloque.Item1, bloque.Item2));
        }
    }
}
