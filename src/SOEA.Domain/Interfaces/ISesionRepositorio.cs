using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SOEA.Domain.Entities;

namespace SOEA.Domain.Interfaces
{
    public interface ISesionRepositorio : IRepositorio<Sesion>
    {
        Task AddRangeAsync(IEnumerable<Sesion> sesiones);
        Task<bool> ExisteAsync(Guid asignaturaId, Guid? docenteId, Guid bloqueTiempoId);
        /// <summary>
        /// PERF4 auditoría: trae un lote de sesiones por id en una sola consulta (WHERE Id IN …),
        /// en vez de un GetByIdAsync por id (N round trips) — mismo patrón que
        /// IAsignacionSemanalRepositorio.GetBySesionIdsAsync.
        /// </summary>
        Task<List<Sesion>> GetByIdsAsync(IEnumerable<Guid> ids);
        /// <summary>
        /// PERF3 auditoría: borra un lote de sesiones en una sola operación (EF Core
        /// ExecuteDeleteAsync — un DELETE, sin cargar ni trackear cada fila), en vez de
        /// llamar DeleteAsync una vez por id (un SELECT + un DELETE cada uno).
        /// </summary>
        Task DeleteRangeAsync(IEnumerable<Guid> ids);
    }
}
