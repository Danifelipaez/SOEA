using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using SOEA.Application.Features.Import;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;
using SOEA.Domain.Exceptions;
using SOEA.Domain.Interfaces;
using SOEA.Domain.Services;
using SOEA.Domain.ValueObjects;

namespace SOEA.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ImportController : ControllerBase
    {
        private readonly ImportarCurriculumService _importService;
        private readonly ILectorExcel _lectorExcel;
        private readonly IBloqueTiempoRepositorio _bloques;
        private readonly ILogger<ImportController> _logger;

        public ImportController(
            ImportarCurriculumService importService,
            ILectorExcel lectorExcel,
            IBloqueTiempoRepositorio bloques,
            ILogger<ImportController> logger)
        {
            _importService = importService;
            _lectorExcel   = lectorExcel;
            _bloques       = bloques;
            _logger        = logger;
        }

        /// <summary>
        /// Paso 1 del import: lee el Excel y devuelve sus filas con las incoherencias encontradas.
        /// No guarda nada — el operador corrige o borra filas y luego llama a POST import/filas.
        /// </summary>
        [HttpPost("excel/revisar")]
        [Consumes("multipart/form-data")]
        public async Task<ActionResult<RevisionImportDto>> RevisarExcel(IFormFile archivo)
        {
            if (archivo == null || archivo.Length == 0)
                throw new ArgumentException("No se recibió ningún archivo.");

            if (!archivo.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) &&
                !archivo.FileName.EndsWith(".xls",  StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("El archivo debe ser .xlsx o .xls.");

            HojaCurriculum hoja;
            try
            {
                using var stream = archivo.OpenReadStream();
                hoja = await _lectorExcel.LeerFilasAsync(stream);
            }
            catch (Exception ex)
            {
                // M5 auditoría: no se atribuye cualquier falla (incluidos bugs del lector) al archivo
                // del usuario ni se devuelve ex.Message crudo: detalle al log, mensaje genérico al cliente.
                _logger.LogWarning(ex, "No se pudo leer el archivo de importación.");
                throw new ArgumentException("No se pudo leer el archivo. Verifica que sea un Excel (.xlsx/.xls) válido y no esté dañado.");
            }

            return Ok(Revisar(hoja.Filas, hoja.Avisos));
        }

        /// <summary>Vuelve a revisar las filas tras editarlas/borrarlas en el popup. No guarda nada.</summary>
        [HttpPost("filas/revisar")]
        public ActionResult<RevisionImportDto> RevisarFilas([FromBody] List<FilaCurriculum> filas)
            => Ok(Revisar(filas, Array.Empty<string>()));

        /// <summary>
        /// Paso 2: importa las filas revisadas y persiste toda la jerarquía en una transacción.
        /// Si queda algún error sin resolver responde 422 con la revisión, sin guardar nada.
        /// </summary>
        [HttpPost("filas")]
        public async Task<IActionResult> ImportarFilas([FromBody] List<FilaCurriculum> filas)
        {
            var revision = Revisar(filas, Array.Empty<string>());
            if (revision.Incoherencias.Any(i => i.EsError))
                return UnprocessableEntity(revision);

            var bloquesCatalogo = await _bloques.GetAllAsync();
            var catalogoLookup = bloquesCatalogo
                .GroupBy(b => (b.Dia, b.HoraInicio))
                .ToDictionary(g => g.Key, g => g.First())
                as IReadOnlyDictionary<(DiaDeSemana, TimeOnly), BloqueTiempo>;

            CurriculumExcelResult resultado;
            try
            {
                resultado = _lectorExcel.ConstruirCurriculum(filas, catalogoLookup);
            }
            catch (ArchivoImportacionInvalidoException ex)
            {
                // El mensaje ya es del usuario (fila + dato).
                throw new ArgumentException(ex.Message);
            }

            // B3 auditoría: sin catch-all aquí — GlobalExceptionHandler (A1) cubre lo demás sin
            // exponer ex.Message crudo.
            var stats = await _importService.EjecutarAsync(resultado);

            return Ok(new ImportExcelStatsDto
            {
                FacultadesCreadas       = stats.FacultadesCreadas,
                ProgramasCreados        = stats.ProgramasCreados,
                DocentesCreados         = stats.DocentesCreados,
                DocentesActualizados    = stats.DocentesActualizados,
                EspaciosCreados         = stats.EspaciosCreados,
                EspaciosActualizados    = stats.EspaciosActualizados,
                AsignaturasCreadas      = stats.AsignaturasCreadas,
                AsignaturasActualizadas = stats.AsignaturasActualizadas,
                GruposCreados           = stats.GruposCreados,
                GruposSinDocente   = stats.GruposSinDocente,
                Advertencias            = stats.Advertencias
            });
        }

        private RevisionImportDto Revisar(IReadOnlyList<FilaCurriculum> filas, IReadOnlyList<string> avisos)
        {
            if (filas.Count == 0 && avisos.Count == 0)
                throw new ArgumentException("No hay filas para importar.");
            return new RevisionImportDto
            {
                Filas          = filas.ToList(),
                Incoherencias  = _lectorExcel.ValidarFilas(filas).ToList(),
                Avisos         = avisos.ToList(),
            };
        }
    }
}
