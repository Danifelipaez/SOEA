using Microsoft.AspNetCore.Mvc;
using SOEA.Application.Features.Asignaturas;
using SOEA.Application.Features.Asignaturas.Requests;
using SOEA.Application.Features.Asignaturas.Responses;
using SOEA.Domain.Enums;
using SOEA.Domain.Interfaces;

namespace SOEA.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AsignaturasController : ControllerBase
    {
        private readonly CreateAsignaturaService _createService;
        private readonly GetAsignaturaByIdService _getByIdService;
        private readonly GetAsignaturasService _getAllService;
        private readonly DeleteAsignaturaService _deleteService;
        private readonly UpdateAlternanciaService _updateAlternanciaService;
        private readonly UpdateAsignaturaService _updateService;

        public AsignaturasController(
            CreateAsignaturaService createService,
            GetAsignaturaByIdService getByIdService,
            GetAsignaturasService getAllService,
            DeleteAsignaturaService deleteService,
            UpdateAlternanciaService updateAlternanciaService,
            UpdateAsignaturaService updateService)
        {
            _createService = createService;
            _getByIdService = getByIdService;
            _getAllService = getAllService;
            _deleteService = deleteService;
            _updateAlternanciaService = updateAlternanciaService;
            _updateService = updateService;
        }

        [HttpPost]
        public async Task<ActionResult<AsignaturaResponse>> CreateAsignatura(
            [FromBody] CreateAsignaturaRequest request)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            try
            {
                var response = await _createService.ExecuteAsync(request);
                return CreatedAtAction(nameof(GetAsignatura), new { id = response.Id }, response);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<AsignaturaResponse>> GetAsignatura(Guid id)
        {
            try
            {
                var response = await _getByIdService.ExecuteAsync(id);
                return Ok(response);
            }
            catch (InvalidOperationException ex)
            {
                return NotFound(ex.Message);
            }
        }

        [HttpGet]
        public async Task<ActionResult<List<AsignaturaResponse>>> GetAllAsignaturas()
        {
            try
            {
                var responses = await _getAllService.ExecuteAsync();
                return Ok(responses);
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }

        /// <summary>
        /// Actualiza los datos editables de una asignatura (nombre, código, duración,
        /// programa, docente y espacio fijo) desde la UI de Ingesta.
        /// </summary>
        [HttpPut("{id}")]
        public async Task<ActionResult<AsignaturaResponse>> UpdateAsignatura(
            Guid id, [FromBody] UpdateAsignaturaRequest request)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            try
            {
                var response = await _updateService.ExecuteAsync(id, request);
                return Ok(response);
            }
            catch (InvalidOperationException ex)
            {
                return NotFound(ex.Message);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteAsignatura(Guid id)
        {
            try
            {
                await _deleteService.ExecuteAsync(id);
                return NoContent();
            }
            catch (InvalidOperationException ex)
            {
                return NotFound(ex.Message);
            }
        }

        /// <summary>
        /// Actualiza el tipo de alternancia de una asignatura.
        /// Body: { "alternancia": "TipoA" | "TipoB" | "SinAlternancia" }
        /// </summary>
        [HttpPatch("{id}/alternancia")]
        public async Task<IActionResult> UpdateAlternancia(Guid id, [FromBody] UpdateAlternanciaDto dto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            try
            {
                await _updateAlternanciaService.ExecuteAsync(id, dto.Alternancia);
                return NoContent();
            }
            catch (InvalidOperationException ex)
            {
                return NotFound(ex.Message);
            }
        }
    }

    public class UpdateAlternanciaDto
    {
        public TipoAlternancia Alternancia { get; set; }
    }
}
