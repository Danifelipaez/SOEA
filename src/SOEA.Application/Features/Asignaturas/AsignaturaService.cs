using SOEA.Application.Features.Grupos;
using SOEA.Domain.Entities;
using SOEA.Domain.Interfaces;
using SOEA.Application.Features.Asignaturas.Requests;
using SOEA.Application.Features.Asignaturas.Responses;
using SOEA.Application.Features.Horario;
using SOEA.Application.Features.Sesiones;

namespace SOEA.Application.Features.Asignaturas;

public class AsignaturaService
{
    private readonly IAsignaturaRepositorio _repository;
    private readonly IGrupoRepositorio _grupoRepository;
    private readonly SesionCascadeService _sesionCascade;
    private readonly IUnitOfWork _uow;
    private readonly IProgramaRepositorio _programaRepository;

    public AsignaturaService(
        IAsignaturaRepositorio repository,
        IGrupoRepositorio grupoRepository,
        SesionCascadeService sesionCascade,
        IUnitOfWork uow,
        IProgramaRepositorio programaRepository)
    {
        _repository = repository;
        _grupoRepository = grupoRepository;
        _sesionCascade = sesionCascade;
        _uow = uow;
        _programaRepository = programaRepository;
    }

    /// <summary>
    /// DB-6 auditoría 2026-09-28: un programa inexistente es un problema del REQUEST (400 con mensaje
    /// claro), no un 409 genérico de la FK Asignaturas.programa_id ni, como antes de la FK, un 201.
    /// </summary>
    private async Task ExigirProgramaExisteAsync(Guid programaId)
    {
        if (await _programaRepository.GetByIdAsync(programaId) is null)
            throw new ArgumentException("El programa indicado no existe. Elija un programa del catálogo.");
    }

    public async Task<AsignaturaResponse> CreateAsync(CreateAsignaturaRequest request)
    {
        await ExigirProgramaExisteAsync(request.ProgramaId);
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
        await ExigirProgramaExisteAsync(request.ProgramaId);
        int sesionesMismoTipoAntes = asignatura.SesionesMismoTipoSemana;

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

        // HC-SEP: subir las sesiones del mismo tipo puede dejar a grupos ya creados con disponibilidad
        // insuficiente. Solo se revisa si el número sube: una edición cualquiera (p. ej. el nombre) no
        // debe bloquearse por datos que ya estaban así.
        if (asignatura.SesionesMismoTipoSemana > sesionesMismoTipoAntes)
        {
            var errores = (await _grupoRepository.GetByAsignaturaIdAsync(id))
                .Select(g => GrupoService.ErrorDiasSeparados(g.DisponibilidadUiJson, asignatura.SesionesMismoTipoSemana, g.Nombre))
                .OfType<string>()
                .ToList();
            if (errores.Count > 0)
                throw new ArgumentException(
                    $"No se puede subir a {asignatura.SesionesMismoTipoSemana} sesiones del mismo tipo por semana: " +
                    string.Join(" ", errores.Take(3)) + (errores.Count > 3 ? $" (y {errores.Count - 3} grupo(s) más)" : "") +
                    " Amplíe primero la disponibilidad de esos grupos.");
        }

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
            // Sesion.AsignaturaId/GrupoId son FK Restrict — sin purgar primero las sesiones
            // generadas, el borrado de abajo falla con un 409 genérico. Son datos regenerables
            // de una corrida, no catálogo: catálogo nunca debe bloquearse por ellas.
            foreach (var grupo in gruposAsociados)
            {
                await _sesionCascade.EliminarPorGrupoAsync(grupo.Id);
                await _grupoRepository.DeleteAsync(grupo.Id);
            }

            await _sesionCascade.EliminarPorAsignaturaAsync(id);
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
