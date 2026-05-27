using SOEA.Domain.Entities;

namespace SOEA.Domain.Interfaces
{
    public interface IDocenteRepositorio : IRepositorio<Docente>
    {
        Task<Docente?> GetByCedulaAsync(string cedula);
    }
}
