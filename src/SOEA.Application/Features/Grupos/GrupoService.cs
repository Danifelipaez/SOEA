using SOEA.Domain.Interfaces;
using SOEA.Domain.ValueObjects;
using SOEA.Application.Features.Sesiones;

namespace SOEA.Application.Features.Grupos;

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

    public GrupoService(IGrupoRepositorio repository, SesionCascadeService sesionCascade, IUnitOfWork uow)
    {
        _repository = repository;
        _sesionCascade = sesionCascade;
        _uow = uow;
    }

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
