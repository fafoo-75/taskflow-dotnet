using System.ComponentModel.DataAnnotations;
using System.Data;

namespace TaskFlow.Models
{
    public class Attachment
    {
        public int Id { get; set; }

        // Nom original du fichier tel que l'user l'a téléchargé.
        // Exemple : "rapport_02.pdf
        [Required]
        public string OriginalName { get; set; } = string.Empty;


        // Nom unique du fichier sur le disque
        // On génére un nom unique (GUID) pour éviter les conflits
        // si 2 utilisateurs charge un fichier du même nom.
        // ex: "a3fukfkfu-....."
        [Required]
        public string StoredName { get; set; } = string.Empty;

        public string ContentType { get; set; } = string.Empty; // Type MIME.

        public long Size { get; set; }

        public DateTime UploadAt {get; set; } = DateTime.Now;

        public int TaskId { get; set; }

        [ScaffoldColumn(false)]
        public TodoTask? Task { get; set; }

        [ScaffoldColumn(false)]
        public string? UserId { get; set; }


    }
}