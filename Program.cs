using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TaskFlow.Data;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Scalar.AspNetCore;
using Microsoft.OpenApi;





var builder = WebApplication.CreateBuilder(args);

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

// Ajouter les services au conteneur.
builder.Services.AddControllersWithViews();

// AddDefaultIdentity pour enregistrer les services Identity
// UserManager : Créer/modifier/supprimer des users
// SignInManager : Connecter/déconnecter user
// PasswordHasher : hacher les mots de passe (jamais en clair)

builder.Services
    .AddDefaultIdentity<IdentityUser>(options =>
    {
        // Règles du mot de passe - à adapter selon les besoins
        options.Password.RequireDigit = true; // doit avoir un chiffre
        options.Password.RequiredLength = 8; // minimum de caractères
        options.Password.RequireUppercase = false; // majuscule non obligatoire (false)
        options.Password.RequireNonAlphanumeric = false; // caractère spécial non obligatoire
        options.SignIn.RequireConfirmedAccount = false; // désactiver la confirmation email (en mode dev pas besoin)
    })
    .AddRoles<IdentityRole>() // AddRoles : gestion des rôles (Admin, User, ....)
    // AddEntityFrameworkStores : dit à Identity de stocker
    // les utilisateurs dans notre AppDbContext (SQLite)
    .AddEntityFrameworkStores<AppDbContext>();

builder.Services
    .AddAuthentication()
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
             IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(
                    builder.Configuration["Jwt:SecretKey"] ?? 
                    "TaskFlow-Super-clé-secrete-dev-only"
                )
             ),
             ValidateIssuer = false, 
             ValidateAudience = false
        };
    });

builder.Services.AddEndpointsApiExplorer();

builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, context, ct) =>
    {
        document.Components ??= new OpenApiComponents();
        // Microsoft.OpenApi 2.x n'initialise plus automatiquement le dictionnaire
        // SecuritySchemes : il faut donc le créer nous-mêmes avant d'y ajouter une entrée,
        // sinon .Add(...) lève une NullReferenceException et le document OpenAPI ne se génère pas
        // (résultat : la page Scalar reste vide).
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes.Add("Bearer", new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "Coller le token ici"
        });

        // Tâche déjà terminée.
         
        return Task.CompletedTask;
    });
});

var app = builder.Build();

if (app.Environment.IsProduction())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

if (!app.Environment.IsDevelopment())
    app.UseHttpsRedirection();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}


// Configurer le pipeline de requêtes HTTP.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // La valeur HSTS par défaut est de 30 jours. Vous pouvez la modifier pour les scénarios de production, voir https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();
// UseAuthentication = doit ête avant UseAuthorization
// Authentication = "Qui est tu ?" (lit le cookie, identifie l'utilisateur)
// Authorization = "as-tu-le droit ? (verifie [Authorize])
// Important : Si les 2 lignes sont inversées, lo login ne marche pas.

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

// MapRazorPages : est necessaire pour les pages Identity (login, register..)
// qui sont des Razor Pages et non des contrôleurs MVC classiques.
app.MapRazorPages();

app.Run();

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
