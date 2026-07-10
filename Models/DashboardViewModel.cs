// Un ViewModel est une classe créée SPÉCIFIQUEMENT pour une vue.
// Elle ne correspond pas à une table en base de données
// c'est un conteneur de données adapté à l'affichage.

namespace TaskFlow.Models
{
    public class DashboardViewModel
    {
        // Statistiques globales des tâches de l'user connecté
        public int Total { get; set; }
        public int Completed { get; set; }
        public int InProgress { get; set; }
        public int Late { get; set; }

        // Propriété CALCULÉE - pas setter.
        // Elle est dérivée d'autres propriétés automatiquement.
        // => est une expression lambda : si Total >0, calcule le %.
        // Terniaire : condition ? valeur_si_vrai : valeur_si_faux
        public int CompletedPercentage =>
            Total > 0 ? (int)Math.Round(Completed * 100.0 / Total) : 0; 

        // Les 5 tâches les plus urgentes à afficher sur le dashboard
        // Liste initialisée à vide pour éviter les NullReferenceException.
        public List<TodoTask> TopTasks { get; set; } = new();
    }
}