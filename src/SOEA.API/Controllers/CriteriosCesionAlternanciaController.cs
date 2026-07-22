using Microsoft.AspNetCore.Mvc;
using SOEA.Application.Features.CriteriosCesionAlternancia;

namespace SOEA.API.Controllers
{
    /// <summary>
    /// Lista ordenada y activable de criterios de cesión a alternancia por saturación de espacio
    /// (fallback presencial-first). Catálogo fijo de 2 filas de sistema (Electiva / Elegible).
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class CriteriosCesionAlternanciaController : ControllerBase
    {
        private readonly CriterioCesionAlternanciaService _service;

        public CriteriosCesionAlternanciaController(CriterioCesionAlternanciaService service) => _service = service;

        [HttpGet]
        public async Task<ActionResult<List<CriterioCesionAlternanciaDto>>> GetAll()
            => Ok(await _service.ListarAsync());

        /// <summary>
        /// Actualiza orden y/o estado activo de un criterio. Body: { "orden": 1, "activo": true }
        /// (ambos opcionales). Si el nuevo orden ya lo ocupa otro criterio, se intercambian.
        /// </summary>
        [HttpPatch("{id}")]
        public async Task<ActionResult<List<CriterioCesionAlternanciaDto>>> Actualizar(
            Guid id, [FromBody] ActualizarCriterioCesionDto dto)
        {
            try
            {
                var lista = await _service.ActualizarAsync(id, dto.Orden, dto.Activo);
                return Ok(lista);
            }
            catch (InvalidOperationException ex)
            {
                return NotFound(ex.Message);
            }
        }
    }

    public class ActualizarCriterioCesionDto
    {
        public int? Orden { get; set; }
        public bool? Activo { get; set; }
    }
}
