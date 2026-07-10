// "using" importe des espaces de nom (namespaces).
// DataAnnotations contient les attributs de validatio
// comme [Required], [StringLenght] etc...

using System.ComponentModel.DataAnnotations;

// Le namespace correspond à l'emplacement du fichier dans le projet.
// TaskFlow = nom du projet, Models = nom du dossier.
namespace TaskFlow.Models
{
    public class TodoTask
    {
        // Classe C# tout ce qui a de plus normal.
        // Rien de spécifique au web - 
        // identitique à ce qu'on écrivait dans une app WinForms.
        public int Id { get; set; }

        // CategoryId : clé vers la table Categories
        // int? nullable car une tâche ne peut pas avoir de catégorie
        // Si CategoryId = null -> tâche sans catégorie (autorisé)
        [Display(Name = "Category")]
        public int? CategoryId { get; set; }

        [ScaffoldColumn(false)]

        public Category? Category { get; set;}
        public string? UserId { get; set;}



        //[Required] : champ obligatoire
        // Si le formulaire est soumis sans titre,
        // ASP.NET Core affichera automatiquement le message d'erreur.
        // [StringLenght] : Limite la longueur du texte.
        // [Display] : le libellé affiché dans les formulaires Razor 

        [Required(ErrorMessage = "Le titre est obligatoire")]
        [StringLength(200, ErrorMessage = "200 caractères maximum.")]
        [Display(Name = "Titre")]
        public string Titre {get; set; } = string.Empty;

        // Le ? après string signifie que cette propriété
        // peut être null (vide) - c'est optionnel.
        // Sans le ?, C# impose une valeur non nulle.

        [StringLength (1000)]
        [Display(Name = "Description")]
        public string? Description {get; set; }


        // Priorité est de type enum - une liste fermée de valeurs.
        // L'enum priorité est defini plus bas dans le fichier.

        [Display(Name = "Priorité")]
        public Priorite Priorite {get; set; } = Priorite.Moyenne;

        // bool = vrai ou faux. false par defaut = tâche non terminée.

        [Display(Name = "Tâche terminée")]
        public bool EstTerminee { get; set; } = false;

        // [Required] : La date est obligatoire
        // [DataType(DataType.Date)] = indique à Razor d'utiliser
        // un selecteur de calendrier dans le formulaire

        [Required(ErrorMessage = "La date d'échéance est obligatoire.")]
        [DataType(DataType.Date)]
        [Display(Name = "Date d'échéance")]
        public DateTime DateEcheance { get; set; } = DateTime.Today.AddDays(7);

        public ICollection<Comment> Comments { get; set; } = new List<Comment>();

        public ICollection<Attachment> Attachments { get; set; } = new List<Attachment>();

        [ScaffoldColumn(false)]
        [Display(Name = "Assigned to")]
        public string? AssignedToUserId { get; set; }

        [ScaffoldColumn(false)]
        public string? AssignedToUserName { get; set; }

        // int? = optionnel — une tâche peut ne pas avoir d'estimation.
        [Range(0, 1000, ErrorMessage = "L'estimation doit être comprise entre 0 et 1000 heures.")]
        [Display(Name = "Heures estimées")]
        public int? EstimatedHour { get; set; }

    }

    public enum Priorite
    {
        Basse = 0,
        Moyenne = 1,
        Haute = 2
    }


}