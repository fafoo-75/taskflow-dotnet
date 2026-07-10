using System.ComponentModel.DataAnnotations;

namespace TaskFlow.Models
{
    public class Category
    {

    public int Id { get; set; }

    [Required(ErrorMessage = "Le nom de la catégorie est requis")]
    [StringLength(50)]
    [Display(Name ="Category name")]
    public string Name { get; set; } = string.Empty;

    [Display(Name ="Color")]
    public string Color { get; set; } = "#3b5bdb";

    public ICollection<TodoTask> Tasks { get; set; } = new List<TodoTask>();
    }
}