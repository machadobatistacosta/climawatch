using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ClimaWatch.Infrastructure;

public sealed class ClimaWatchDbContextFactory : IDesignTimeDbContextFactory<ClimaWatchDbContext>
{
    public ClimaWatchDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ClimaWatchDbContext>();
        
        // Connection string de fallback para design-time
        const string fallbackConnectionString = "Host=localhost;Port=5432;Database=climawatch;Username=postgres;Password=postgres";
        
        optionsBuilder.UseNpgsql(fallbackConnectionString);

        return new ClimaWatchDbContext(optionsBuilder.Options);
    }
}
