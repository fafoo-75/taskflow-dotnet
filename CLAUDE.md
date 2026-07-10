# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commandes

```bash
dotnet build                 # Compiler le projet
dotnet run                   # Lancer l'application (Program.cs)
dotnet watch run             # Lancer avec hot-reload

dotnet ef migrations add <Nom>   # Créer une migration après modif d'un modèle
dotnet ef database update        # Appliquer les migrations à taskflow.db (SQLite)
```

Il n'y a pas de suite de tests automatisés dans ce dépôt actuellement.

## Architecture

ASP.NET Core MVC (.NET, cible `net10.0`) avec authentification ASP.NET Identity et base SQLite (`taskflow.db`).

- **Program.cs** — pipeline de démarrage : `AddDbContext<AppDbContext>` (SQLite), `AddDefaultIdentity<IdentityUser>` + `AddRoles<IdentityRole>` + `AddEntityFrameworkStores<AppDbContext>`. Ordre important : `UseAuthentication()` doit précéder `UseAuthorization()`. `MapRazorPages()` sert les pages Identity (login/register), `MapControllerRoute` sert les contrôleurs MVC classiques (`{controller=Home}/{action=Index}/{id?}`).
- **Data/AppDbContext.cs** — hérite de `IdentityDbContext<IdentityUser>` (donc les tables Identity type AspNetUsers cohabitent avec les tables métier). `OnModelCreating` seed des `Category` et `TodoTask` initiales via `HasData` (ces seeds ne doivent être modifiés que par une nouvelle migration, pas en éditant les migrations existantes).
- **Models/** — `TodoTask` est l'entité centrale, reliée à `Category` (FK optionnelle), `Comment` et `Attachment` (collections enfants), avec `Priorite` (enum), `UserId` (propriétaire) et `AssignedToUserId`/`AssignedToUserName` (assignation à un autre utilisateur — le nom est dénormalisé pour éviter une jointure AspNetUsers à l'affichage). `DashboardViewModel` est un ViewModel pur (pas de table), avec `CompletedPercentage` calculé en propriété lambda.
- **Controllers/** — chaque contrôleur est `[Authorize]` sauf `HomeController`. Pattern systématique répété dans tous les contrôleurs (Todo, Comment, Attachment) :
  - Récupération de `userId` via `_userManager.GetUserId(User)` injecté en constructeur aux côtés de `AppDbContext`.
  - Chaque accès à une entité filtre TOUJOURS par propriétaire (`t.Id == id && t.UserId == userId`), pour empêcher un utilisateur d'accéder aux données d'un autre en modifiant l'Id dans l'URL → `NotFound()` sinon. Respecter ce pattern pour toute nouvelle action.
  - Pattern PRG (Post-Redirect-Get) après chaque POST réussi, avec message de confirmation via `TempData["Succes"]` / `TempData["Erreur"]`.
  - `ViewBag.Categories` / `ViewBag.Users` (SelectList) doivent être reconstruits à l'identique dans la branche GET et dans la branche `!ModelState.IsValid` du POST correspondant, sinon les dropdowns reviennent vides au réaffichage du formulaire.
- **AttachmentController** — upload de fichiers vers `wwwroot/uploads` avec nom généré par GUID (`StoredName`) distinct du nom original (`OriginalName`), limite 10 Mo, whitelist d'extensions. Vérifie systématiquement que la tâche liée (`Task.UserId`) appartient à l'utilisateur avant upload/download/delete.
- **Helpers/PaginatedList.cs** — pagination générique via `CreateAsync(IQueryable<T>, pageIndex, pageSize)` : exécute lui-même le `Skip`/`Take`, donc ne jamais appeler `.ToListAsync()` sur la requête avant de la lui passer.
- **Views/** — Razor views classiques par contrôleur (Todo, Category, Tag, Home), layout partagé dans `Views/Shared/_Layout.cshtml`.

## Notes

- Les commentaires de code sont en français et volontairement pédagogiques (le dépôt sert de support d'apprentissage ASP.NET Core/EF Core) — conserver ce style dans les nouveaux fichiers.
- `Tag` existe comme modèle mais n'est pas encore relié à `TodoTask` (pas de table de jointure) ni exposé dans `AppDbContext` au-delà du `DbSet<Tag>`.
