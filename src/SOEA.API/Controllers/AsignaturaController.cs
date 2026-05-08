using Microsoft.AspNetCore.Mvc;
using SOEA.Application.Features.Asignaturas;
using SOEA.Application.Interfaces;

namespace SOEA.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    //newtestline
    public class AsignaturasController : ControllerBase
    {
        private readonly CreateAsignaturaService _createService;
        private readonly IAsignaturaRepository _repository;

        public AsignaturasController(CreateAsignaturaService createService, IAsignaturaRepository repository)
        {
            _createService = createService;
            _repository = repository;
        }

        [HttpPost]
        public async Task<ActionResult<AsignaturaResponse>> CreateAsignatura(
            [FromBody] CreateAsignaturaRequest request)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var response = await _createService.ExecuteAsync(request);
            return CreatedAtAction(nameof(GetAsignatura), new { id = response.Id }, response);
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<AsignaturaResponse>> GetAsignatura(Guid id)
        {
            var asignatura = await _repository.GetByIdAsync(id);
            if (asignatura == null)
                return NotFound();

            return Ok(new AsignaturaResponse
            {
                Id = asignatura.Id,
                Nombre = asignatura.Nombre,
                Codigo = asignatura.Codigo,
                BloquesSemanales = asignatura.BloquesSemanales,
                RequiereLab = asignatura.RequiereLab,
                Alternancia = asignatura.Alternancia,
                ProgramaId = asignatura.ProgramaId
            });
        }

        [HttpGet]
        public async Task<ActionResult<List<AsignaturaResponse>>> GetAllAsignaturas()
        {
            var asignaturas = await _repository.GetAllAsync();
            return Ok(asignaturas.Select(a => new AsignaturaResponse
            {
                Id = a.Id,
                Nombre = a.Nombre,
                Codigo = a.Codigo,
                BloquesSemanales = a.BloquesSemanales,
                RequiereLab = a.RequiereLab,
                Alternancia = a.Alternancia,
                ProgramaId = a.ProgramaId
            }).ToList());
        }
    }
}