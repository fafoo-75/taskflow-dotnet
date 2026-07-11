using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TaskFlow.Models;

namespace TaskFlow.Data
{
    // IDataProtectionKeyContext : permet de stocker les clés de chiffrement
    // ASP.NET (antiforgery, cookies d'auth) dans la base plutôt que dans le
    // système de fichiers éphémère du conteneur Railway.
    public class AppDbContext : IdentityDbContext<IdentityUser>, IDataProtectionKeyContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options)
            : base(options){ }

        // Constructeur protégé nécessaire pour les contextes dérivés
        // (SqliteAppDbContext / PostgresAppDbContext) qui portent chacun
        // leur propre jeu de migrations.
        protected AppDbContext(DbContextOptions options)
            : base(options){ }

        public DbSet<TodoTask> TodoTasks { get; set; }

        // Table Categories - une ligne par ligne
        public DbSet<Category> Categories { get; set; }

        public DbSet<Comment> Comments { get; set; }

        public DbSet<Attachment> Attachments { get; set; }

        public DbSet<Tag> Tags { get; set; }

        // Table des clés de chiffrement Data Protection.
        public DbSet<DataProtectionKey> DataProtectionKeys { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            base.OnConfiguring(optionsBuilder);

            // La longueur des colonnes clés Identity (varchar(128) vs text) diffère
            // de façon non déterministe entre le runtime et l'outil « dotnet ef ».
            // Cet écart est purement cosmétique (mêmes chaînes stockées), donc on
            // empêche EF de bloquer le démarrage/migration à cause de ce faux positif.
            optionsBuilder.ConfigureWarnings(w =>
                w.Ignore(RelationalEventId.PendingModelChangesWarning));
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Category>().HasData(
                // Catégories initiales insérées à la première migration.
                new Category { Id = 1, Name = "Travail", Color = "#3b5bdb"},
                new Category { Id = 2, Name = "Perso", Color = "#16a34a"},
                new Category { Id = 3, Name = "Urgent", Color = "#dc2626"},
                new Category { Id = 4, Name = "Entrainement", Color = "#ea580c"}
            );

            modelBuilder.Entity<TodoTask>().HasData
            (
                new TodoTask 
                { 
                Id = 1, Titre = "Allez au sport" ,
                Priorite = Priorite.Basse,
                EstTerminee = false,
                DateEcheance = new DateTime(2026, 7, 28, 0, 0, 0, DateTimeKind.Utc)
                },

                new TodoTask 
                { 
                Id = 2, Titre = "Allez encore au sport" ,
                Priorite = Priorite.Basse,
                EstTerminee = false,
                DateEcheance = new DateTime(2026, 7, 29, 0, 0, 0, DateTimeKind.Utc)
                }
            );
        }
    }
}