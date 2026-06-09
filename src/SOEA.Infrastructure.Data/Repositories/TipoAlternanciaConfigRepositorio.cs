using SOEA.Domain.Entities;
using SOEA.Domain.Interfaces;
using SOEA.Infrastructure.Data.Context;

namespace SOEA.Infrastructure.Data.Repositories
{
    public class TipoAlternanciaConfigRepositorio
        : BaseRepository<TipoAlternanciaConfig>, ITipoAlternanciaConfigRepositorio
    {
        public TipoAlternanciaConfigRepositorio(SOEABdContext context) : base(context) { }
    }
}
