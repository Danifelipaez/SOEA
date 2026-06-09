using System;
using System.Threading.Tasks;
using SOEA.Domain.Entities;

namespace SOEA.Domain.Interfaces
{
    public interface IProgramaRepositorio : IRepositorio<Programa>
    {
        Task<Programa?> GetByNombreYFacultadAsync(string nombre, Guid facultadId);
    }
}
