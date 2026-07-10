using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using TaskFlow.Models;

namespace TaskFlow.Data
{
    public class AppDbContext : IdentityDbContext<IdentityUser>
    {
        public AppDbContext(DbContextOptions<AppDbContext> options)
            : base(options){ }

        public DbSet<TodoTask> TodoTasks { get; set; }

        // Table Categories - une ligne par ligne
        public DbSet<Category> Categories { get; set; }

        public DbSet<Comment> Comments { get; set; }

        public DbSet<Attachment> Attachments { get; set; }

        public DbSet<Tag> Tags { get; set; }
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
                DateEcheance = new DateTime(2026, 7, 28)
                },

                new TodoTask 
                { 
                Id = 2, Titre = "Allez encore au sport" , 
                Priorite = Priorite.Basse, 
                EstTerminee = false, 
                DateEcheance = new DateTime(2026, 7, 29)
                }            
            );
        }
    }
}