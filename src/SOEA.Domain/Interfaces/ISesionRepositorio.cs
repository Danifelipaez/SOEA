using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SOEA.Domain.Entities;

namespace SOEA.Domain.Interfaces
{
    public interface ISesionRepositorio : IRepositorio<Sesion>
    {
        Task AddRangeAsync(IEnumerable<Sesion> sesiones);
        Task<bool> ExisteAsync(Guid asignaturaId, Guid docenteId, Guid bloqueTiempoId);
    }
}
