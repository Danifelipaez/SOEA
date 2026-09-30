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
        /// Importa el Excel de horario/currículum y persiste toda la jerarquía en una transacción.
        /// </summary>
        [HttpPost("excel")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> ImportarExcel(IFormFile archivo)
        {
            if (archivo == null || archivo.Length == 0)
                throw new ArgumentException("No se recibió ningún archivo.");

            if (!archivo.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) &&
                !archivo.FileName.EndsWith(".xls",  StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("El archivo debe ser .xlsx o .xls.");

            var bloquesCatalogo = await _bloques.GetAllAsync();
            var catalogoLookup = bloquesCatalogo
                .GroupBy(b => (b.Dia, b.HoraInicio))
                .ToDictionary(g => g.Key, g => g.First())
                as IReadOnlyDictionary<(DiaDeSemana, TimeOnly), BloqueTiempo>;

            CurriculumExcelResult resultado;
            try
            {
                using var stream = archivo.OpenReadStream();
                resultado = await _lectorExcel.LeerCurriculumAsync(stream, catalogoLookup);
            }
            catch (ArchivoImportacionInvalidoException ex)
            {
                // El mensaje ya es del usuario (fila + dato): se le devuelve tal cual, no "archivo dañado".
                throw new ArgumentException(ex.Message);
            }
            catch (Exception ex)
            {
                // M5 auditoría: antes se le atribuía CUALQUIER falla (incluidos bugs internos del
                // lector) al archivo del usuario, y se devolvía ex.Message crudo en el body. Se
                // registra el detalle real en el log del servidor y se responde un mensaje genérico.
                _logger.LogWarning(ex, "No se pudo leer el archivo de importación.");
                throw new ArgumentException("No se pudo leer el archivo. Verifica que sea un Excel (.xlsx/.xls) válido y no esté dañado.");
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
    }
}
