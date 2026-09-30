using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using SOEA.API;
using Xunit;

namespace SOEA.Tests.Api
{
    /// <summary>
    /// L-11 (auditoría 2026-09-28): los errores salían en formatos distintos (texto plano, JSON sin <c>detail</c>,
    /// <c>problem+json</c>) y la validación de modelo en inglés ("The semestre field is required"; una subida de
    /// 35 MB con "Request body too large… 30000000 bytes").
    /// </summary>
    public class RespuestasDeErrorTests
    {
        private static ObjectResult Validar(ModelStateDictionary modelo) =>
            Assert.IsType<ObjectResult>(ValidacionModelo.RespuestaEnEspanol(
                new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor(), modelo)));

        [Fact]
        public void ValidacionDeModelo_DevuelveProblemDetailsEnEspanol_ConLosCamposAfectados()
        {
            var modelo = new ModelStateDictionary();
            modelo.AddModelError("semestre", "The semestre field is required.");
            modelo.AddModelError("$.configuracion.maxGeneraciones", "The JSON value could not be converted to System.Int32.");

            var r = Validar(modelo);

            Assert.Equal(400, r.StatusCode);
            Assert.Contains("application/problem+json", r.ContentTypes);
            var pd = Assert.IsType<ValidationProblemDetails>(r.Value);
            Assert.Contains("semestre", pd.Detail);
            Assert.Contains("configuracion.maxGeneraciones", pd.Detail);
            Assert.All(pd.Errors.Values, msgs => Assert.DoesNotContain("field is required", string.Join(' ', msgs)));
            Assert.Contains("obligatorio", pd.Errors["semestre"][0]);
        }

        [Fact]
        public void CuerpoDemasiadoGrande_Responde413ConMensajeClaro_SinBytesNiIngles()
        {
            var modelo = new ModelStateDictionary();
            modelo.AddModelError("", "Failed to read the request form. Request body too large. The max request body size is 30000000 bytes.");

            var r = Validar(modelo);

            Assert.Equal(413, r.StatusCode);
            var pd = Assert.IsType<ProblemDetails>(r.Value);
            Assert.Contains("30 MB", pd.Detail);
            Assert.DoesNotContain("bytes", pd.Detail);
        }

        private static async Task<(int status, string tipo, JsonElement cuerpo)> Traducir(Exception ex)
        {
            var contexto = new DefaultHttpContext();
            contexto.Response.Body = new MemoryStream();
            var manejado = await new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance)
                .TryHandleAsync(contexto, ex, default);
            Assert.True(manejado);
            contexto.Response.Body.Position = 0;
            var cuerpo = (await JsonDocument.ParseAsync(contexto.Response.Body)).RootElement.Clone();
            return (contexto.Response.StatusCode, contexto.Response.ContentType!, cuerpo);
        }

        [Fact]
        public async Task ManejadorGlobal_NoEncontrado_Responde404ProblemJson_ConDetalle()
        {
            var (status, tipo, cuerpo) = await Traducir(new KeyNotFoundException("Grupo con ID x no encontrado."));

            Assert.Equal(404, status);
            Assert.StartsWith("application/problem+json", tipo);
            Assert.Equal("Grupo con ID x no encontrado.", cuerpo.GetProperty("detail").GetString());
        }

        [Fact]
        public async Task ManejadorGlobal_CuerpoDemasiadoGrande_Responde413ConMensajeEnEspanol()
        {
            var (status, _, cuerpo) = await Traducir(new BadHttpRequestException(
                "Request body too large. The max request body size is 30000000 bytes.", StatusCodes.Status413PayloadTooLarge));

            Assert.Equal(413, status);
            Assert.Contains("30 MB", cuerpo.GetProperty("detail").GetString());
        }
    }
}
