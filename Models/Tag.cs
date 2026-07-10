using System.ComponentModel.DataAnnotations;

namespace TaskFlow.Models
{
    public class Tag
    {

    public int Id { get; set; }

    [Required(ErrorMessage = "Le nom du tag est requis")]
    [StringLength(50)]
    [Display(Name ="Tag name")]
    public string Name { get; set; } = string.Empty;

    [Display(Name ="Color")]
    public string Color { get; set; } = "#3b5bdb";

    [ScaffoldColumn(false)]
    public string? UserId { get; set; }
    }
}
