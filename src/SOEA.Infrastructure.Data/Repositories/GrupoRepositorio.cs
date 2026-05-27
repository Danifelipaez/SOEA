using SOEA.Domain.Entities;
using SOEA.Domain.Interfaces;
using SOEA.Infrastructure.Data.Context;

namespace SOEA.Infrastructure.Data.Repositories
{
    public class GrupoRepositorio : BaseRepository<Grupo>, IGrupoRepositorio
    {
        public GrupoRepositorio(SOEABdContext context) : base(context) { }
    }
}
