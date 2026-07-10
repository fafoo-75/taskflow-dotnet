using Microsoft.EntityFrameworkCore;

namespace TaskFlow.Data
{
    // Contexte utilisé en développement (SQLite).
    // Ses migrations vivent dans Migrations/Sqlite.
    public class SqliteAppDbContext : AppDbContext
    {
        public SqliteAppDbContext(DbContextOptions<SqliteAppDbContext> options)
            : base(options) { }
    }
}
