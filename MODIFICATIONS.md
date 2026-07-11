# Modifications — Correctifs déploiement Railway & erreurs 500

Ce document récapitule **toutes les modifications** apportées pour corriger le
déploiement sur Railway et les erreurs 500 en production (PostgreSQL).

## Contexte des problèmes

L'application fonctionnait en local (SQLite) mais échouait en ligne (PostgreSQL) :

1. **Build cassé** sur Railway : erreur `CS9137` (générateur OpenAPI).
2. **Crash au démarrage** : `DATABASE_URL` fournie par Railway au format URI, illisible par Npgsql.
3. **Migrations SQLite incompatibles** avec PostgreSQL.
4. **500 à la création de tâche** — deux causes cumulées :
   - Clés *Data Protection* régénérées à chaque redémarrage (antiforgery cassé).
   - Dates de formulaire (`Kind=Unspecified`) refusées par les colonnes `timestamptz`.

> Pourquoi seulement en ligne ? SQLite ne vérifie ni le `Kind` des `DateTime`,
> ni le chiffrement persistant des tokens — le bug n'apparaît qu'avec PostgreSQL.

---

## 1. `TaskFlow.csproj`

### 1.1 — Correction du build (CS9137)

**Ancien :**
```xml
<PropertyGroup>
  <TargetFramework>net10.0</TargetFramework>
  <Nullable>enable</Nullable>
  <ImplicitUsings>enable</ImplicitUsings>
</PropertyGroup>
```

**Nouveau :**
```xml
<PropertyGroup>
  <TargetFramework>net10.0</TargetFramework>
  <Nullable>enable</Nullable>
  <ImplicitUsings>enable</ImplicitUsings>
  <InterceptorsNamespaces>$(InterceptorsNamespaces);Microsoft.AspNetCore.OpenApi.Generated</InterceptorsNamespaces>
</PropertyGroup>
```
> Le générateur de source OpenAPI utilise la fonctionnalité C# « interceptors »,
> désactivée par défaut. On autorise son namespace.

### 1.2 — Ajout du package Data Protection

**Ancien :** *(absent)*

**Nouveau :** *(ajouté dans `<ItemGroup>`)*
```xml
<PackageReference Include="Microsoft.AspNetCore.DataProtection.EntityFrameworkCore" Version="10.0.9" />
```
> Permet de stocker les clés de chiffrement en base de données.

---

## 2. `.gitignore`

**Ancien :** *(pas de règle pour le dossier de publication)*

**Nouveau :** *(ajouté en fin de fichier)*
```gitignore
# Dossier de publication (dotnet publish -o out)
/out/
```

---

## 3. `Program.cs`

### 3.1 — Mode legacy timestamp Npgsql (fix 500 sur les dates)

**Ancien :** *(absent)*

**Nouveau :** *(tout en haut, avant `WebApplication.CreateBuilder`)*
```csharp
// Autorise Npgsql à écrire des DateTime Kind=Unspecified (dates saisies dans les
// formulaires) dans des colonnes timestamp. Sans ça : « Cannot write DateTime with
// Kind=Unspecified to PostgreSQL type 'timestamp with time zone' » → 500 à la
// création de tâche. Doit être défini avant toute utilisation de Npgsql.
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
```

Ajout du `using` correspondant en tête de fichier :
```csharp
using Microsoft.AspNetCore.DataProtection;
```

### 3.2 — Enregistrement de la base de données

**Ancien :**
```csharp
var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL");

if (!string.IsNullOrEmpty(databaseUrl))
{
    // Produiction : PostgreSQL sur Railway
    builder.Services.AddDbContext<AppDbContext>(options =>
        options.UseNpgsql(databaseUrl));
}
else
{
    // Développement : SQLite
    builder.Services.AddDbContext<AppDbContext>(options =>
        options.UseSqlite(
            builder.Configuration.GetConnectionString("DefaultConnection")
        ));
}

// Ajouter les services au conteneur.
builder.Services.AddControllersWithViews();

// ⚠️ AddDbContext EN DOUBLE (écrasait la config ci-dessus)
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(
        builder.Configuration.GetConnectionString("DefaultConnection")));
```

**Nouveau :**
```csharp
var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL");

if (!string.IsNullOrEmpty(databaseUrl))
{
    // Production : PostgreSQL sur Railway.
    // Railway fournit DATABASE_URL au format URI (postgresql://user:pass@host:port/db),
    // que Npgsql ne sait pas lire directement. On le convertit en chaîne clé-valeur.
    var connectionString = BuildNpgsqlConnectionString(databaseUrl);
    builder.Services.AddDbContext<PostgresAppDbContext>(options =>
        options.UseNpgsql(connectionString));

    // Les contrôleurs et Identity injectent AppDbContext : on le fait résoudre
    // vers le contexte PostgreSQL actif.
    builder.Services.AddScoped<AppDbContext>(sp => sp.GetRequiredService<PostgresAppDbContext>());
}
else
{
    // Développement : SQLite
    builder.Services.AddDbContext<SqliteAppDbContext>(options =>
        options.UseSqlite(
            builder.Configuration.GetConnectionString("DefaultConnection")
        ));

    builder.Services.AddScoped<AppDbContext>(sp => sp.GetRequiredService<SqliteAppDbContext>());
}
```
> - Suppression du `AddDbContext` en double (qui forçait SQLite).
> - Utilisation de contextes dédiés par provider (voir §5).
> - Conversion de l'URL Railway (voir §3.4).

### 3.3 — Persistance des clés Data Protection (fix 500 antiforgery)

**Ancien :** *(absent — clés stockées sur le filesystem éphémère du conteneur)*

**Nouveau :** *(juste avant `AddControllersWithViews`)*
```csharp
// Persiste les clés de chiffrement (antiforgery, cookies d'auth) dans la base.
// Sans ça, Railway régénère les clés à chaque redémarrage du conteneur
// (filesystem éphémère) → « antiforgery token could not be decrypted » → 500.
builder.Services.AddDataProtection()
    .PersistKeysToDbContext<AppDbContext>();
```

### 3.4 — Fonction de conversion de l'URL PostgreSQL

**Ancien :** *(absent)*

**Nouveau :** *(fonction locale ajoutée en fin de fichier, après `app.Run();`)*
```csharp
// Convertit une URL PostgreSQL au format URI (fournie par Railway/Heroku)
// en chaîne de connexion Npgsql clé-valeur.
static string BuildNpgsqlConnectionString(string databaseUrl)
{
    // Si ce n'est pas une URI (déjà au format clé-valeur), on la retourne telle quelle.
    if (!databaseUrl.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
        && !databaseUrl.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
    {
        return databaseUrl;
    }

    var uri = new Uri(databaseUrl);
    var userInfo = uri.UserInfo.Split(':', 2);

    var builder = new Npgsql.NpgsqlConnectionStringBuilder
    {
        Host = uri.Host,
        Port = uri.IsDefaultPort ? 5432 : uri.Port,
        Username = Uri.UnescapeDataString(userInfo[0]),
        Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : string.Empty,
        Database = uri.AbsolutePath.TrimStart('/'),
        // Prefer : utilise TLS s'il est disponible, sinon connexion en clair.
        // Compatible avec le réseau interne Railway (sans TLS) comme avec l'URL publique.
        SslMode = Npgsql.SslMode.Prefer
    };

    return builder.ConnectionString;
}
```

---

## 4. `Data/AppDbContext.cs`

### 4.1 — Usings & interface Data Protection

**Ancien :**
```csharp
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
```

**Nouveau :**
```csharp
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
```

### 4.2 — DbSet des clés + suppression du faux positif de migration

**Ancien :**
```csharp
        public DbSet<Tag> Tags { get; set; }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
```

**Nouveau :**
```csharp
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
```

### 4.3 — Dates de seed en UTC

**Ancien :**
```csharp
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
```

**Nouveau :**
```csharp
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
```
> Les données de seed sont insérées via migration dans une colonne `timestamptz`
> → elles doivent être en UTC.

---

## 5. `Data/SqliteAppDbContext.cs` *(nouveau fichier)*

