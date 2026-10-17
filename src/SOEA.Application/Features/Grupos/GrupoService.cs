using SOEA.Domain.Entities;
using SOEA.Domain.Interfaces;
using SOEA.Domain.ValueObjects;
using SOEA.Application.Features.Sesiones;

namespace SOEA.Application.Features.Grupos;

/// <summary>Datos editables de un grupo al crearlo o editarlo desde el catálogo.</summary>
public record GrupoDatos(
    Guid? AsignaturaId, string Nombre, string? Codigo, int EstudiantesInscritos,
    Guid? DocenteId, string? DisponibilidadUiJson, List<RequisitoEspacio> RequisitosEspacio);

/// <summary>Grupo con su programa y facultad, derivados de la asignatura (Grupo → Asignatura → Programa → Facultad).
/// ProgramaId = Guid.Empty si la asignatura no existe.</summary>
public record GrupoConJerarquia(Grupo Grupo, Guid ProgramaId, Guid? FacultadId);

/// <summary>
/// Borrado de Grupo con purga en cascada de sus Sesiones generadas (y AsignacionSemanal) — antes
/// GruposController bloqueaba el borrado con 409 si había sesiones generadas para el grupo
/// (Sesion.GrupoId es FK Restrict desde M14 auditoría). Mismo principio que
/// EspacioService/AsignaturaService.DeleteAsync: catálogo no se bloquea por datos regenerables.
/// </summary>
public class GrupoService
{
    private readonly IGrupoRepositorio _repository;
    private readonly SesionCascadeService _sesionCascade;
    private readonly IUnitOfWork _uow;
    private readonly IAsignaturaRepositorio _asignaturas;
    private readonly IProgramaRepositorio _programas;

    public GrupoService(IGrupoRepositorio repository, SesionCascadeService sesionCascade, IUnitOfWork uow,
        IAsignaturaRepositorio asignaturas, IProgramaRepositorio programas)
    {
        _repository = repository;
        _sesionCascade = sesionCascade;
        _uow = uow;
        _asignaturas = asignaturas;
        _programas = programas;
    }

    public async Task<List<GrupoConJerarquia>> ListarAsync()
    {
        var facultadPorPrograma = (await _programas.GetAllAsync()).ToDictionary(p => p.Id, p => p.FacultadId);
        var programaPorAsignatura = (await _asignaturas.GetAllAsync()).ToDictionary(a => a.Id, a => a.ProgramaId);
        return (await _repository.GetAllAsync()).Select(g =>
            programaPorAsignatura.TryGetValue(g.AsignaturaId, out var programaId)
                ? new GrupoConJerarquia(g, programaId, facultadPorPrograma.TryGetValue(programaId, out var f) ? f : null)
                : new GrupoConJerarquia(g, Guid.Empty, null)).ToList();
    }

    public async Task<GrupoConJerarquia> ObtenerAsync(Guid id)
    {
        var grupo = await _repository.GetByIdAsync(id)
            ?? throw new KeyNotFoundException($"Grupo con ID {id} no encontrado.");
        return await ConJerarquiaAsync(grupo, await _asignaturas.GetByIdAsync(grupo.AsignaturaId));
    }

    public async Task<List<GrupoConJerarquia>> ListarPorAsignaturaAsync(Guid asignaturaId)
    {
        var grupos = await _repository.GetByAsignaturaIdAsync(asignaturaId);
        var asignatura = await _asignaturas.GetByIdAsync(asignaturaId);
        Guid? facultadId = asignatura is null ? null : (await _programas.GetByIdAsync(asignatura.ProgramaId))?.FacultadId;
        return grupos.Select(g => new GrupoConJerarquia(g, asignatura?.ProgramaId ?? Guid.Empty, facultadId)).ToList();
    }

    public async Task<GrupoConJerarquia> CrearAsync(Guid id, GrupoDatos datos)
    {
        var asignatura = await AsignaturaValidaAsync(datos);
        // ERR2 auditoría: sin catch — GlobalExceptionHandler traduce ArgumentException a 400 y
        // DbUpdateException (p. ej. código de grupo repetido, ix_grupo_codigo) a 409.
        var grupo = new Grupo(
            id == Guid.Empty ? Guid.NewGuid() : id,
            datos.Nombre,
            asignatura.Id,
            datos.EstudiantesInscritos,
            docenteId: datos.DocenteId,
            codigo: datos.Codigo);
        grupo.ActualizarDisponibilidadUi(datos.DisponibilidadUiJson);
        grupo.ActualizarRequisitosEspacio(datos.RequisitosEspacio);

        await _repository.AddAsync(grupo);
        return await ConJerarquiaAsync(grupo, asignatura);
    }

