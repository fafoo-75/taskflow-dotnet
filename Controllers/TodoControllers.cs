using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using TaskFlow.Data;
using TaskFlow.Models;
using TaskFlow.Helpers;


namespace TaskFlow.Controllers
{
    // [Authorize] sur la CLASSE entière = toutes les actions nécessitent
    // d'être connecté. Si l'utilisateur n'est pas connecté,
    // il est redirigé vers /Identity/Account/Login automatiquement.
    [Authorize]
    public class TodoController : Controller
    {
        // _context = notre AppDbContext injecté automatiquement.
        // Il représente la session de travail avec la base SQLite.
        private readonly AppDbContext _context;

        // UserManager permet d'accéder aux infos de l'utilisateur connecté.
        // On l'injecte dans le constructeur comme AppDbContext.
        private readonly UserManager<IdentityUser> _userManager;

        // Constructeur : ASP.NET Core injecte automatiquement
        // AppDbContext et UserManager grâce à AddDbContext()
        // et AddDefaultIdentity() déclarés dans Program.cs.
        public TodoController(AppDbContext context,
                              UserManager<IdentityUser> userManager)
        {
            _context     = context;
            _userManager = userManager;
        }

        // ── INDEX ────────────────────────────────────────────────
        // GET /Todo
        // Affiche la liste des tâches de l'utilisateur connecté.
        // Paramètre optionnel categoryId : filtre par catégorie si renseigné.
        // int? = nullable — si null, aucun filtre catégorie appliqué.
        // pageNumber : numéro de page demandé (défaut = 1 si absent de l'URL).
        public async Task<IActionResult> Index(
            int? categoryId,
            string? filtre,        // Switch sur filtre
            string? recherche,     // Switch sur recherche
            int pageNumber = 1)
        {
            // GetUserId(User) : récupère le GUID unique de l'utilisateur
            // connecté depuis le cookie de session Identity.
            var userId = _userManager.GetUserId(User);
            const int pageSize = 10; // 10 tâches par page

            // IQueryable = requête SQL pas encore exécutée.
            // On peut enchaîner des .Where() supplémentaires
            // sans déclencher de SELECT — c'est l'exécution différée.
            var query = _context.TodoTasks
                .Where(t => t.UserId == userId)
                .Include(t => t.Category)   // charge la catégorie de chaque tâche
                .Include(t => t.Comments)   // charge les commentaire
                .AsNoTracking()             // lecture seule — plus rapide
                .AsQueryable();

            query = ApplyFilters(query, categoryId, filtre, recherche);

            // CORRECTION : on ne fait PAS de ToListAsync() ici.
            // PaginatedList.CreateAsync() exécute lui-même le SELECT
            // avec Skip/Take — une seule requête paginée.
            // OrderBy avant de passer à PaginatedList.
            var paginatedTasks = await PaginatedList<TodoTask>.CreateAsync(
                query
                    .OrderBy(t => t.EstTerminee)    // non-terminées en premier
                    .ThenBy(t => t.DateEcheance),         // puis par date d'échéance
                pageNumber,
                pageSize
            );

            // ViewBag.Categories : liste des catégories pour la DropDownList de filtre.
            ViewBag.Categories = new SelectList(
                await _context.Categories.OrderBy(c => c.Name).ToListAsync(),
                "Id",
                "Name"
            );

            ViewBag.CategoryId = categoryId;
            ViewBag.Filtre     = filtre;      //  pour conserver la sélection
            ViewBag.Recherche  = recherche;   //  pour conserver la recherche


            // Conserver la catégorie sélectionnée pour la remettre en place
            // après rechargement de la page.
            ViewBag.CategoryId = categoryId;

            return View(paginatedTasks);
        }

        // ── APPLY FILTERS ────────────────────────────────────────
        // Applique les filtres de statut, recherche texte et catégorie
        // à la requête. Ne s'exécute pas immédiatement (IQueryable).
        private static IQueryable<TodoTask> ApplyFilters(
            IQueryable<TodoTask> query,
            int? categoryId,
            string? filtre,
            string? recherche)
        {
            // Filtre par statut
            query = filtre switch
            {
                "inprogress" => query.Where(t => !t.EstTerminee),
                "completed"  => query.Where(t => t.EstTerminee),
                "high"       => query.Where(t => t.Priorite == Priorite.Haute),
                _            => query
            };

            // Filtre par recherche texte
            if (!string.IsNullOrWhiteSpace(recherche))
                query = query.Where(t => t.Titre.Contains(recherche));

            // Filtre par catégorie
            if (categoryId.HasValue)
                query = query.Where(t => t.CategoryId == categoryId.Value);

            return query;
        }

