using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SOEA.Infrastructure.Data.Context
{
    public class SOEABdContextFactory : IDesignTimeDbContextFactory<SOEABdContext>
    {
        public SOEABdContext CreateDbContext(string[] args)
        {
            var optionsBuilder = new DbContextOptionsBuilder<SOEABdContext>();
            optionsBuilder.UseNpgsql("Host=127.0.0.1;Port=5432;Database=SOEAdb;Username=postgres;Password=2356");

            return new SOEABdContext(optionsBuilder.Options);
        }
    }
}
