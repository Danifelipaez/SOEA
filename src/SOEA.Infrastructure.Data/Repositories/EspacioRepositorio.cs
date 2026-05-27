using SOEA.Domain.Entities;
using SOEA.Domain.Interfaces;
using SOEA.Infrastructure.Data.Context;

namespace SOEA.Infrastructure.Data.Repositories
{
    public class EspacioRepositorio : BaseRepository<Espacio>, IEspacioRepositorio
    {
        public EspacioRepositorio(SOEABdContext context) : base(context) { }
    }
}
