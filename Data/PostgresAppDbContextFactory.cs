using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TaskFlow.Data
{
    public class PostgresAppDbContextFactory : IDesignTimeDbContextFactory<PostgresAppDbContext>
    {
        public PostgresAppDbContext CreateDbContext(string[] args)
        {
            var optionsBuilder = new DbContextOptionsBuilder<PostgresAppDbContext>();
            
            // Chaîne de connexion PostgreSQL locale pour les migrations
            // Remplace par ta vraie URL PostgreSQL si besoin
            optionsBuilder.UseNpgsql(
                "Host=localhost;Database=taskflow_dev;Username=postgres;Password=postgres"
            );

            return new PostgresAppDbContext(optionsBuilder.Options);
        }
    }
}
