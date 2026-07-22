using SOEA.Domain.Entities;
using SOEA.Domain.Interfaces;
using SOEA.Infrastructure.Data.Context;

namespace SOEA.Infrastructure.Data.Repositories
{
    public class CriterioCesionAlternanciaRepositorio
        : BaseRepository<CriterioCesionAlternancia>, ICriterioCesionAlternanciaRepositorio
    {
        public CriterioCesionAlternanciaRepositorio(SOEABdContext context) : base(context) { }
    }
}