    public async Task<GrupoConJerarquia> ActualizarAsync(Guid id, GrupoDatos datos)
    {
        var grupo = await _repository.GetByIdAsync(id)
            ?? throw new KeyNotFoundException($"Grupo con ID {id} no encontrado.");
        var asignatura = await AsignaturaValidaAsync(datos);

        grupo.ActualizarNombre(datos.Nombre);
        grupo.ActualizarCodigo(datos.Codigo);
        grupo.ActualizarEstudiantes(datos.EstudiantesInscritos);
        // Si cambia la asignatura, la FK compuesta de Sesiones (ON UPDATE CASCADE) mueve con él
        // las sesiones del grupo: nunca quedan de una asignatura distinta a la de su grupo.
        grupo.ActualizarAsignatura(asignatura.Id);
        grupo.AsignarDocente(datos.DocenteId);
        grupo.ActualizarDisponibilidadUi(datos.DisponibilidadUiJson);
        grupo.ActualizarRequisitosEspacio(datos.RequisitosEspacio);

        await _repository.UpdateAsync(grupo);
        return await ConJerarquiaAsync(grupo, asignatura);
    }

    /// <summary>Todo grupo debe estar atado a una asignatura existente, y sus datos deben ser válidos para ella.</summary>
    private async Task<Asignatura> AsignaturaValidaAsync(GrupoDatos datos)
    {
        if (datos.AsignaturaId is not { } asignaturaId || asignaturaId == Guid.Empty)
            throw new ArgumentException("AsignaturaId es obligatorio.");
        var asignatura = await _asignaturas.GetByIdAsync(asignaturaId)
            ?? throw new ArgumentException($"No existe la asignatura con Id '{asignaturaId}'.");

        ValidarDatos(datos.DisponibilidadUiJson, datos.RequisitosEspacio);
        if (ErrorDiasSeparados(datos.DisponibilidadUiJson, asignatura.SesionesMismoTipoSemana, datos.Nombre) is { } errorDias)
            throw new ArgumentException(errorDias);
        return asignatura;
    }

    private async Task<GrupoConJerarquia> ConJerarquiaAsync(Grupo grupo, Asignatura? asignatura) =>
        asignatura is null
            ? new GrupoConJerarquia(grupo, Guid.Empty, null)
            : new GrupoConJerarquia(grupo, asignatura.ProgramaId, (await _programas.GetByIdAsync(asignatura.ProgramaId))?.FacultadId);

    /// <summary>
    /// Reglas de los datos editables de un grupo al guardarlo desde el catálogo. Una disponibilidad ilegible
    /// (o con una franja de horas imposible) se guardaba y al generar el grupo quedaba en silencio sin
    /// restricción. No va en el setter de la entidad: la generación tolera una disponibilidad inválida con un aviso.
    /// </summary>
    public static void ValidarDatos(string? disponibilidadUiJson, IEnumerable<RequisitoEspacio> requisitos)
    {
        DisponibilidadSemanal.ExigirValido(disponibilidadUiJson, "del grupo");
        if (requisitos.Any(r => r.Sesiones < 0))
            throw new ArgumentException("Las sesiones de un requisito de espacio no pueden ser negativas.");
    }

    /// <summary>
    /// HC-SEP en la captura: con n ≥ 2 sesiones del mismo tipo por semana, el grupo debe estar disponible
    /// en n días con al menos uno libre entre ellos. Antes esto solo aparecía al generar, como un
    /// "grupo en conflicto" sin explicación. Null = cumple. Espejo de errorSeparacionDias (frontend).
    /// </summary>
    public static string? ErrorDiasSeparados(string? disponibilidadUiJson, int sesionesMismoTipo, string nombreGrupo)
    {
        if (sesionesMismoTipo < 2) return null;
        if (sesionesMismoTipo > 3)
            return $"Con {sesionesMismoTipo} sesiones del mismo tipo por semana no hay forma de dejar un día libre entre " +
                   "todas (caben como máximo 3: lunes, miércoles y viernes). Reduzca las sesiones por semana de la asignatura.";
        if (DisponibilidadSemanal.DesdeJson(disponibilidadUiJson, diaSinEntradaDisponible: false).DiasSeparadosDisponibles() >= sesionesMismoTipo) return null;
        return $"El grupo '{nombreGrupo}' tiene {sesionesMismoTipo} sesiones del mismo tipo por semana, que deben ir en días " +
               "distintos con al menos un día libre entre ellas, y su disponibilidad no lo permite. Marque al menos " +
               $"{sesionesMismoTipo} días disponibles separados (por ejemplo {(sesionesMismoTipo == 2 ? "lunes y miércoles" : "lunes, miércoles y viernes")}).";
    }

    public async Task DeleteAsync(Guid id)
    {
        if (await _repository.GetByIdAsync(id) is null)
            throw new KeyNotFoundException($"Grupo con ID {id} no encontrado.");

        await _uow.BeginTransactionAsync();
        try
        {
            await _sesionCascade.EliminarPorGrupoAsync(id);
            await _repository.DeleteAsync(id);
            await _uow.CommitAsync();
        }
        catch
        {
            await _uow.RollbackAsync();
            throw;
        }
    }
}
