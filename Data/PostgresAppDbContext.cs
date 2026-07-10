using Microsoft.EntityFrameworkCore;

namespace TaskFlow.Data
{
    // Contexte utilisé en production (PostgreSQL / Railway).
    // Ses migrations vivent dans Migrations/Postgres.
    public class PostgresAppDbContext : AppDbContext
    {
        public PostgresAppDbContext(DbContextOptions<PostgresAppDbContext> options)
            : base(options) { }
    }
}