        // ── CREATE GET ───────────────────────────────────────────
        // GET /Todo/Create
        // Affiche le formulaire vide de création d'une tâche.
        public async Task<IActionResult> Create()
        {
            // ViewBag.Categories : alimente la DropDownList de catégories
            // dans le formulaire de création.
            ViewBag.Categories = new SelectList(
                await _context.Categories.OrderBy(c => c.Name).ToListAsync(),
                "Id",
                "Name"
            );
            var users = await _userManager.Users
                .OrderBy(u => u.Email)
                .Select(u => new { u.Id, u.Email})
                .ToListAsync();

            ViewBag.Users = new SelectList(users, "Id", "Email");

            return View();
        }

        // ── CREATE POST ──────────────────────────────────────────
        // POST /Todo/Create
        // Reçoit les données du formulaire et crée la tâche en base.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(TodoTask task)
        {
            // ModelState.IsValid : vérifie les DataAnnotations de TodoTask
            // ([Required], [StringLength] etc.)
            // Si Title est vide par exemple, IsValid = false.
            if (!ModelState.IsValid)
            {
                // IMPORTANT : reconstruire ViewBag.Categories si la validation
                // échoue — sinon la DropDownList est vide quand le formulaire
                // est réaffiché avec les messages d'erreur.
                ViewBag.Categories = new SelectList(
                    await _context.Categories.ToListAsync(), "Id", "Name");
                ViewBag.Users = new SelectList(
                    await _userManager.Users.ToListAsync(), "Id", "Email");

                return View(task);
            }

            // On associe la tâche à l'utilisateur connecté.
            // Sans ça, la tâche n'aurait pas de propriétaire.
            task.UserId = _userManager.GetUserId(User);

            if (!string.IsNullOrEmpty(task.AssignedToUserId))
            {
                var assignedUser = await _userManager.FindByIdAsync(task.AssignedToUserId);
                task.AssignedToUserName = assignedUser?.Email;
            }

            // Add() : signale à EF Core que cette tâche doit être insérée.
            _context.Add(task);

            // SaveChangesAsync() : exécute le INSERT SQL dans SQLite.
            // L'Id est assigné automatiquement par SQLite.
            await _context.SaveChangesAsync();

            // TempData : message qui survit à UNE redirection.
            TempData["Succes"] = "Tâche créée avec succès.";

            // Pattern PRG (Post-Redirect-Get) : on redirige toujours
            // après un POST réussi pour éviter la double soumission.
            return RedirectToAction(nameof(Index));
        }

        // ── EDIT GET ─────────────────────────────────────────────
        // GET /Todo/Edit/5
        // Récupère la tâche et affiche le formulaire prérempli.
        public async Task<IActionResult> Edit(int id)
        {
            var userId = _userManager.GetUserId(User);

            // Double condition : Id ET UserId doivent correspondre.
            // Empêche un utilisateur d'accéder à la tâche d'un autre
            // en modifiant l'Id dans l'URL — retourne 404 dans ce cas.
            var task = await _context.TodoTasks
                .FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId);

            if (task == null)
                return NotFound();

            // SelectList avec valeur présélectionnée.
            // Le 4ème paramètre = categoryId actuel de la tâche.
            // Razor sélectionne automatiquement la bonne option dans le <select>.
            ViewBag.Categories = new SelectList(
                await _context.Categories.OrderBy(c => c.Name).ToListAsync(),
                "Id",
                "Name",
                task.CategoryId
            );
            var users = await _userManager.Users
                .OrderBy(u => u.Email)
                .Select(u => new { u.Id, u.Email })
                .ToListAsync();

            ViewBag.Users = new SelectList(users, "Id", "Email",
            task.AssignedToUserId); // valeur présélectionnée


            // On passe la tâche à la vue — asp-for préremplira les champs.
            return View(task);
        }

