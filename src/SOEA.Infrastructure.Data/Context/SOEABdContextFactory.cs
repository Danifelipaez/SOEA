using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SOEA.Infrastructure.Data.Context
{
    public class SOEABdContextFactory : IDesignTimeDbContextFactory<SOEABdContext>
    {
        public SOEABdContext CreateDbContext(string[] args)
        {
            var optionsBuilder = new DbContextOptionsBuilder<SOEABdContext>();
            optionsBuilder.UseNpgsql("Host=localhost;Database=SOEA_DB;Username=postgres;Password=postgres");

            return new SOEABdContext(optionsBuilder.Options);
        }
    }
}
