using System;
using System.Net.Mail;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;
using SOEA.Domain.ValueObjects;
using Xunit;

namespace SOEA.Tests.Domain
{
    /// <summary>Auditoría de escala (2026-09-29): datos que el CRUD aceptaba y después bloqueaban toda la generación.</summary>
    public class ValidacionesDeEntradaTests
    {
        private static Asignatura Crear(int horasTeoria = 2) => new(Guid.NewGuid(), "Física", "FIS-1",
            horasPorSesion: horasTeoria, sesionesPorSemana: 1, sesionesLaboratorioSemestre: 0, programaId: Guid.NewGuid());

        [Fact]
        public void Asignatura_HorasPorSesionMayorQue8_Rechaza() =>
            Assert.Throws<ArgumentException>(() => Crear(horasTeoria: Asignatura.HorasMaximasPorSesion + 1));

        [Fact]
        public void Asignatura_HorasPorSesionIgualA8_Acepta() => Crear(horasTeoria: Asignatura.HorasMaximasPorSesion);

        [Theory]
        [InlineData(99)]
        [InlineData(-1)]
        public void Asignatura_EnumFueraDeRango_Rechaza(int valor)
        {
            var a = Crear();
            Assert.Throws<ArgumentException>(() => a.EstablecerAlternancia((TipoAlternancia)valor));
            Assert.Throws<ArgumentException>(() => a.EstablecerCategoria((CategoriaAsignatura)valor));
        }

        [Theory]
        [InlineData("""{"Lunes":{"noDisponible":false,"tipo":"Franja específica","desde":"08:00","hasta":"12:00"}}""", true)]
        [InlineData("""{"Lunes":{"noDisponible":false,"tipo":"Franja específica","desde":"25:00","hasta":"12:00"}}""", false)]
        [InlineData("""{"Lunes":{"noDisponible":false,"tipo":"Franja específica","desde":"12:00","hasta":"08:00"}}""", false)]
        [InlineData("""{"Sabado":{"noDisponible":true}}""", true)]
        [InlineData("\"hola\"", false)]
        [InlineData("", true)]
        public void DisponibilidadJsonEsValido(string json, bool esperado) =>
            Assert.Equal(esperado, DisponibilidadSemanal.JsonEsValido(json));

        [Fact]
        public void DisponibilidadExigirValido_DesdeNoAnteriorAHasta_RechazaNombrandoAQuien()
        {
            var ex = Assert.Throws<ArgumentException>(() => DisponibilidadSemanal.ExigirValido(
                """{"lunes":{"noDisponible":false,"tipo":"Franja específica","desde":"10:00","hasta":"10:00"}}""", "del grupo"));
            Assert.Contains("del grupo", ex.Message);
            DisponibilidadSemanal.ExigirValido(null, "del grupo");   // ausente = sin restricción, válido
        }

        [Fact]
        public void GrupoValidarDatos_RequisitoConSesionesNegativas_Rechaza() =>
            Assert.Throws<ArgumentException>(() => SOEA.Application.Features.Grupos.GrupoService.ValidarDatos(null,
                new[] { new RequisitoEspacio(TipoSesion.Laboratorio, null, null, -1) }));

        [Theory]
        [InlineData("PEREZ, JUAN")]
        [InlineData("DR. LOPEZ")]
        [InlineData("GOMEZ (VISITANTE)")]
        [InlineData("María José Núñez")]
        [InlineData("(,)")]
        public void CorreoSintetico_SiempreEsUnCorreoValido(string nombre)
        {
            var correo = NormalizadorTexto.CorreoSintetico(nombre, Guid.NewGuid());
            Assert.Equal(correo, new MailAddress(correo).Address);
        }
    }
}