        // ── EDIT POST ────────────────────────────────────────────
        // POST /Todo/Edit/5
        // Reçoit les données modifiées et met à jour la tâche en base.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, TodoTask taskModified)
        {
            // Vérification de sécurité : l'Id dans l'URL doit correspondre
            // à l'Id dans le formulaire caché (input type="hidden" asp-for="Id").
            if (id != taskModified.Id)
                return BadRequest();

            if (!ModelState.IsValid)
            {
                // Reconstruire ViewBag.Categories si la validation échoue.
                ViewBag.Categories = new SelectList(
                    await _context.Categories.OrderBy(c => c.Name).ToListAsync(),
                    "Id",
                    "Name",
                    taskModified.CategoryId
                );
                return View(taskModified);
            }

            var userId = _userManager.GetUserId(User);

            // Double condition Id + UserId — sécurité.
            var task = await _context.TodoTasks
                .FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId);

            if (task == null)
                return NotFound();

            // Mise à jour des propriétés — toutes en anglais.
            // CategoryId ajouté pour conserver le lien avec la catégorie.
            task.Titre        = taskModified.Titre;
            task.Description = taskModified.Description;
            task.Priorite     = taskModified.Priorite;
            task.EstTerminee  = taskModified.EstTerminee;
            task.DateEcheance = taskModified.DateEcheance;
            task.CategoryId  = taskModified.CategoryId;
            task.AssignedToUserId = taskModified.AssignedToUserId;
            task.EstimatedHour = taskModified.EstimatedHour;

            if (!string.IsNullOrEmpty(task.AssignedToUserId))
            {
                var assignedUser = await _userManager.FindByIdAsync(task.AssignedToUserId);
                task.AssignedToUserName = assignedUser?.Email;
            }
            else
            {
                task.AssignedToUserName = null;
            }

            // SaveChangesAsync() : exécute UPDATE TodoTasks SET ... WHERE Id = X
            await _context.SaveChangesAsync();

            TempData["Succes"] = "Tâche modifiée avec succès.";
            return RedirectToAction(nameof(Index));
        }

        // ── DELETE GET ───────────────────────────────────────────
        // GET /Todo/Delete/5
        // Affiche une page de confirmation avant la suppression.
        // Un GET ne doit jamais modifier des données.
        public async Task<IActionResult> Delete(int id)
        {
            var userId = _userManager.GetUserId(User);

            var task = await _context.TodoTasks
                .FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId);

            if (task == null)
                return NotFound();

            // On affiche la tâche pour que l'utilisateur confirme
            // qu'il supprime bien la bonne tâche.
            return View(task);
        }

        // ── DELETE POST ──────────────────────────────────────────
        // POST /Todo/Delete/5
        // Exécute la suppression après confirmation.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var userId = _userManager.GetUserId(User);

            var task = await _context.TodoTasks
                .FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId);

            if (task != null)
            {
                // Remove() : signale à EF Core que cette tâche doit être supprimée.
                _context.TodoTasks.Remove(task);

                // SaveChangesAsync() : exécute DELETE FROM TodoTasks WHERE Id = X
                await _context.SaveChangesAsync();
            }

            TempData["Succes"] = "Tâche supprimée.";
            return RedirectToAction(nameof(Index));
        }

        // ── TOGGLE TERMINÉE ──────────────────────────────────────
        // POST /Todo/ToggleTerminee/5
        // Bascule le statut IsCompleted sans passer par le formulaire Edit.
        // Action rapide depuis la liste — bouton checkbox inline.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleTerminee(int id)
        {
            var userId = _userManager.GetUserId(User);

            var task = await _context.TodoTasks
                .FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId);

            if (task == null)
                return NotFound();

            // ! = opérateur NOT : inverse la valeur booléenne.
            // true devient false, false devient true.
            task.EstTerminee = !task.EstTerminee;

            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }

        // ── DETAILS ──────────────────────────────────────────────
        // GET /Todo/Details/5
        // Affiche les détails d'une tâche et ses commentaires.
        public async Task<IActionResult> Details(int id)
        {
            var userId = _userManager.GetUserId(User);

            // On charge la tâche avec ses commentaires ET sa catégorie
            // en une seule requête SQL grâce aux deux Include().
            var task = await _context.TodoTasks
                .Include(t => t.Category)
                .Include(t => t.Comments
                    .OrderByDescending(c => c.CreatedAt)) // plus récent en premier
                .Include(t => t.Attachments
                    .OrderByDescending(a => a.UploadAt))
                .FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId);

            if (task == null) return NotFound();

            return View(task);
        }
    }
}