**Ancien :** *(n'existait pas)*

**Nouveau :**
```csharp
using Microsoft.EntityFrameworkCore;

namespace TaskFlow.Data
{
    // Contexte utilisé en développement (SQLite).
    // Ses migrations vivent dans Migrations/Sqlite.
    public class SqliteAppDbContext : AppDbContext
    {
        public SqliteAppDbContext(DbContextOptions<SqliteAppDbContext> options)
            : base(options) { }
    }
}
```

---

## 6. `Data/PostgresAppDbContext.cs` *(nouveau fichier)*

**Ancien :** *(n'existait pas)*

**Nouveau :**
```csharp
using Microsoft.EntityFrameworkCore;

namespace TaskFlow.Data
{
    // Contexte utilisé en production (PostgreSQL / Railway).
    // Ses migrations vivent dans Migrations/Postgres.
    public class PostgresAppDbContext : AppDbContext
    {
        public PostgresAppDbContext(DbContextOptions<PostgresAppDbContext> options)
            : base(options) { }
    }
}
```

---

## 7. Dossier `Migrations/` *(restructuré)*

Les migrations sont des fichiers **générés** par `dotnet ef` — on ne les édite
pas à la main. Voici la restructuration effectuée.

### Ancien *(un seul jeu, généré pour SQLite, lié à `AppDbContext`)*
```
Migrations/
├── 20260702143056_InitialCreate.cs          (types SQLite : TEXT, INTEGER,
├── 20260703082806_AddIdentity.cs             annotation « Sqlite:Autoincrement »)
├── 20260703091133_AddUserIdToTodoTask.cs
├── 20260703143937_AddCategories.cs
├── 20260706141833_AddComments.cs
├── 20260707105538_AddCommentsTable.cs
├── 20260707122823_AddAttachments.cs
├── 20260707145159_AddAssignedTo.cs
├── 20260708115910_AddEstimatedHourToTodoTask.cs
└── AppDbContextModelSnapshot.cs
```
> Incompatible avec PostgreSQL (SQL spécifique SQLite).

### Nouveau *(deux jeux séparés par provider)*
```
Migrations/
├── Sqlite/                 (lié à SqliteAppDbContext — types SQLite)
│   ├── 20260710092634_InitialCreate.cs
│   ├── 20260711115653_AddDataProtectionKeys.cs
│   └── SqliteAppDbContextModelSnapshot.cs
└── Postgres/               (lié à PostgresAppDbContext — types PostgreSQL)
    ├── 20260710092657_InitialCreate.cs      (text, timestamptz,
    ├── 20260711115706_AddDataProtectionKeys.cs   identity by default)
    └── PostgresAppDbContextModelSnapshot.cs
```

La migration `AddDataProtectionKeys` crée la table de stockage des clés :
```csharp
migrationBuilder.CreateTable(
    name: "DataProtectionKeys",
    columns: table => new
    {
        Id = table.Column<int>(...),
        FriendlyName = table.Column<string>(nullable: true),
        Xml = table.Column<string>(nullable: true)
    },
    constraints: table => table.PrimaryKey("PK_DataProtectionKeys", x => x.Id));
```

### Commandes pour régénérer une future migration
```bash
# SQLite (dev)
ASPNETCORE_ENVIRONMENT=Development dotnet ef migrations add MonChangement \
  --context SqliteAppDbContext -o Migrations/Sqlite

# PostgreSQL (prod) — DATABASE_URL peut pointer vers n'importe quelle base PG
DATABASE_URL="postgresql://user:pass@host:5432/db" ASPNETCORE_ENVIRONMENT=Production \
  dotnet ef migrations add MonChangement --context PostgresAppDbContext -o Migrations/Postgres
```

---

## Récapitulatif des causes → correctifs

| Symptôme | Cause | Correctif |
|---|---|---|
| Build échoue (`CS9137`) | Interceptors OpenAPI désactivés | `<InterceptorsNamespaces>` (§1.1) |
| Crash au démarrage | `DATABASE_URL` au format URI | `BuildNpgsqlConnectionString` (§3.4) |
| Config DB écrasée | `AddDbContext` en double | Suppression (§3.2) |
| Migrations incompatibles | Migrations SQLite sur PostgreSQL | Deux jeux par provider (§5, §6, §7) |
| 500 création tâche (1) | Clés Data Protection éphémères | `PersistKeysToDbContext` (§3.3, §4) |
| 500 création tâche (2) | Dates `Kind=Unspecified` sur `timestamptz` | Mode legacy timestamp (§3.1) |

## Actions à ne pas oublier côté Railway
- 🔒 **Changer le mot de passe PostgreSQL** (il avait été exposé en clair un instant).
- ⚙️ Vérifier que la variable **`DATABASE_URL`** est bien définie sur le service applicatif.

---

## 8. Changements front-end (vues, CSS, bootstrap)

Cette section documente les modifications de l'interface (Razor Views + CSS)
faites en parallèle. Vu le volume (~2000 lignes), elles sont présentées en
**diff unifié** : les lignes commençant par `-` sont l'**ancien** code,
celles commençant par `+` sont le **nouveau** code ; les autres sont du contexte inchangé.

> Référence : diff entre l'état d'origine (`d03bad9`) et l'état actuel.

### `Views/Shared/_Layout.cshtml`

```diff
@@ -1,67 +1,136 @@
 ﻿<!DOCTYPE html>
-<html lang="en">
+<html lang="fr">
 <head>
     <meta charset="utf-8" />
     <meta name="viewport" content="width=device-width, initial-scale=1.0" />
     <title>@ViewData["Title"] - TaskFlow</title>
     <link rel="stylesheet" href="~/lib/bootstrap/dist/css/bootstrap.min.css" />
     <link rel="stylesheet" href="~/css/site.css" asp-append-version="true" />
-    <link rel="stylesheet" href="~/TaskFlow.styles.css" asp-append-version="true" />
 </head>
 <body>
-    <header>
-        <nav class="navbar navbar-expand-sm navbar-toggleable-sm navbar-light bg-white border-bottom box-shadow mb-3">
-            <div class="container-fluid">
-                <a class="navbar-brand" asp-area="" asp-controller="Home" asp-action="Index">TaskFlow</a>
-                <button class="navbar-toggler" type="button" data-bs-toggle="collapse" data-bs-target=".navbar-collapse" aria-controls="navbarSupportedContent"
-                        aria-expanded="false" aria-label="Toggle navigation">
-                    <span class="navbar-toggler-icon"></span>
-                </button>
-                <div class="navbar-collapse collapse d-sm-inline-flex justify-content-between">
-                    <ul class="navbar-nav flex-grow-1">
-                        <li class="nav-item">
-                            <a class="nav-link text-dark" asp-area="" asp-controller="Home" asp-action="Index">Home</a>
-                        </li>
-                        <li class="nav-item">
-                            <a asp-controller="Category" asp-action="Index" class="nav-link">Catégories</a>                        </li>
-                    </ul>
-                    <partial name="_LoginPartial" />
+
+@if (!User.Identity!.IsAuthenticated)
+{
+    @RenderBody()
+    IgnoreSection("TopbarActions");
+}
+else
+{
+    <div class="app-shell">
+
+        @* ── SIDEBAR ──────────────────────────────────────────── *@
+        <nav class="sidebar">
+            <div class="sidebar-brand">
+                <div class="logo-mark">T</div>
+                <div>
+                    <div class="brand-text">TaskFlow</div>
+                    <div class="brand-sub">v1.0.0</div>
+                </div>
+            </div>
+
+            <div class="sidebar-nav">
+                <div class="nav-group">
+                    <div class="nav-group-label">Navigation</div>
+
+                    <a class="nav-item @(ViewContext.RouteData.Values["controller"]?.ToString() == "Home" ? "active" : "")"
+                       asp-controller="Home" asp-action="Index">
+                        <span class="ni">📊</span> Tableau de bord
+                    </a>
+
+                    <a class="nav-item @(ViewContext.RouteData.Values["controller"]?.ToString() == "Todo" ? "active" : "")"
+                       asp-controller="Todo" asp-action="Index">
+                        <span class="ni">✅</span> Mes tâches
+                    </a>
+
+                    <a class="nav-item @(ViewContext.RouteData.Values["controller"]?.ToString() == "Category" ? "active" : "")"
+                       asp-controller="Category" asp-action="Index">
+                        <span class="ni">🏷️</span> Catégories
+                    </a>
+                </div>
+
+                <div class="nav-group">
+                    <div class="nav-group-label">Filtres rapides</div>
+
+                    <a class="nav-item" asp-controller="Todo" asp-action="Index"
+                       asp-route-filtre="inprogress">
+                        <span class="ni">🔵</span> En cours
+                    </a>
+
+                    <a class="nav-item" asp-controller="Todo" asp-action="Index"
+                       asp-route-filtre="completed">
+                        <span class="ni">🟢</span> Terminées
+                    </a>
+
+                    <a class="nav-item" asp-controller="Todo" asp-action="Index"
+                       asp-route-filtre="high">
+                        <span class="ni">⚡</span> Haute priorité
+                    </a>
                 </div>
             </div>
+
+            @* Zone utilisateur — en bas de la sidebar *@
+            <div class="sidebar-user">
+                <div class="user-avatar">
+                    @User.Identity.Name?.Substring(0, 1).ToUpper()
+                </div>
+                <div>
+                    <div class="user-name">@User.Identity.Name?.Split('@')[0]</div>
+                    <div class="user-role">Développeur</div>
+                </div>
+                <form asp-area="Identity" asp-page="/Account/Logout"
+                      asp-route-returnUrl="@Url.Action("Index", "Home")"
+                      method="post" style="margin-left:auto">
+                    @Html.AntiForgeryToken()
+                    <button type="submit" class="logout-btn" title="Se déconnecter">⏻</button>
+                </form>
+            </div>
         </nav>
-    </header>
-    <div class="container">
-        <main role="main" class="pb-3">
-        @* TempData ["Succes"] contient le message passé par le contrôleur. 
-        Il persiste le temps d'UNE puis disparaît automatiquement.
-        On vérifie qu'il n'est pas null avant d'afficher le bloc. *@
-
-        @if(TempData["Succes"] != null)
-        {
-            <div class="container mt-2">
-                <div class="alert alert-success alert-dismissible fade show"
-                role="alert">
-
-                @TempData["Succes"]
-
-                <button type="button" class="btn-close" data-bs-dismiss="alert"
-                aria-label="Close"></button>
-                
+
+        @* ── MAIN ─────────────────────────────────────────────── *@
+        <div class="main">
+
+            @* Topbar *@
+            <div class="topbar">
+                <div>
+                    <div class="topbar-title">@ViewData["Title"]</div>
+                    @if (ViewData["Subtitle"] != null)
+                    {
+                        <div class="topbar-sub">@ViewData["Subtitle"]</div>
+                    }
+                </div>
+                <div class="topbar-actions">
+                    @await RenderSectionAsync("TopbarActions", required: false)
                 </div>
             </div>
-        }
-            @RenderBody()
-        </main>
-    </div>
 
-    <footer class="border-top footer text-muted">
-        <div class="container">
-            &copy; 2026 - TaskFlow - <a asp-area="" asp-controller="Home" asp-action="Privacy">Privacy</a>
+            @* Contenu principal *@
+            <div class="content">
+
+                @if (TempData["Succes"] != null)
+                {
+                    <div class="toast success"
+                         style="position:static;margin-bottom:16px;display:flex;">
+                        ✓ @TempData["Succes"]
+                    </div>
+                }
+                @if (TempData["Erreur"] != null)
+                {
+                    <div class="toast error"
+                         style="position:static;margin-bottom:16px;display:flex;">
+                        ✕ @TempData["Erreur"]
+                    </div>
+                }
+
+                @RenderBody()
+            </div>
         </div>
-    </footer>
-    <script src="~/lib/jquery/dist/jquery.min.js"></script>
-    <script src="~/lib/bootstrap/dist/js/bootstrap.bundle.min.js"></script>
-    <script src="~/js/site.js" asp-append-version="true"></script>
-    @await RenderSectionAsync("Scripts", required: false)
+    </div>
+}
+
+<div class="toast-container" id="toastContainer"></div>
+
+<script src="~/lib/bootstrap/dist/js/bootstrap.bundle.min.js"></script>
+<script src="~/js/site.js" asp-append-version="true"></script>
+@await RenderSectionAsync("Scripts", required: false)
 </body>
 </html>
```

### `Views/Home/Index.cshtml`

```diff
@@ -1,92 +1,125 @@
 ﻿@model TaskFlow.Models.DashboardViewModel
 
-@{ViewData["Title"] = "Tableau de bord";}
-
-@if (!User.Identity!.IsAuthenticated)
-{
-    <div class="text-center py-5">
-        <h1>TaskFlow</h1>
-        <p class="lead text-muted mb-4">Gérez vos tâches professionnelles simplement.</p>
-        <a asp-area="Identity" asp-page="/Account/Login"
-        class="btn btn-primary btn-lg me-2">Se connecter</a>
-        <a asp-area="Identity" asp-page="/Account/Register"
-        class="btn btn-outline-secondary btn-lg">Créer un compte</a>    
-    </div>
+@{
+    ViewData["Title"] = "Tableau de bord";
+    ViewData["Subtitle"] = "Vue d'ensemble de vos tâches";
 }
-else{
-    @*Tableau de bord si connecté*@
 
-    <h2 class="h4 mb-4">Salut, @User.Identity.Name</h2>
+@section TopbarActions {
+    <a asp-controller="Todo" asp-action="Create" class="btn btn-primary">+ Nouvelle tâche</a>
+}
 
-    <div class="row g-3 mb-4">
-        <div class="col-md-3">
-            <div class="card text-center border-primary">
-                <div class="card-body">
-                <div class="display-6 fw-bold text-primary">@Model.Total</div>
-                <div class="text-muted small">Tâches totales</div>
+@if (!User.Identity!.IsAuthenticated)
+{
+    <div class="login-page">
+        <div class="login-card">
+            <div class="login-logo">
+                <div class="logo-mark">T</div>
+                <div>
+                    <div style="font-size:18px;font-weight:700;">TaskFlow</div>
+                    <div style="font-size:11px;color:var(--muted2);">Gestion de tâches</div>
                 </div>
             </div>
+            <h2>Bienvenue</h2>
+            <p>Connectez-vous pour gérer vos tâches.</p>
+            <a asp-area="Identity" asp-page="/Account/Login" class="btn btn-primary btn-block">
+                Se connecter →
+            </a>
         </div>
-        <div class="col-md-3">
-            <div class="card text-center border-success">
-                <div class="card-body">
-                <div class="display-6 fw-bold text-success">@Model.Completed</div>
-                <div class="text-muted small">Complète (@Model.CompletedPercentage%)</div>
-                </div>
-            </div>
-        </div> 
-        <div class="col-md-3">
-            <div class="card text-center border-warning">
-                <div class="card-body">
-                <div class="display-6 fw-bold text-warning">@Model.InProgress</div>
-                <div class="text-muted small">En cours</div>
-                </div>
-            </div>
+    </div>
+}
+else
+{
+    <!-- Cartes statistiques -->
+    <div class="stats-grid">
+        <div class="stat-card blue">
+            <div class="stat-label">Total des tâches</div>
+            <div class="stat-value">@Model.Total</div>
+            <div class="stat-sub">dans le système</div>
+        </div>
+        <div class="stat-card green">
+            <div class="stat-label">Terminées</div>
+            <div class="stat-value">@Model.Completed</div>
+            <div class="stat-sub">@Model.CompletedPercentage% du total</div>
+        </div>
+        <div class="stat-card orange">
+            <div class="stat-label">En cours</div>
+            <div class="stat-value">@Model.InProgress</div>
+            <div class="stat-sub">à compléter</div>
+        </div>
+        <div class="stat-card red">
+            <div class="stat-label">En retard</div>
+            <div class="stat-value">@Model.Late</div>
+            <div class="stat-sub">dépassement d'échéance</div>
         </div>
-        <div class="col-md-3">
-            <div class="card text-center border-danger">
-                <div class="card-body">
-                <div class="display-6 fw-bold text-danger">@Model.Late</div>
-                <div class="text-muted small">En retard</div>
-                </div>
-            </div>
-        </div>        
     </div>
 
-
-    @* Tâches prioritaires *@
-    <div class="d-flex justify-content-between align-items-center mb-3">
-        <h3 class="h5 mb-0">Tâches prioritaires</h3>
-        <a asp-controller="Todo" asp-action="Index"
-        class="btn btn-sm btn-outline-primary">Afficher tout</a>
+    <!-- Tâches prioritaires -->
+    <div class="section-header">
+        <h3>Tâches prioritaires</h3>
+        <a asp-controller="Todo" asp-action="Index">Voir toutes →</a>
     </div>
 
     @if (!Model.TopTasks.Any())
     {
-        <div class="alert alert-success">
-            Toutes les tâches ont été complétées !
+        <div class="empty-state">
+            <div class="empty-icon">🎉</div>
+            <div class="empty-title">Toutes les tâches sont terminées !</div>
+            <p>Créez de nouvelles tâches pour continuer.</p>
         </div>
     }
     else
     {
-        <div class="list-group">
+        <div class="task-table">
+            <div class="task-table-header">
+                <span>Titre</span>
+                <span>Priorité</span>
+                <span>Statut</span>
+                <span>Échéance</span>
+                <span></span>
+            </div>
             @foreach (var task in Model.TopTasks)
             {
-                <a asp-controller="Todo" asp-action="Edit" asp-route-id="@task.Id"
-                class="list-group-item list-group-item-action d-flex justify-content-between">
-                <div>
-                    <span class="fw-medium">@task.Titre</span>
-                    <small class="text-muted ms-2">
-                        - @task.DateEcheance.ToShortDateString()
-                    </small>
+                <div class="task-row @(task.EstTerminee ? "done" : "")">
+                    <div class="task-title-cell">
+                        <div>
+                            <div class="task-name">@task.Titre</div>
+                            @if (!string.IsNullOrEmpty(task.Description))
+                            {
+                                <div class="task-desc">@task.Description</div>
+                            }
+                        </div>
+                    </div>
+                    <div>
+                        <span class="badge badge-@task.Priorite.ToString().ToLower()">
+                            @task.Priorite
+                        </span>
+                    </div>
+                    <div>
+                        @if (task.EstTerminee)
+                        {
+                            <span class="badge badge-terminee">Terminée</span>
+                        }
+                        else if (task.DateEcheance < DateTime.Today)
+                        {
+                            <span class="badge badge-retard">En retard</span>
+                        }
+                        else
+                        {
+                            <span class="badge badge-encours">En cours</span>
+                        }
+                    </div>
+                    <div class="date-cell @(task.DateEcheance < DateTime.Today && !task.EstTerminee ? "retard" : "")">
+                        @task.DateEcheance.ToShortDateString()
+                    </div>
+                    <div class="task-actions">
+                        <a asp-controller="Todo" asp-action="Details" asp-route-id="@task.Id"
+                           class="btn btn-secondary btn-sm">Voir</a>
+                        <a asp-controller="Todo" asp-action="Edit" asp-route-id="@task.Id"
+                           class="btn btn-secondary btn-sm">✏️</a>
+                    </div>
                 </div>
-                <span class="badge @(task.Priorite == TaskFlow.Models.Priorite.Haute ? "bg-danger" : 
-                task.Priorite == TaskFlow.Models.Priorite.Moyenne ? "bg-warning text-dark" : "bg-success")">
-                @task.Priorite
-                </span>
-                </a>
             }
         </div>
     }
-
-}
\ No newline at end of file
+}
```

### `Views/Todo/index.cshtml`

```diff
@@ -1,184 +1,162 @@
 @model TaskFlow.Helpers.PaginatedList<TaskFlow.Models.TodoTask>
 
 @{
-    ViewData["Title"] = "Mes taches";
+    ViewData["Title"] = "Mes tâches";
+    ViewData["Subtitle"] = "Gérez et suivez vos tâches";
 }
 
-<h1>Mes taches</h1>
+@section TopbarActions {
+    <a asp-action="Create" class="btn btn-primary">+ Nouvelle tâche</a>
+}
 
-<p>
-    <a asp-action="Create" class="btn btn-primary">+ Nouvelle tache</a>
-</p>
+<!-- Filtres -->
+<div class="filters-row">
+    <form method="get" style="display:contents">
+        <a class="filter-btn @(ViewBag.Filtre == null ? "active" : "")"
+           asp-action="Index">Toutes</a>
+        <a class="filter-btn @(ViewBag.Filtre == "inprogress" ? "active" : "")"
+           asp-action="Index" asp-route-filtre="inprogress">En cours</a>
+        <a class="filter-btn @(ViewBag.Filtre == "completed" ? "active" : "")"
+           asp-action="Index" asp-route-filtre="completed">Terminées</a>
+        <a class="filter-btn @(ViewBag.Filtre == "high" ? "active" : "")"
+           asp-action="Index" asp-route-filtre="high">Haute priorité</a>
+
+        <div class="filters-right">
+            <input type="text" name="recherche"
+                   value="@ViewBag.Recherche"
+                   class="search-input"
+                   placeholder="Rechercher..."
+                   onchange="this.form.submit()" />
+            <select name="categoryId"
+                    class="sort-select"
+                    asp-items="ViewBag.Categories"
+                    onchange="this.form.submit()">
+                <option value="">Toutes les catégories</option>
+            </select>
+        </div>
+    </form>
+</div>
 
 @if (!Model.Any())
 {
-    <div class="alert alert-info">
-        Aucune tache. Creez votre premiere tache !
+    <div class="empty-state">
+        <div class="empty-icon">✅</div>
+        <div class="empty-title">Aucune tâche</div>
+        <p>Créez votre première tâche pour commencer.</p>
     </div>
 }
 else
 {
-    <form method="get" class="mb-3 d-flex gap-2 align-items-center flex-wrap">
-
-        <select name="filtre" class="form-select form-select-sm" style="width:auto"
-                onchange="this.form.submit()">
-            <option value="">Toutes les taches</option>
-            <option value="inprogress" selected="@(ViewBag.Filtre == "inprogress" ? "selected" : null)">Taches en cours</option>
-            <option value="completed"  selected="@(ViewBag.Filtre == "completed"  ? "selected" : null)">Taches terminees</option>
-            <option value="high"       selected="@(ViewBag.Filtre == "high"       ? "selected" : null)">Taches urgentes</option>
-        </select>
-
-        <select name="categoryId"
-                class="form-select form-select-sm"
-                style="width:auto"
-                asp-items="ViewBag.Categories"
-                onchange="this.form.submit()">
-            <option value="">Toutes les categories</option>
-        </select>
-
-        <input type="text" name="recherche"
-               value="@ViewBag.Recherche"
-               class="form-control form-control-sm"
-               style="width:200px"
-               placeholder="Rechercher..." />
-
-        <button type="submit" class="btn btn-sm btn-outline-secondary">Filtrer</button>
-        <a asp-action="Index" class="btn btn-sm btn-link">Reset</a>
-    </form>
-
-    <table class="table table-hover">
-        <thead>
-            <tr>
-                <th>Titre</th>
-                <th>Priorite</th>
-                <th>Echeance</th>
-                <th>Heures est.</th>
-                <th>Statut</th>
-                <th>Assignee a</th>
-                <th>Commentaires</th>
-                <th>Actions</th>
-            </tr>
-        </thead>
-        <tbody>
-            @foreach (var tache in Model)
-            {
-                <tr class="@(tache.EstTerminee ? "table-success" : "")">
-
-                    <td>
-                        @tache.Titre
-                        @if (tache.Category != null)
+    <div class="task-table">
+        <div class="task-table-header">
+            <span>Titre</span>
+            <span>Priorité</span>
+            <span>Statut</span>
+            <span>Échéance</span>
+            <span></span>
+        </div>
+
+        @foreach (var tache in Model)
+        {
+            <div class="task-row @(tache.EstTerminee ? "done" : "")">
+
+                <div class="task-title-cell">
+                    <!-- Bouton toggle checkbox -->
+                    <form asp-action="ToggleTerminee" asp-route-id="@tache.Id"
+                          method="post" style="display:contents">
+                        @Html.AntiForgeryToken()
+                        <button type="submit"
+                                class="task-checkbox @(tache.EstTerminee ? "checked" : "")">
+                        </button>
+                    </form>
+                    <div>
+                        <div class="task-name">@tache.Titre</div>
+                        @if (!string.IsNullOrEmpty(tache.Description))
                         {
-                            <span style="display:inline-flex;align-items:center;gap:5px">
-                                <span style="width:10px;height:10px;border-radius:50%;background:@tache.Category.Color;display:inline-block">
-                                </span>
-                                @tache.Category.Name
-                            </span>
+                            <div class="task-desc">@tache.Description</div>
                         }
-                        else
-                        {
-                            <span class="text-muted small">--</span>
-                        }
-                    </td>
-
-                    <td>@tache.Priorite</td>
-
-                    <td>@tache.DateEcheance.ToShortDateString()</td>
-
-                    <td>@(tache.EstimatedHour?.ToString() ?? "-")</td>
-
-                    <td>
-                        @if (tache.EstTerminee)
-                        {
-                            <span class="badge bg-success">Terminee</span>
-                        }
-                        else
-                        {
-                            <span class="badge bg-primary">En cours</span>
-                        }
-                    </td>
-
-                    <td>
-                        @if (!string.IsNullOrEmpty(tache.AssignedToUserName))
+                        @if (tache.Category != null)
                         {
-                            <span class="badge bg-secondary rounded-circle"
-                                  title="@tache.AssignedToUserName">
-                                @tache.AssignedToUserName[0].ToString().ToUpper()
+                            <span style="display:inline-flex;align-items:center;gap:4px;margin-top:2px;">
+                                <span style="width:8px;height:8px;border-radius:50%;
+                                       background:@tache.Category.Color;display:inline-block;"></span>
+                                <span style="font-size:11px;color:var(--muted);">@tache.Category.Name</span>
                             </span>
-                            <small class="text-muted ms-1">@tache.AssignedToUserName</small>
                         }
-                        else
-                        {
-                            <span class="text-muted small">--</span>
-                        }
-                    </td>
-
-                    <td>
-                        @if (tache.Comments.Any())
-                        {
-                            <span class="badge bg-secondary">@tache.Comments.Count</span>
-                        }
-                        else
-                        {
-                            <span class="text-muted">-</span>
-                        }
-                    </td>
-
-                    <td>
-                        <a asp-action="Details" asp-route-id="@tache.Id"
-                           class="btn btn-sm btn-outline-secondary">Voir</a>
-                        <a asp-action="Edit" asp-route-id="@tache.Id"
-                           class="btn btn-sm btn-outline-primary">Modifier</a>
-                        <a asp-action="Delete" asp-route-id="@tache.Id"
-                           class="btn btn-sm btn-outline-danger">Supprimer</a>
-                    </td>
-
-                </tr>
-            }
-        </tbody>
-    </table>
+                    </div>
+                </div>
+
+                <div>
+                    <span class="badge badge-@tache.Priorite.ToString().ToLower()">
+                        @tache.Priorite
+                    </span>
+                </div>
+
+                <div>
+                    @if (tache.EstTerminee)
+                    {
+                        <span class="badge badge-terminee">Terminée</span>
+                    }
+                    else if (tache.DateEcheance < DateTime.Today)
+                    {
+                        <span class="badge badge-retard">En retard</span>
+                    }
+                    else
+                    {
+                        <span class="badge badge-encours">En cours</span>
+                    }
+                </div>
+
+                <div class="date-cell @(tache.DateEcheance < DateTime.Today && !tache.EstTerminee ? "retard" : "")">
+                    @tache.DateEcheance.ToShortDateString()
+                </div>
+
+                <div class="task-actions">
+                    <a asp-action="Details" asp-route-id="@tache.Id"
+                       class="btn btn-secondary btn-sm">Voir</a>
+                    <a asp-action="Edit" asp-route-id="@tache.Id"
+                       class="btn btn-secondary btn-sm btn-icon">✏️</a>
+                    <a asp-action="Delete" asp-route-id="@tache.Id"
+                       class="btn btn-danger btn-sm btn-icon">🗑</a>
+                </div>
+
+            </div>
+        }
+    </div>
 
+    <!-- Pagination -->
     @if (Model.TotalPages > 1)
     {
-        <nav class="d-flex justify-content-between align-items-center mt-3">
-
-            <small class="text-muted">
-                Page @Model.PageIndex sur @Model.TotalPages
-                (@Model.TotalCount taches au total)
+        <div style="display:flex;justify-content:space-between;align-items:center;margin-top:16px;">
+            <small style="color:var(--muted);">
+                Page @Model.PageIndex sur @Model.TotalPages (@Model.TotalCount tâches)
             </small>
-
-            <ul class="pagination pagination-sm mb-0">
-
-                <li class="page-item @(!Model.HasPreviousPage ? "disabled" : "")">
-                    <a class="page-link"
+            <div style="display:flex;gap:4px;">
+                @if (Model.HasPreviousPage)
+                {
+                    <a class="btn btn-secondary btn-sm"
                        asp-action="Index"
                        asp-route-pageNumber="@(Model.PageIndex - 1)"
                        asp-route-filtre="@ViewBag.Filtre"
-                       asp-route-recherche="@ViewBag.Recherche">Precedent
-                    </a>
-                </li>
-
+                       asp-route-recherche="@ViewBag.Recherche">← Préc.</a>
+                }
                 @for (var i = 1; i <= Model.TotalPages; i++)
                 {
-                    <li class="page-item @(i == Model.PageIndex ? "active" : "")">
-                        <a class="page-link"
-                           asp-action="Index"
-                           asp-route-pageNumber="@i"
-                           asp-route-filtre="@ViewBag.Filtre"
-                           asp-route-recherche="@ViewBag.Recherche">@i
-                        </a>
-                    </li>
+                    <a class="btn @(i == Model.PageIndex ? "btn-primary" : "btn-secondary") btn-sm"
+                       asp-action="Index"
+                       asp-route-pageNumber="@i"
+                       asp-route-filtre="@ViewBag.Filtre"
+                       asp-route-recherche="@ViewBag.Recherche">@i</a>
                 }
-
-                <li class="page-item @(!Model.HasNextPage ? "disabled" : "")">
-                    <a class="page-link"
+                @if (Model.HasNextPage)
+                {
+                    <a class="btn btn-secondary btn-sm"
                        asp-action="Index"
                        asp-route-pageNumber="@(Model.PageIndex + 1)"
                        asp-route-filtre="@ViewBag.Filtre"
-                       asp-route-recherche="@ViewBag.Recherche">Suivant
-                    </a>
-                </li>
-
-            </ul>
-        </nav>
+                       asp-route-recherche="@ViewBag.Recherche">Suiv. →</a>
+                }
+            </div>
+        </div>
     }
-}
+}
\ No newline at end of file
```

### `Views/Todo/Create.cshtml`

```diff
@@ -1,69 +1,62 @@
-@* @model TodoTask : cette vue reçoit UN seul objet TodoTask du contrôleur.
-(Contrairement à Index qui recevait une List<TodoTask>.) *@
-
 @model TaskFlow.Models.TodoTask
-@{ ViewData["Title"] = "Nouvelle tâche"; }
-
-<h1>Nouvelle tâche</h1>
-
-<form asp-action="Create" method="post">
-    @Html.AntiForgeryToken()
-
-    <div asp-validation-summary="ModelOnly" class="alert alert-danger d-none"></div>
-
-    <div class="mb-3">
-            <label asp-for="Titre" class="form-label"></label>
-            <input asp-for="Titre" class="form-control" placeholder="Titre de la tâche"/>
-            <span asp-validation-for="Titre" class="text-danger small"></span>
+@{ ViewData["Title"] = "Nouvelle tâche"; ViewData["Subtitle"] = "Créer une nouvelle tâche"; }
+
+@section TopbarActions {
+    <a asp-action="Index" class="btn btn-secondary">← Retour</a>
+}
+
+<div style="max-width:560px;">
+    <div class="profile-card">
+        <form asp-action="Create" method="post">
+            @Html.AntiForgeryToken()
+            <div asp-validation-summary="ModelOnly" class="toast error" style="position:static;margin-bottom:16px;display:flex;"></div>
+
+            <div class="form-group">
+                <label asp-for="Titre"></label>
+                <input asp-for="Titre" class="form-control" placeholder="Ex : Préparer la réunion client" />
+                <span asp-validation-for="Titre" class="form-error show"></span>
+            </div>
+
+            <div class="form-group">
+                <label asp-for="Description"></label>
+                <textarea asp-for="Description" class="form-control" placeholder="Détails optionnels..."></textarea>
+            </div>
+
+            <div class="form-row">
+                <div class="form-group">
+                    <label asp-for="Priorite"></label>
+                    <select asp-for="Priorite" class="form-control"
+                            asp-items="Html.GetEnumSelectList<TaskFlow.Models.Priorite>()">
+                    </select>
+                </div>
+                <div class="form-group">
+                    <label asp-for="DateEcheance"></label>
+                    <input asp-for="DateEcheance" type="date" class="form-control" />
+                    <span asp-validation-for="DateEcheance" class="form-error show"></span>
+                </div>
+            </div>
+
+            <div class="form-row">
+                <div class="form-group">
+                    <label asp-for="CategoryId"></label>
+                    <select asp-for="CategoryId" class="form-control" asp-items="ViewBag.Categories">
+                        <option value="">-- Aucune catégorie --</option>
+                    </select>
+                </div>
+                <div class="form-group">
+                    <label asp-for="AssignedToUserId"></label>
+                    <select asp-for="AssignedToUserId" class="form-control" asp-items="ViewBag.Users">
+                        <option value="">-- Non assigné --</option>
+                    </select>
+                </div>
+            </div>
+
+            <div style="display:flex;gap:8px;margin-top:8px;">
+                <button type="submit" class="btn btn-primary">Enregistrer</button>
+                <a asp-action="Index" class="btn btn-secondary">Annuler</a>
+            </div>
+        </form>
     </div>
+</div>
 
-    <div class="mb-3">
-            <label asp-for="Description" class="form-label"></label>
-            <textarea asp-for="Description" class="form-control" rows="3" placeholder="Description"></textarea>
-            <span asp-validation-for="Description" class="text-danger small"></span>
-    </div>
-
-    <div class="row">
-        <div class="col-md-6 mb-3">
-            <label asp-for="Priorite" class="form-label"></label>
-            <select asp-for="Priorite" class="form-select" 
-                asp-items="Html.GetEnumSelectList<TaskFlow.Models.Priorite>()">
-            </select>
-        </div>
-        <div class="col-md-6 mb-3">
-            <label asp-for="CategoryId" class="form-label"></label>
-            <select asp-for="CategoryId" class="form-select" 
-                asp-items="ViewBag.Categories">
-                <option value="">-- Aucune catégorie --</option>
-            </select>
-        </div>
-        <div class="mb-3">
-            <label asp-for="AssignedToUserId" class="form-label"></label>
-            <select asp-for="AssignedToUserId" asp-items="ViewBag.Users"
-                    class="form-select">
-                    <option value="">-- Aucun --</option>
-            </select>
-        </div>
-        </div>        
-        <div class="col-md-6 mb-3">
-                <label asp-for="DateEcheance" class="form-label"></label>
-                <input asp-for="DateEcheance" type="date" class="form-control"/>
-                <span asp-validation-for="DateEcheance" class="text-danger small"></span>
-        </div>
-        <div class="col-md-6 mb-3">
-                <label asp-for="EstimatedHour" class="form-label"></label>
-                <input asp-for="EstimatedHour" type="number" min="0" class="form-control" placeholder="Heures estimées"/>
-                <span asp-validation-for="EstimatedHour" class="text-danger small"></span>
-        </div>
-     </div>
-
-    <div class="d-flex gap-2">
-        <button type="submit" class="btn btn-primary">Enregistrer</button>
-        <a asp-action="Index" class="btn btn-outline-secondary">Annuler</a>
-    </div>
-
-</form>
-
-@section Scripts{
-    @{await Html.RenderPartialAsync("_ValidationScriptsPartial");}
-}
\ No newline at end of file
+@section Scripts { @{await Html.RenderPartialAsync("_ValidationScriptsPartial");} }
\ No newline at end of file
```

### `Views/Todo/Edit.cshtml`

```diff
@@ -1,70 +1,68 @@
 @model TaskFlow.Models.TodoTask
-@{ ViewData["Title"] = "Modifier la tâche"; }
+@{ ViewData["Title"] = "Modifier la tâche"; ViewData["Subtitle"] = @Model.Titre; }
 
-<h1>Modifier la tâche</h1>
+@section TopbarActions {
+    <a asp-action="Index" class="btn btn-secondary">← Retour</a>
+    <a asp-action="Delete" asp-route-id="@Model.Id" class="btn btn-danger">🗑 Supprimer</a>
+}
 
-<form asp-action="Edit" method="post">
-    @Html.AntiForgeryToken()
+<div style="max-width:560px;">
+    <div class="profile-card">
+        <form asp-action="Edit" method="post">
+            @Html.AntiForgeryToken()
+            <input type="hidden" asp-for="Id" />
 
-    <input type="hidden" asp-for="Id"/>
+            <div class="form-group">
+                <label asp-for="Titre"></label>
+                <input asp-for="Titre" class="form-control" />
+                <span asp-validation-for="Titre" class="form-error show"></span>
+            </div>
 
-    <div class="mb-3">
-        <label asp-for="Titre" class="form-label"></label>
-        <input asp-for="Titre" class="form-control"/>
-        <span asp-validation-for="Titre" class="text-danger small"></span>
-    </div>
+            <div class="form-group">
+                <label asp-for="Description"></label>
+                <textarea asp-for="Description" class="form-control"></textarea>
+            </div>
 
-    <div class="mb-3">
-        <label asp-for="Description" class="form-label"></label>
-        <textarea asp-for="Description" class="form-control" rows="3"></textarea>
-        <span asp-validation-for="Description" class="text-danger small"></span>
-    </div>
+            <div class="form-row">
+                <div class="form-group">
+                    <label asp-for="Priorite"></label>
+                    <select asp-for="Priorite" class="form-control"
+                            asp-items="Html.GetEnumSelectList<TaskFlow.Models.Priorite>()">
+                    </select>
+                </div>
+                <div class="form-group">
+                    <label asp-for="DateEcheance"></label>
+                    <input asp-for="DateEcheance" type="date" class="form-control" />
+                    <span asp-validation-for="DateEcheance" class="form-error show"></span>
+                </div>
+            </div>
 
-    <div class="row">
-        <div class="col-md-6 mb-3">
-            <label asp-for="Priorite" class="form-label"></label>
-            <select asp-for="Priorite" class="form-select"
-            asp-items="Html.GetEnumSelectList<TaskFlow.Models.Priorite>()">
-            </select>
-        </div>
-        <div class="col-md-6 mb-3">
-            <label asp-for="CategoryId" class="form-label"></label>
-            <select asp-for="CategoryId"
-                    asp-items="ViewBag.Categories"
-                    class="form-select">
-                <option value="">- Aucune catégorie -</option>
-            </select>
-        </div>
-        <div class="mb-3">
-            <label asp-for="AssignedToUserId" class="form-label"></label>
-            <select asp-for="AssignedToUserId" asp-items="ViewBag.Users"
-                    class="form-select">
-                    <option value="">-- Aucun --</option>
-            </select>
-        </div>
-        <div class="col-md-6 mb-3">
-            <label asp-for="DateEcheance" class="form-label"></label>
-            <input asp-for="DateEcheance" type="date" class="form-control"/>
-            <span asp-validation-for="DateEcheance" class="text-danger small"></span>
-        </div>
-        <div class="col-md-6 mb-3">
-            <label asp-for="EstimatedHour" class="form-label"></label>
-            <input asp-for="EstimatedHour" type="number" min="0" class="form-control"/>
-            <span asp-validation-for="EstimatedHour" class="text-danger small"></span>
-        </div>
-    </div>
+            <div class="form-row">
+                <div class="form-group">
+                    <label asp-for="CategoryId"></label>
+                    <select asp-for="CategoryId" class="form-control" asp-items="ViewBag.Categories">
+                        <option value="">-- Aucune catégorie --</option>
+                    </select>
+                </div>
+                <div class="form-group">
+                    <label asp-for="AssignedToUserId"></label>
+                    <select asp-for="AssignedToUserId" class="form-control" asp-items="ViewBag.Users">
+                        <option value="">-- Non assigné --</option>
+                    </select>
+                </div>
+            </div>
 
-    <div class="mb-3 form-check">
-        <input asp-for="EstTerminee" class="form-check-input"/>
-        <label asp-for="EstTerminee" class="form-check-label"></label>
-    </div>
+            <div class="form-group" style="display:flex;align-items:center;gap:8px;">
+                <input asp-for="EstTerminee" type="checkbox" style="width:16px;height:16px;" />
+                <label asp-for="EstTerminee" style="margin:0;text-transform:none;font-size:13px;font-weight:500;"></label>
+            </div>
 
-    <div class="d-flex gap-2">
-        <button type="submit" class="btn btn-primary">Enregistrer</button>
-        <a asp-action="Index" class="btn btn-outline-secondary">Annuler</a>
+            <div style="display:flex;gap:8px;margin-top:8px;">
+                <button type="submit" class="btn btn-primary">Enregistrer les modifications</button>
+                <a asp-action="Index" class="btn btn-secondary">Annuler</a>
+            </div>
+        </form>
     </div>
-</form>
+</div>
 
-@section Scripts{
-    @{await Html.RenderPartialAsync("_ValidationScriptsPartial");}
-}
\ No newline at end of file
+@section Scripts { @{await Html.RenderPartialAsync("_ValidationScriptsPartial");} }
\ No newline at end of file
```

### `Views/Todo/Details.cshtml`

```diff
@@ -1,128 +1,174 @@
 @model TaskFlow.Models.TodoTask
 @inject Microsoft.AspNetCore.Identity.UserManager<Microsoft.AspNetCore.Identity.IdentityUser> _userManager
-@{ ViewData["Title"] = "Détails des tâches"; }
+@{ ViewData["Title"] = Model.Titre; ViewData["Subtitle"] = "Détails de la tâche"; }
 
-<div class="d-flex justify-content-between align-items-start mb-4">
-    <div>
-        <h1 class="h3">@Model.Titre</h1>
-        @if (Model.Category != null)
-        {
-            <span style="background:@Model.Category.Color;color:#fff"
-                class="badge">@Model.Category.Name</span>
-        }
-    </div>
-    <a asp-action="Edit" asp-route-id="@Model.Id" class="btn btn-outline-primary btn-sm">
-        Modifier
-    </a>
-</div>
-
-@* Détails de la tâche *@
-<dl class="row mb-4">
-    <dt class="col-sm-3">Description</dt>
-    <dd class="col-sm-9">@(Model.Description ?? "-")</dd>
-    <dt class="col-sm-3">Priorité</dt>
-    <dd class="col-sm-9">@Model.Priorite</dd>
-    <dt class="col-sm-3">Date échéance</dt>
-    <dd class="col-sm-9">@Model.DateEcheance.ToShortDateString()</dd>
-    <dt class="col-sm-3">Heures estimées</dt>
-    <dd class="col-sm-9">@(Model.EstimatedHour?.ToString() ?? "-")</dd>
-    <dt class="col-sm-3">Statut</dt>
-    <dd class="col-sm-9">
-        @if (Model.EstTerminee)
-        {
-            <span class="badge bg-success">Terminée</span>
-        }
-        else
-        {
-            <span class="badge bg-primary">En cours</span>
-        }
-    </dd>
-</dl>
+@section TopbarActions {
+    <a asp-action="Index" class="btn btn-secondary">← Retour</a>
+    <a asp-action="Edit" asp-route-id="@Model.Id" class="btn btn-primary">✏️ Modifier</a>
+}
 
-<hr />
+<div style="display:grid;grid-template-columns:1fr 340px;gap:20px;align-items:start;">
 
-@* ── SECTION COMMENTAIRES ────────────────────────────── *@
-<h4>Commentaires (@Model.Comments.Count)</h4>
+    <!-- Colonne gauche : détails + commentaires -->
+    <div>
+        <!-- Carte détails -->
+        <div class="profile-card" style="margin-bottom:16px;">
+            <div style="display:flex;align-items:center;gap:10px;margin-bottom:16px;">
+                <h2 style="font-size:18px;font-weight:700;">@Model.Titre</h2>
+                @if (Model.Category != null)
+                {
+                    <span style="display:inline-flex;align-items:center;gap:5px;
+                           background:var(--bg);padding:3px 10px;border-radius:12px;font-size:12px;">
+                        <span style="width:8px;height:8px;border-radius:50%;
+                               background:@Model.Category.Color;display:inline-block;"></span>
+                        @Model.Category.Name
+                    </span>
+                }
+            </div>
 
-<form asp-controller="Comment" asp-action="Add" method="post" class="mb-4">
-    @Html.AntiForgeryToken()
-    <input type="hidden" name="TaskId" value="@Model.Id" />
-    <div class="mb-2">
-        <textarea name="Content" class="form-control"
-                  rows="2" placeholder="Ajouter un commentaire..."></textarea>
-    </div>
-    <button type="submit" class="btn btn-sm btn-primary">Poster un commentaire</button>
-</form>
+            @if (!string.IsNullOrEmpty(Model.Description))
+            {
+                <p style="color:var(--muted);font-size:13px;margin-bottom:16px;">@Model.Description</p>
+            }
 
-@if (!Model.Comments.Any())
-{
-    <p class="text-muted">Aucun commentaire pour le moment, soyez le premier !</p>
-}
-else
-{
-    @foreach (var comment in Model.Comments)
-    {
-        <div class="card mb-2">
-            <div class="card-body py-2">
-                <div class="d-flex justify-content-between">
-                    <small class="text-muted fw-medium">
-                        @comment.UserName — @comment.CreatedAt.ToString("dd/MM/yyyy HH:mm")
-                    </small>
-                    @if (comment.UserId == _userManager.GetUserId(User))
+            <div style="display:grid;grid-template-columns:1fr 1fr;gap:12px;font-size:13px;">
+                <div>
+                    <div style="color:var(--muted);font-size:11px;text-transform:uppercase;font-weight:700;letter-spacing:.04em;margin-bottom:4px;">Priorité</div>
+                    <span class="badge badge-@Model.Priorite.ToString().ToLower()">@Model.Priorite</span>
+                </div>
+                <div>
+                    <div style="color:var(--muted);font-size:11px;text-transform:uppercase;font-weight:700;letter-spacing:.04em;margin-bottom:4px;">Statut</div>
+                    @if (Model.EstTerminee)
+                    {
+                        <span class="badge badge-terminee">Terminée</span>
+                    }
+                    else if (Model.DateEcheance < DateTime.Today)
                     {
-                        <form asp-controller="Comment" asp-action="Delete"
-                              asp-route-id="@comment.Id" method="post">
-                            @Html.AntiForgeryToken()
-                            <button type="submit"
-                                    class="btn btn-link btn-sm text-danger p-0"
-                                    onclick="return confirm('Supprimer ce commentaire ?')">
-                                Supprimer
-                            </button>
-                        </form>
+                        <span class="badge badge-retard">En retard</span>
                     }
+                    else
+                    {
+                        <span class="badge badge-encours">En cours</span>
+                    }
+                </div>
+                <div>
+                    <div style="color:var(--muted);font-size:11px;text-transform:uppercase;font-weight:700;letter-spacing:.04em;margin-bottom:4px;">Échéance</div>
+                    <span class="date-cell @(Model.DateEcheance < DateTime.Today && !Model.EstTerminee ? "retard" : "")">
+                        @Model.DateEcheance.ToShortDateString()
+                    </span>
                 </div>
-                <p class="mb-0 mt-1">@comment.Content</p>
+                @if (!string.IsNullOrEmpty(Model.AssignedToUserName))
+                {
+                    <div>
+                        <div style="color:var(--muted);font-size:11px;text-transform:uppercase;font-weight:700;letter-spacing:.04em;margin-bottom:4px;">Assigné à</div>
+                        <div style="display:flex;align-items:center;gap:6px;">
+                            <div class="user-avatar" style="width:24px;height:24px;font-size:10px;">
+                                @Model.AssignedToUserName[0].ToString().ToUpper()
+                            </div>
+                            <span>@Model.AssignedToUserName</span>
+                        </div>
+                    </div>
+                }
             </div>
         </div>
-    }
-}
 
-<hr />
+        <!-- Commentaires -->
+        <div class="profile-card">
+            <div class="section-header">
+                <h3>Commentaires (@Model.Comments.Count)</h3>
+            </div>
 
-@* ── SECTION PIÈCES JOINTES ─────────────────────────── *@
-<h4>Pièces jointes (@Model.Attachments.Count)</h4>
+            <form asp-controller="Comment" asp-action="Add" method="post" style="margin-bottom:16px;">
+                @Html.AntiForgeryToken()
+                <input type="hidden" name="TaskId" value="@Model.Id" />
+                <div class="form-group">
+                    <textarea name="Content" class="form-control" rows="2"
+                              placeholder="Ajouter un commentaire..."></textarea>
+                </div>
+                <button type="submit" class="btn btn-primary btn-sm">Poster</button>
+            </form>
 
-<form asp-controller="Attachment" asp-action="Upload"
-      method="post" enctype="multipart/form-data" class="mb-3">
-    @Html.AntiForgeryToken()
-    <input type="hidden" name="taskId" value="@Model.Id" />
-    <div class="d-flex gap-2">
-        <input type="file" name="file" class="form-control form-control-sm"
-               accept=".pdf,.doc,.docx,.xls,.xlsx,.png,.jpeg,.jpg,.txt" />
-        <button type="submit" class="btn btn-sm btn-primary">Upload</button>
+            @if (!Model.Comments.Any())
+            {
+                <p style="color:var(--muted);font-size:13px;">Aucun commentaire pour le moment.</p>
+            }
+            else
+            {
+                @foreach (var comment in Model.Comments)
+                {
+                    <div style="border:1px solid var(--border);border-radius:6px;padding:12px 14px;margin-bottom:10px;">
+                        <div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:6px;">
+                            <div style="display:flex;align-items:center;gap:8px;">
+                                <div class="user-avatar" style="width:24px;height:24px;font-size:10px;">
+                                    @comment.UserName?[0].ToString().ToUpper()
+                                </div>
+                                <span style="font-size:12px;font-weight:600;">@comment.UserName</span>
+                                <span style="font-size:11px;color:var(--muted);">
+                                    @comment.CreatedAt.ToString("dd/MM/yyyy HH:mm")
+                                </span>
+                            </div>
+                            @if (comment.UserId == _userManager.GetUserId(User))
+                            {
+                                <form asp-controller="Comment" asp-action="Delete"
+                                      asp-route-id="@comment.Id" method="post">
+                                    @Html.AntiForgeryToken()
+                                    <button type="submit" class="btn btn-danger btn-sm"
+                                            onclick="return confirm('Supprimer ?')">✕</button>
+                                </form>
+                            }
+                        </div>
+                        <p style="font-size:13px;color:var(--text);margin:0;">@comment.Content</p>
+                    </div>
+                }
+            }
+        </div>
     </div>
-</form>
 
-@foreach (var att in Model.Attachments)
-{
-    <div class="d-flex align-items-center gap-2 mb-2">
-        <span>📎</span>
-        <a asp-controller="Attachment" asp-action="Download"
-           asp-route-id="@att.Id">@att.OriginalName</a>
-        <small class="text-muted">
-            (@((att.Size / 1024.0).ToString("F1")) KB)
-        </small>
-        <form asp-controller="Attachment" asp-action="Delete"
-              asp-route-id="@att.Id" method="post">
+    <!-- Colonne droite : pièces jointes -->
+    <div class="profile-card">
+        <div class="section-header">
+            <h3>Pièces jointes (@Model.Attachments.Count)</h3>
+        </div>
+
+        <form asp-controller="Attachment" asp-action="Upload"
+              method="post" enctype="multipart/form-data" style="margin-bottom:14px;">
             @Html.AntiForgeryToken()
-            <button type="submit" class="btn btn-link btn-sm text-danger p-0"
-                    onclick="return confirm('Voulez-vous supprimer ce fichier ?')">
-                Supprimer
-            </button>
+            <input type="hidden" name="taskId" value="@Model.Id" />
+            <input type="file" name="file" class="form-control"
+                   style="margin-bottom:8px;"
+                   accept=".pdf,.doc,.docx,.xls,.xlsx,.png,.jpg,.txt" />
+            <button type="submit" class="btn btn-primary btn-sm" style="width:100%;">Upload</button>
         </form>
+
+        @if (!Model.Attachments.Any())
+        {
+            <p style="color:var(--muted);font-size:13px;">Aucun fichier joint.</p>
+        }
+        else
+        {
+            @foreach (var att in Model.Attachments)
+            {
+                <div style="display:flex;align-items:center;gap:8px;padding:8px 0;
+                             border-bottom:1px solid var(--border);">
+                    <span>📎</span>
+                    <div style="flex:1;min-width:0;">
+                        <a asp-controller="Attachment" asp-action="Download" asp-route-id="@att.Id"
+                           style="font-size:13px;display:block;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;">
+                            @att.OriginalName
+                        </a>
+                        <span style="font-size:11px;color:var(--muted);">
+                            @((att.Size / 1024.0).ToString("F1")) KB
+                        </span>
+                    </div>
+                    <form asp-controller="Attachment" asp-action="Delete"
+                          asp-route-id="@att.Id" method="post">
+                        @Html.AntiForgeryToken()
+                        <button type="submit" class="btn btn-danger btn-sm btn-icon"
+                                onclick="return confirm('Supprimer ?')">✕</button>
+                    </form>
+                </div>
+            }
+        }
     </div>
-}
 
-<a asp-action="Index" class="btn btn-outline-secondary btn-sm mt-3">
-    Retour à la liste
-</a>
+</div>
\ No newline at end of file
```

### `Views/Todo/Delete.cshtml`

```diff
@@ -1,42 +1,38 @@
 @model TaskFlow.Models.TodoTask
 @{ ViewData["Title"] = "Supprimer la tâche"; }
 
-<h1>Confirmer la suppression</h1>
-
-<div class="alert alert-danger">
-    Vous allez supprimer définitivement <strong>@Model.Titre</strong>.
-    <br/>Cette action est irréversible.
-</div>
-
-@* Récapitulatif de la tâche à supprimer *@
-
-<dl class="row mb-4">
-    <dt class="col-sm-3">Titre</dt>
-    <dd class="col-sm-9">@Model.Titre</dd>
-
-    <dt class="col-sm-3">Priorité</dt>
-    <dd class="col-sm-9">@Model.Priorite</dd>
-
-    <dt class="col-sm-3">Échéance</dt>
-    <dd class="col-sm-9">@Model.DateEcheance.ToShortDateString()</dd>
-</dl>
-
-@* Formulaire de confirmation *@
-<form asp-action="DeleteConfirmed" method="post">
-    @Html.AntiForgeryToken()
-
-    @* Champ caché Id - même principe que dans Edit.cshtml. *@
-    <input type="hidden" asp-for="Id"/>
-
-    <div class="d-flex gap-2">
-        <button type="submit" class="btn btn-danger">
-            Oui, supprimer définitivement
-        </button>
-        <a asp-action="Index" class="btn btn-outline-secondary">
-            Annuler
-        </a>
+@section TopbarActions {
+    <a asp-action="Index" class="btn btn-secondary">← Retour</a>
+}
+
+<div style="max-width:420px;">
+    <div class="confirm-modal" style="box-shadow:none;border:1px solid var(--border);">
+        <div class="confirm-icon">🗑️</div>
+        <div class="confirm-title">Supprimer cette tâche ?</div>
+        <div class="confirm-desc">
+            "<strong>@Model.Titre</strong>" sera définitivement supprimée.
+            Cette action est irréversible.
+        </div>
+
+        <!-- Récapitulatif -->
+        <div style="text-align:left;background:var(--bg);border-radius:6px;padding:12px 14px;margin-bottom:20px;font-size:13px;">
+            <div style="display:flex;justify-content:space-between;margin-bottom:6px;">
+                <span style="color:var(--muted);">Priorité</span>
+                <span class="badge badge-@Model.Priorite.ToString().ToLower()">@Model.Priorite</span>
+            </div>
+            <div style="display:flex;justify-content:space-between;">
+                <span style="color:var(--muted);">Échéance</span>
+                <span>@Model.DateEcheance.ToShortDateString()</span>
+            </div>
+        </div>
+
+        <div class="confirm-actions">
+            <a asp-action="Index" class="btn btn-secondary">Annuler</a>
+            <form asp-action="DeleteConfirmed" method="post" style="display:inline;">
+                @Html.AntiForgeryToken()
+                <input type="hidden" asp-for="Id" />
+                <button type="submit" class="btn btn-danger">Oui, supprimer</button>
+            </form>
+        </div>
     </div>
-
-</form>
-
-
+</div>
\ No newline at end of file
```

### `Views/Category/index.cshtml`

```diff
@@ -1,38 +1,46 @@
 @model List<TaskFlow.Models.Category>
+@{ ViewData["Title"] = "Catégories"; ViewData["Subtitle"] = "Gérez vos catégories de tâches"; }
 
-@{
-    ViewData["Title"] = "Categories";
+@section TopbarActions {
+    <a asp-action="Create" class="btn btn-primary">+ Nouvelle catégorie</a>
 }
 
-<div class="d-flex justify-content-between align-items-center mb-3">
-    <h1>Mes categories</h1>
-    <a asp-action="Create" class="btn btn-primary">+ Nouvelle categorie</a>
-</div>
-
 @if (!Model.Any())
 {
-    <div class="alert alert-info">
-        Aucune categorie. Creez votre premiere categorie !
+    <div class="empty-state">
+        <div class="empty-icon">🏷️</div>
+        <div class="empty-title">Aucune catégorie</div>
+        <p>Créez votre première catégorie pour organiser vos tâches.</p>
     </div>
 }
 else
 {
-    <div class="list-group">
+    <div class="task-table">
+        <div class="task-table-header" style="grid-template-columns:1fr 80px 120px;">
+            <span>Nom</span>
+            <span>Tâches</span>
+            <span></span>
+        </div>
         @foreach (var cat in Model)
         {
-            <div class="list-group-item d-flex justify-content-between align-items-center">
-                <div class="d-flex align-items-center gap-2">
-                    <span style="width:14px;height:14px;border-radius:50%;background:@cat.Color;display:inline-block"></span>
-                    <span class="fw-medium">@cat.Name</span>
-                    <span class="badge bg-secondary">@cat.Tasks.Count tache(s)</span>
+            <div class="task-row" style="grid-template-columns:1fr 80px 120px;">
+                <div style="display:flex;align-items:center;gap:10px;">
+                    <span style="width:14px;height:14px;border-radius:50%;
+                           background:@cat.Color;display:inline-block;flex-shrink:0;"></span>
+                    <span class="task-name">@cat.Name</span>
+                </div>
+                <div>
+                    <span class="badge" style="background:var(--bg);color:var(--muted);">
+                        @cat.Tasks.Count
+                    </span>
                 </div>
-                <div class="d-flex gap-2">
+                <div class="task-actions">
                     <a asp-action="Edit" asp-route-id="@cat.Id"
-                       class="btn btn-sm btn-outline-primary">Editer</a>
+                       class="btn btn-secondary btn-sm">✏️</a>
                     <a asp-action="Delete" asp-route-id="@cat.Id"
-                       class="btn btn-sm btn-outline-danger">Supprimer</a>
+                       class="btn btn-danger btn-sm">🗑</a>
                 </div>
             </div>
         }
     </div>
-}
+}
\ No newline at end of file
```

### `Views/Category/Create.cshtml`

```diff
@@ -1,34 +1,30 @@
 @model TaskFlow.Models.Category
+@{ ViewData["Title"] = "Nouvelle catégorie"; }
 
-    @{ 
-        @* ViewData[Title] : définit le titre de l'onglet du navigateur
-        cette valeur est utilisée dans le _Layout.cshtml.*@
-        ViewData["Title"] = "Nouvelle catégories";
-    }
+@section TopbarActions {
+    <a asp-action="Index" class="btn btn-secondary">← Retour</a>
+}
 
-    <div class="d-flex justify-content-between align-items-center mb-3">
-        <h1>Nouvelle catégorie</h1>
-        <a asp-action="Index" class="btn btn-outline-secondary">← Retour</a>
-    </div>
-
-    <div class="col-md-5">
+<div style="max-width:400px;">
+    <div class="profile-card">
         <form asp-action="Create" method="post">
             @Html.AntiForgeryToken()
-            <div asp-validation-summary="ModelOnly" class="alert alert-danger d-none"></div>
-            <div class="mb-3">
-                <label asp-for="Name" class="form-label"></label>
-                <input asp-for="Name" class="form-control" placeholder="Ex: Travail, perso..."/>
-                <span asp-validation-for="Name" class="text-danger small"></span>
+            <div class="form-group">
+                <label asp-for="Name"></label>
+                <input asp-for="Name" class="form-control" placeholder="Ex : Travail, Perso..." />
+                <span asp-validation-for="Name" class="form-error show"></span>
             </div>
-            <div class="mb-3">
-                <label asp-for="Color" class="form-label"></label>
-                <input asp-for="Color" type="color" class="form-control form-control-color"/>
-                <span asp-validation-for="Color" class="text-danger small"></span>
+            <div class="form-group">
+                <label asp-for="Color"></label>
+                <input asp-for="Color" type="color" class="form-control"
+                       style="height:42px;padding:4px 8px;cursor:pointer;" />
             </div>
-            <div class="mb-3">
+            <div style="display:flex;gap:8px;">
                 <button type="submit" class="btn btn-primary">Enregistrer</button>
-                <a asp-action="Index" class="btn btn-outline-secondary">Annuler</a>
+                <a asp-action="Index" class="btn btn-secondary">Annuler</a>
             </div>
         </form>
+    </div>
 </div>
-@section Scripts {@{await Html.RenderPartialAsync("_ValidationScriptsPartial");}}
+
+@section Scripts { @{await Html.RenderPartialAsync("_ValidationScriptsPartial");} }
\ No newline at end of file
```

### `Views/Category/Edit.cshtml`

```diff
@@ -1,35 +1,31 @@
 @model TaskFlow.Models.Category
+@{ ViewData["Title"] = "Modifier la catégorie"; ViewData["Subtitle"] = @Model.Name; }
 
-    @{ 
-        ViewData["Title"] = "Modifier catégories";
-    }
-    
-    <h1>Modifier catégorie</h1>
+@section TopbarActions {
+    <a asp-action="Index" class="btn btn-secondary">← Retour</a>
+}
 
-
-    <div class="col-md-5">
+<div style="max-width:400px;">
+    <div class="profile-card">
         <form asp-action="Edit" method="post">
             @Html.AntiForgeryToken()
-
-            @* Id caché pour que le contrôleur sache quelle cat modifier*@
-            <input type="hidden" asp-for="Id"/>
-
-            <div asp-validation-summary="ModelOnly" class="alert alert-danger d-none"></div>
-
-            <div class="mb-3">
-                <label asp-for="Name" class="form-label"></label>
-                <input asp-for="Name" class="form-control" placeholder="Ex: Travail, perso..."/>
-                <span asp-validation-for="Name" class="text-danger small"></span>
+            <input type="hidden" asp-for="Id" />
+            <div class="form-group">
+                <label asp-for="Name"></label>
+                <input asp-for="Name" class="form-control" />
+                <span asp-validation-for="Name" class="form-error show"></span>
             </div>
-            <div class="mb-3">
-                <label asp-for="Color" class="form-label"></label>
-                <input asp-for="Color" type="color" class="form-control form-control-color"/>
-                <span asp-validation-for="Color" class="text-danger small"></span>
+            <div class="form-group">
+                <label asp-for="Color"></label>
+                <input asp-for="Color" type="color" class="form-control"
+                       style="height:42px;padding:4px 8px;cursor:pointer;" />
             </div>
-            <div class="d-flex gap-2">
+            <div style="display:flex;gap:8px;">
                 <button type="submit" class="btn btn-primary">Enregistrer</button>
-                <a asp-action="Index" class="btn btn-outline-secondary">Annuler</a>
+                <a asp-action="Index" class="btn btn-secondary">Annuler</a>
             </div>
         </form>
+    </div>
 </div>
-@section Scripts {@{await Html.RenderPartialAsync("_ValidationScriptsPartial");}}
+
+@section Scripts { @{await Html.RenderPartialAsync("_ValidationScriptsPartial");} }
\ No newline at end of file
```

### `Views/Category/Delete.cshtml`

```diff
@@ -1,38 +1,25 @@
 @model TaskFlow.Models.Category
+@{ ViewData["Title"] = "Supprimer la catégorie"; }
 
-    @{ 
-        ViewData["Title"] = "Supprimer catégories";
-    }
-    
-    <h1>Supprimer catégorie</h1>
+@section TopbarActions {
+    <a asp-action="Index" class="btn btn-secondary">← Retour</a>
+}
 
-    <div class="alert alert-warning" role="alert">
-        Êtes vous sûre de vouloir supprimer la catégorie ?
-        Cette action est irréversible 
-    </div>
-
-    <dl class="row">
-        <dt class="col-sm-4">Nom</dt>
-        <dd class="col-sm-8">@Model.Name</dd>
-
-        <dt class="col-sm-4">Couleur</dt>
-        <dd class="col-sm-8">
-            <span style="display:inline-block; width:24px; height:24px;
-                        background-color:@Model.Color; border-radius:4px;
-                        border:1px solid #ccc;">
-            </span>
-            @Model.Color
-        </dd>
-    </dl>
-        @* Formulaire de confirmation - action="Delete" + méthode POST*@
-        <form asp-action="DeleteConfirmed" method="post">
-            @Html.AntiForgeryToken()
-            @* Id caché pour que le contrôleur sache quelle cat modifier*@
-            <input type="hidden" asp-for="Id"/>
-
-            <div class="d-flex gap-2">
+<div style="max-width:400px;">
+    <div class="confirm-modal" style="box-shadow:none;border:1px solid var(--border);">
+        <div class="confirm-icon">🏷️</div>
+        <div class="confirm-title">Supprimer cette catégorie ?</div>
+        <div class="confirm-desc">
+            La catégorie <strong>@Model.Name</strong> sera supprimée.
+            Les @Model.Tasks.Count tâche(s) liée(s) n'auront plus de catégorie.
+        </div>
+        <div class="confirm-actions">
+            <a asp-action="Index" class="btn btn-secondary">Annuler</a>
+            <form asp-action="DeleteConfirmed" method="post" style="display:inline;">
+                @Html.AntiForgeryToken()
+                <input type="hidden" asp-for="Id" />
                 <button type="submit" class="btn btn-danger">Oui, supprimer</button>
-                <a asp-action="Index" class="btn btn-outline-secondary">Annuler</a>
-            </div>
-        </form>
-
+            </form>
+        </div>
+    </div>
+</div>
\ No newline at end of file
```

### `wwwroot/css/site.css`

```diff
@@ -1,22 +1,705 @@
-html {
+*,
+*::before,
+*::after {
+  box-sizing: border-box;
+  margin: 0;
+  padding: 0;
+}
+:root {
+  --bg: #f4f4f2;
+  --surface: #ffffff;
+  --border: #e8e8e6;
+  --border2: #d4d4d0;
+  --text: #1a1a1a;
+  --muted: #6b6b6b;
+  --muted2: #9a9a9a;
+  --accent: #3b5bdb;
+  --accent-h: #2f4ac4;
+  --accent-bg: #eef2ff;
+  --green: #16a34a;
+  --green-bg: #f0fdf4;
+  --red: #dc2626;
+  --red-bg: #fef2f2;
+  --orange: #ea580c;
+  --orange-bg: #fff7ed;
+  --radius: 8px;
+  --shadow: 0 1px 3px rgba(0, 0, 0, 0.08);
+}
+html,
+body {
+  height: 100%;
+}
+body {
+  font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif;
+  background: var(--bg);
+  color: var(--text);
   font-size: 14px;
+  line-height: 1.5;
+}
+button {
+  font-family: inherit;
+  cursor: pointer;
+}
+input,
+select,
+textarea {
+  font-family: inherit;
+  font-size: 14px;
+}
+a {
+  color: var(--accent);
+  text-decoration: none;
 }
 
-@media (min-width: 768px) {
-  html {
-    font-size: 16px;
-  }
+/* ── LOGIN ── */
+.login-page {
+  min-height: 100vh;
+  display: flex;
+  align-items: center;
+  justify-content: center;
+}
+.login-card {
+  background: var(--surface);
+  border: 1px solid var(--border);
+  border-radius: 12px;
+  padding: 40px;
+  width: 380px;
+  box-shadow: 0 4px 24px rgba(0, 0, 0, 0.08);
+}
+.login-logo {
+  display: flex;
+  align-items: center;
+  gap: 10px;
+  margin-bottom: 28px;
 }
 
-.btn:focus, .btn:active:focus, .btn-link.nav-link:focus, .form-control:focus, .form-check-input:focus {
-  box-shadow: 0 0 0 0.1rem white, 0 0 0 0.25rem #258cfb;
+/* ── APP SHELL ── */
+.app-shell {
+  display: flex;
+  height: 100vh;
+  overflow: hidden;
 }
 
-html {
-  position: relative;
-  min-height: 100%;
+/* ── SIDEBAR ── */
+.sidebar {
+  width: 220px;
+  min-width: 220px;
+  background: var(--surface);
+  border-right: 1px solid var(--border);
+  display: flex;
+  flex-direction: column;
+  height: 100vh;
+  overflow-y: auto;
+}
+.sidebar-brand {
+  padding: 16px;
+  border-bottom: 1px solid var(--border);
+  display: flex;
+  align-items: center;
+  gap: 10px;
+}
+.logo-mark {
+  width: 32px;
+  height: 32px;
+  background: var(--accent);
+  border-radius: 7px;
+  display: flex;
+  align-items: center;
+  justify-content: center;
+  color: white;
+  font-weight: 700;
+  font-size: 14px;
+  flex-shrink: 0;
+}
+.brand-text {
+  font-size: 15px;
+  font-weight: 700;
+}
+.brand-sub {
+  font-size: 11px;
+  color: var(--muted2);
+}
+.sidebar-nav {
+  flex: 1;
+  padding: 8px;
+}
+.nav-group {
+  margin-bottom: 20px;
+}
+.nav-group-label {
+  font-size: 10px;
+  font-weight: 700;
+  text-transform: uppercase;
+  letter-spacing: 0.06em;
+  color: var(--muted2);
+  padding: 4px 8px 6px;
+}
+.nav-item {
+  display: flex;
+  align-items: center;
+  gap: 9px;
+  padding: 7px 10px;
+  border-radius: 6px;
+  font-size: 13px;
+  color: var(--muted);
+  text-decoration: none;
+  border: none;
+  background: none;
+  width: 100%;
+  text-align: left;
+  transition: all 0.12s;
+}
+.nav-item:hover {
+  background: var(--bg);
+  color: var(--text);
+}
+.nav-item.active {
+  background: var(--accent-bg);
+  color: var(--accent);
+  font-weight: 600;
+}
+.ni {
+  width: 16px;
+  text-align: center;
+  font-size: 14px;
+  flex-shrink: 0;
+}
+.sidebar-user {
+  padding: 12px 14px;
+  border-top: 1px solid var(--border);
+  display: flex;
+  align-items: center;
+  gap: 10px;
+}
+.user-avatar {
+  width: 32px;
+  height: 32px;
+  border-radius: 50%;
+  background: var(--accent-bg);
+  color: var(--accent);
+  display: flex;
+  align-items: center;
+  justify-content: center;
+  font-size: 12px;
+  font-weight: 700;
+  flex-shrink: 0;
+}
+.user-name {
+  font-size: 13px;
+  font-weight: 500;
+}
+.user-role {
+  font-size: 11px;
+  color: var(--muted2);
+}
+.logout-btn {
+  margin-left: auto;
+  background: none;
+  border: none;
+  color: var(--muted2);
+  cursor: pointer;
+  font-size: 16px;
+  padding: 4px;
+}
+.logout-btn:hover {
+  color: var(--red);
 }
 
-body {
-  margin-bottom: 60px;
-}
\ No newline at end of file
+/* ── MAIN ── */
+.main {
+  flex: 1;
+  overflow-y: auto;
+  display: flex;
+  flex-direction: column;
+}
+.topbar {
+  background: var(--surface);
+  border-bottom: 1px solid var(--border);
+  padding: 14px 24px;
+  display: flex;
+  align-items: center;
+  gap: 16px;
+  position: sticky;
+  top: 0;
+  z-index: 10;
+}
+.topbar-title {
+  font-size: 16px;
+  font-weight: 700;
+}
+.topbar-sub {
+  font-size: 12px;
+  color: var(--muted);
+}
+.topbar-actions {
+  margin-left: auto;
+  display: flex;
+  gap: 8px;
+}
+.content {
+  padding: 24px;
+  flex: 1;
+}
+
+/* ── BOUTONS ── */
+.btn {
+  display: inline-flex;
+  align-items: center;
+  gap: 6px;
+  padding: 9px 16px;
+  border-radius: 6px;
+  font-size: 13px;
+  font-weight: 500;
+  border: none;
+  transition: all 0.15s;
+  cursor: pointer;
+  text-decoration: none;
+}
+.btn-primary {
+  background: var(--accent);
+  color: white;
+}
+.btn-primary:hover {
+  background: var(--accent-h);
+  color: white;
+}
+.btn-secondary {
+  background: var(--bg);
+  color: var(--text);
+  border: 1px solid var(--border2);
+}
+.btn-secondary:hover {
+  background: var(--border);
+}
+.btn-danger {
+  background: var(--red-bg);
+  color: var(--red);
+  border: 1px solid #fecaca;
+}
+.btn-danger:hover {
+  background: #fee2e2;
+}
+.btn-sm {
+  padding: 5px 10px;
+  font-size: 12px;
+}
+.btn-block {
+  width: 100%;
+  justify-content: center;
+}
+.btn-icon {
+  width: 30px;
+  height: 30px;
+  padding: 0;
+  justify-content: center;
+}
+
+/* ── STATS ── */
+.stats-grid {
+  display: grid;
+  grid-template-columns: repeat(4, 1fr);
+  gap: 14px;
+  margin-bottom: 24px;
+}
+.stat-card {
+  background: var(--surface);
+  border: 1px solid var(--border);
+  border-radius: var(--radius);
+  padding: 16px 18px;
+}
+.stat-label {
+  font-size: 12px;
+  color: var(--muted);
+  margin-bottom: 6px;
+}
+.stat-value {
+  font-size: 28px;
+  font-weight: 700;
+  line-height: 1;
+}
+.stat-sub {
+  font-size: 11px;
+  color: var(--muted2);
+  margin-top: 4px;
+}
+.stat-card.blue .stat-value {
+  color: var(--accent);
+}
+.stat-card.green .stat-value {
+  color: var(--green);
+}
+.stat-card.orange .stat-value {
+  color: var(--orange);
+}
+.stat-card.red .stat-value {
+  color: var(--red);
+}
+
+/* ── FILTRES ── */
+.filters-row {
+  display: flex;
+  align-items: center;
+  gap: 10px;
+  margin-bottom: 16px;
+  flex-wrap: wrap;
+}
+.filter-btn {
+  padding: 5px 14px;
+  border-radius: 20px;
+  font-size: 12px;
+  font-weight: 500;
+  border: 1px solid var(--border2);
+  background: var(--surface);
+  color: var(--muted);
+  cursor: pointer;
+  transition: all 0.12s;
+  text-decoration: none;
+}
+.filter-btn:hover {
+  background: var(--bg);
+  color: var(--text);
+}
+.filter-btn.active {
+  background: var(--accent);
+  color: white;
+  border-color: var(--accent);
+}
+.search-input {
+  padding: 6px 12px;
+  border: 1px solid var(--border2);
+  border-radius: 6px;
+  font-size: 13px;
+  width: 200px;
+}
+.search-input:focus {
+  outline: none;
+  border-color: var(--accent);
+}
+.filters-right {
+  margin-left: auto;
+  display: flex;
+  gap: 8px;
+}
+.sort-select {
+  padding: 5px 10px;
+  border: 1px solid var(--border2);
+  border-radius: 6px;
+  font-size: 12px;
+  background: var(--surface);
+  color: var(--text);
+}
+
+/* ── TASK TABLE ── */
+.task-table {
+  background: var(--surface);
+  border: 1px solid var(--border);
+  border-radius: var(--radius);
+  overflow: hidden;
+}
+.task-table-header {
+  display: grid;
+  grid-template-columns: 2fr 100px 110px 120px 80px;
+  gap: 12px;
+  padding: 10px 16px;
+  border-bottom: 1px solid var(--border);
+  background: var(--bg);
+}
+.task-table-header span {
+  font-size: 11px;
+  font-weight: 700;
+  text-transform: uppercase;
+  letter-spacing: 0.04em;
+  color: var(--muted);
+}
+.task-row {
+  display: grid;
+  grid-template-columns: 2fr 100px 110px 120px 80px;
+  gap: 12px;
+  padding: 12px 16px;
+  border-bottom: 1px solid var(--border);
+  align-items: center;
+  transition: background 0.1s;
+}
+.task-row:last-child {
+  border-bottom: none;
+}
+.task-row:hover {
+  background: #fafafa;
+}
+.task-row.done .task-title-cell {
+  opacity: 0.5;
+  text-decoration: line-through;
+}
+.task-title-cell {
+  display: flex;
+  align-items: flex-start;
+  gap: 10px;
+}
+.task-checkbox {
+  width: 16px;
+  height: 16px;
+  border: 1.5px solid var(--border2);
+  border-radius: 4px;
+  flex-shrink: 0;
+  margin-top: 2px;
+  cursor: pointer;
+  display: flex;
+  align-items: center;
+  justify-content: center;
+  transition: all 0.12s;
+  background: white;
+}
+.task-checkbox.checked {
+  background: var(--green);
+  border-color: var(--green);
+}
+.task-checkbox.checked::after {
+  content: "✓";
+  color: white;
+  font-size: 10px;
+  font-weight: 700;
+}
+.task-name {
+  font-size: 13px;
+  font-weight: 500;
+  color: var(--text);
+}
+.task-desc {
+  font-size: 11px;
+  color: var(--muted);
+  margin-top: 2px;
+  white-space: nowrap;
+  overflow: hidden;
+  text-overflow: ellipsis;
+  max-width: 280px;
+}
+.task-actions {
+  display: flex;
+  gap: 4px;
+  justify-content: flex-end;
+}
+.date-cell {
+  font-size: 12px;
+  color: var(--muted);
+}
+.date-cell.retard {
+  color: var(--red);
+  font-weight: 500;
+}
+
+/* ── BADGES ── */
+.badge {
+  display: inline-flex;
+  align-items: center;
+  padding: 3px 9px;
+  border-radius: 12px;
+  font-size: 11px;
+  font-weight: 600;
+}
+.badge-haute {
+  background: var(--red-bg);
+  color: var(--red);
+}
+.badge-moyenne {
+  background: var(--orange-bg);
+  color: var(--orange);
+}
+.badge-basse {
+  background: var(--green-bg);
+  color: var(--green);
+}
+.badge-encours {
+  background: var(--accent-bg);
+  color: var(--accent);
+}
+.badge-terminee {
+  background: var(--green-bg);
+  color: var(--green);
+}
+.badge-retard {
+  background: var(--red-bg);
+  color: var(--red);
+}
+
+/* ── EMPTY STATE ── */
+.empty-state {
+  padding: 48px;
+  text-align: center;
+  color: var(--muted);
+}
+.empty-icon {
+  font-size: 36px;
+  margin-bottom: 12px;
+}
+.empty-title {
+  font-size: 15px;
+  font-weight: 600;
+  color: var(--text);
+  margin-bottom: 6px;
+}
+
+/* ── FORMS ── */
+.form-group {
+  margin-bottom: 14px;
+}
+.form-group:last-child {
+  margin-bottom: 0;
+}
+.form-group label {
+  display: block;
+  font-size: 12px;
+  font-weight: 600;
+  color: var(--muted);
+  text-transform: uppercase;
+  letter-spacing: 0.04em;
+  margin-bottom: 5px;
+}
+.form-control {
+  width: 100%;
+  padding: 9px 12px;
+  border: 1px solid var(--border2);
+  border-radius: 6px;
+  font-size: 14px;
+  color: var(--text);
+  background: var(--surface);
+  transition: border-color 0.15s;
+}
+.form-control:focus {
+  outline: none;
+  border-color: var(--accent);
+  box-shadow: 0 0 0 3px rgba(59, 91, 219, 0.12);
+}
+textarea.form-control {
+  resize: vertical;
+  min-height: 72px;
+}
+.form-row {
+  display: grid;
+  grid-template-columns: 1fr 1fr;
+  gap: 14px;
+}
+.form-error {
+  font-size: 11px;
+  color: var(--red);
+  margin-top: 3px;
+  display: none;
+}
+.form-error.show {
+  display: block;
+}
+
+/* ── PROFILE CARD ── */
+.profile-card {
+  background: var(--surface);
+  border: 1px solid var(--border);
+  border-radius: var(--radius);
+  padding: 24px;
+}
+.profile-avatar-lg {
+  width: 64px;
+  height: 64px;
+  border-radius: 50%;
+  background: var(--accent-bg);
+  color: var(--accent);
+  display: flex;
+  align-items: center;
+  justify-content: center;
+  font-size: 24px;
+  font-weight: 700;
+  margin-bottom: 16px;
+}
+
+/* ── CONFIRM MODAL ── */
+.confirm-modal {
+  background: var(--surface);
+  border-radius: 10px;
+  padding: 24px;
+  text-align: center;
+}
+.confirm-icon {
+  font-size: 32px;
+  margin-bottom: 12px;
+}
+.confirm-title {
+  font-size: 16px;
+  font-weight: 700;
+  margin-bottom: 8px;
+}
+.confirm-desc {
+  font-size: 13px;
+  color: var(--muted);
+  margin-bottom: 20px;
+  line-height: 1.6;
+}
+.confirm-actions {
+  display: flex;
+  gap: 10px;
+  justify-content: center;
+}
+
+/* ── SECTION HEADER ── */
+.section-header {
+  display: flex;
+  align-items: center;
+  justify-content: space-between;
+  margin-bottom: 12px;
+}
+.section-header h3 {
+  font-size: 14px;
+  font-weight: 700;
+}
+
+/* ── TOAST ── */
+.toast-container {
+  position: fixed;
+  bottom: 24px;
+  right: 24px;
+  z-index: 200;
+  display: flex;
+  flex-direction: column;
+  gap: 8px;
+}
+.toast {
+  background: #1a1a1a;
+  color: white;
+  padding: 12px 18px;
+  border-radius: 8px;
+  font-size: 13px;
+  display: flex;
+  align-items: center;
+  gap: 10px;
+  box-shadow: var(--shadow);
+  max-width: 320px;
+}
+.toast.success {
+  background: var(--green);
+}
+.toast.error {
+  background: var(--red);
+}
+
+/* scrollbar */
+::-webkit-scrollbar {
+  width: 6px;
+}
+::-webkit-scrollbar-thumb {
+  background: var(--border2);
+  border-radius: 3px;
+}
+
+@media (max-width: 768px) {
+  .stats-grid {
+    grid-template-columns: 1fr 1fr;
+  }
+  .task-table-header {
+    display: none;
+  }
+  .task-row {
+    grid-template-columns: 1fr;
+    gap: 6px;
+  }
+  .sidebar {
+    width: 180px;
+    min-width: 180px;
+  }
+}
```

