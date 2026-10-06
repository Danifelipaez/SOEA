using System;
using System.Linq;
using System.Threading.Tasks;
using SOEA.Domain.Entities;

namespace SOEA.Domain.Interfaces
{
    public interface IProgramaRepositorio : IRepositorio<Programa>
    {
        Task<Programa?> GetByNombreYFacultadAsync(string nombre, Guid facultadId);

        /// <summary>Programas de una facultad (guard de borrado). Por defecto filtra en memoria; el
        /// repositorio EF lo resuelve con un COUNT en la BD.</summary>
        async Task<int> ContarPorFacultadAsync(Guid facultadId)
            => (await GetAllAsync()).Count(p => p.FacultadId == facultadId);
    }
}
