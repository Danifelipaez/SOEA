using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SOEA.Domain.Entities;

namespace SOEA.Domain.Interfaces
{
    public interface IAsignacionSemanalRepositorio : IRepositorio<AsignacionSemanal>
    {
        Task AddRangeAsync(IEnumerable<AsignacionSemanal> asignaciones);
        Task<List<AsignacionSemanal>> GetBySesionIdsAsync(IEnumerable<Guid> sesionIds);
        /// <summary>PERF3 auditoría: borra todas las asignaciones de un lote de sesiones en una
        /// sola operación (EF Core ExecuteDeleteAsync), en vez de un DeleteAsync por fila.</summary>
        Task DeleteBySesionIdsAsync(IEnumerable<Guid> sesionIds);
    }
}
