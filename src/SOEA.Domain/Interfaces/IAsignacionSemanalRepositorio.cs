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
    }
}
