using System.ComponentModel.DataAnnotations;

namespace TaskFlow.Models
{
    public class Comment
    {
        public int Id { get; set; }

        // Contenu du commentaire - obligatoire , 1000 caract max
        [Required(ErrorMessage = "Le commentaire ne peut être vide.")]
        [StringLength(1000)]
        [Display(Name = "Commenter")]
        public string Content { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.Now; // Date de création assignée automatiquement au contrôleur

        public int TaskId { get; set; } // int (pas int?) = obligatoire - pas de comm sans tâche

        // Propriété de navigation vers la tâche parente
        // Permet d'accéder à task.Comment.Task sans reqête supplémentaire.
        [ScaffoldColumn(false)]
        public TodoTask? Task{ get; set; }

        // GUID  de l'user qui a posté le commentaire
        [ScaffoldColumn(false)]
        public string? UserId { get; set; }

        // Nom de l'user - stocké pour éviter une jointure
        // sur AspNetUsers à chaque affichage des commentaires.
        [ScaffoldColumn(false)]
        public string? UserName { get; set; }

    }
}