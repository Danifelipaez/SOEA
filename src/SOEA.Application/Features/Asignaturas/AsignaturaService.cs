using SOEA.Domain.Entities;
using SOEA.Domain.Interfaces;
using SOEA.Application.Features.Asignaturas.Requests;
using SOEA.Application.Features.Asignaturas.Responses;
using SOEA.Application.Features.Horario;

namespace SOEA.Application.Features.Asignaturas;

public class AsignaturaService
{
    private readonly IAsignaturaRepositorio _repository;
    private readonly IGrupoRepositorio _grupoRepository;
    private readonly IUnitOfWork _uow;

    public AsignaturaService(IAsignaturaRepositorio repository, IGrupoRepositorio grupoRepository, IUnitOfWork uow)
    {
        _repository = repository;
        _grupoRepository = grupoRepository;
        _uow = uow;
    }

    public async Task<AsignaturaResponse> CreateAsync(CreateAsignaturaRequest request)
    {
        var asignatura = new Asignatura(
            request.Id == Guid.Empty ? Guid.NewGuid() : request.Id,
            request.Nombre,
            request.Codigo,
            sesionesTeoriaPresencialSemana: request.SesionesTeoriaPresencialSemana,
            horasTeoriaPresencial: request.HorasTeoriaPresencial,
            sesionesTeoriaVirtualSemana: request.SesionesTeoriaVirtualSemana,
            horasTeoriaVirtual: request.HorasTeoriaVirtual,
            sesionesLaboratorioSemana: request.SesionesLaboratorioSemana,
            horasLaboratorio: request.HorasLaboratorio,
            sesionesLaboratorioSemestre: request.SesionesLaboratorioSemestre,
            programaId: request.ProgramaId,
            categoria: request.Categoria ?? Domain.Enums.CategoriaAsignatura.Obligatoria,
            horaInicioMin: GenerarHorarioService.ParseHora(request.HoraInicioMin),
            horaFinMax: GenerarHorarioService.ParseHora(request.HoraFinMax));

        if (request.Alternancia.HasValue)
            asignatura.EstablecerAlternancia(request.Alternancia.Value);

        await _repository.AddAsync(asignatura);
        return AsignaturaResponse.FromEntity(asignatura);
    }

    public async Task<AsignaturaResponse> GetByIdAsync(Guid id)
    {
        var asignatura = await _repository.GetByIdAsync(id)
            ?? throw new KeyNotFoundException($"Asignatura con ID {id} no encontrada.");
        return AsignaturaResponse.FromEntity(asignatura);
    }

    public async Task<List<AsignaturaResponse>> GetAllAsync()
    {
        var asignaturas = await _repository.GetAllAsync();
        return asignaturas.Select(AsignaturaResponse.FromEntity).ToList();
    }

    public async Task<AsignaturaResponse> UpdateAsync(Guid id, UpdateAsignaturaRequest request)
    {
        var asignatura = await _repository.GetByIdAsync(id)
            ?? throw new KeyNotFoundException($"Asignatura con ID {id} no encontrada.");

        asignatura.ActualizarDatos(
            request.Nombre,
            request.Codigo,
            sesionesTeoriaPresencialSemana: request.SesionesTeoriaPresencialSemana,
            horasTeoriaPresencial: request.HorasTeoriaPresencial,
            sesionesTeoriaVirtualSemana: request.SesionesTeoriaVirtualSemana,
            horasTeoriaVirtual: request.HorasTeoriaVirtual,
            sesionesLaboratorioSemana: request.SesionesLaboratorioSemana,
            horasLaboratorio: request.HorasLaboratorio,
            sesionesLaboratorioSemestre: request.SesionesLaboratorioSemestre,
            programaId: request.ProgramaId,
            alternanciaExplicita: request.Alternancia,
            categoria: request.Categoria,
            horaInicioMin: GenerarHorarioService.ParseHora(request.HoraInicioMin),
            horaFinMax: GenerarHorarioService.ParseHora(request.HoraFinMax));

        await _repository.UpdateAsync(asignatura);
        return AsignaturaResponse.FromEntity(asignatura);
    }

    public async Task DeleteAsync(Guid id)
    {
        if (await _repository.GetByIdAsync(id) is null)
            throw new KeyNotFoundException($"Asignatura con ID {id} no encontrada.");

        var gruposAsociados = (await _grupoRepository.GetByAsignaturaIdAsync(id)).ToList();

        // H5 auditoría: antes cada DeleteAsync confirmaba por su cuenta (SaveChanges propio) — si
        // el borrado de la Asignatura fallaba después de borrar sus Grupos, esos Grupos quedaban
        // borrados sin ninguna forma de recuperarlos. Una sola transacción para las dos escrituras.
        await _uow.BeginTransactionAsync();
        try
        {
            foreach (var grupo in gruposAsociados)
                await _grupoRepository.DeleteAsync(grupo.Id);

            await _repository.DeleteAsync(id);
            await _uow.CommitAsync();
        }
        catch
        {
            await _uow.RollbackAsync();
            throw;
        }
    }

    /// <summary>
    /// Marca (o desmarca) la asignatura como candidata a ceder a alternancia si el algoritmo
    /// agota el espacio físico disponible (cesión por saturación de espacio).
    /// </summary>
    public async Task UpdateElegibilidadAlternanciaAsync(Guid id, bool elegible)
    {
        var asignatura = await _repository.GetByIdAsync(id)
            ?? throw new KeyNotFoundException($"Asignatura con ID {id} no encontrada.");
        asignatura.EstablecerElegibilidadAlternancia(elegible);
        await _repository.UpdateAsync(asignatura);
    }
}
