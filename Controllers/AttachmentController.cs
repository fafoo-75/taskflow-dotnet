using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TaskFlow.Data;
using TaskFlow.Models;

namespace TaskFlow.Controllers
{
    [Authorize]
    public class AttachmentController : Controller
    {
        private readonly AppDbContext _context;
        private readonly UserManager<IdentityUser> _userManager;

        // IWebHostEnvironment donne accès au chemin physique
        // du dossier wwwroot sur le disque.
        private readonly IWebHostEnvironment _env;

        public AttachmentController(AppDbContext context,
                                    UserManager<IdentityUser> userManager,
                                    IWebHostEnvironment env)
        {
            _context     = context;
            _userManager = userManager;
            _env         = env;
        }

        // ── UPLOAD POST ───────────────────────────────────────────
        // POST /Attachment/Upload
        // Le paramètre taskId doit correspondre exactement au name="taskId"
        // du champ caché dans le formulaire HTML.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Upload(IFormFile file, int taskId)
        {
            var userId = _userManager.GetUserId(User);

            // Vérifier que la tâche appartient à l'utilisateur connecté.
            var task = await _context.TodoTasks
                .FirstOrDefaultAsync(t => t.Id == taskId && t.UserId == userId);

            if (task == null) return NotFound();

            // Vérifier qu'un fichier a bien été sélectionné.
            if (file == null || file.Length == 0)
            {
                TempData["Erreur"] = "Veuillez choisir un fichier.";
                return RedirectToAction("Details", "Todo", new { id = taskId });
            }

            // Limiter la taille à 10 Mo.
            if (file.Length > 10 * 1024 * 1024)
            {
                TempData["Erreur"] = "Le fichier est trop volumineux. Maximum 10 Mo.";
                return RedirectToAction("Details", "Todo", new { id = taskId });
            }

            // Vérifier l'extension autorisée.
            var allowedExtensions = new[] { ".pdf", ".doc", ".docx",
                                            ".xls", ".xlsx", ".png",
                                            ".jpg", ".jpeg", ".txt" };
            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();

            if (!allowedExtensions.Contains(extension))
            {
                TempData["Erreur"] = "Type de fichier non autorisé.";
                return RedirectToAction("Details", "Todo", new { id = taskId });
            }

            // Générer un nom unique avec GUID pour éviter les conflits.
            var storedName = $"{Guid.NewGuid()}{extension}";

            // _env.WebRootPath = chemin absolu vers wwwroot.
            // Path.Combine gère le séparateur selon l'OS (Mac, Windows, Linux).
            var uploadFolder = Path.Combine(_env.WebRootPath, "uploads");
            var filePath     = Path.Combine(uploadFolder, storedName);

            // Créer le dossier s'il n'existe pas.
            Directory.CreateDirectory(uploadFolder);

            // Écrire le fichier sur le disque.
            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            // Enregistrer les métadonnées en base SQLite.
            var attachment = new Attachment
            {
                OriginalName = file.FileName,
                StoredName   = storedName,
                ContentType  = file.ContentType,
                Size         = file.Length,
                UploadAt     = DateTime.Now,
                TaskId       = taskId,
                UserId       = userId
            };

            _context.Attachments.Add(attachment);
            await _context.SaveChangesAsync();

            TempData["Succes"] = "Le fichier a bien été envoyé.";
            return RedirectToAction("Details", "Todo", new { id = taskId });
        }

        // ── DOWNLOAD ──────────────────────────────────────────────
        // GET /Attachment/Download/5
        // Envoie le fichier au navigateur pour téléchargement.
        public async Task<IActionResult> Download(int id)
        {
            var userId = _userManager.GetUserId(User);

            var attachment = await _context.Attachments
                .Include(a => a.Task)
                .FirstOrDefaultAsync(a => a.Id == id
                                       && a.Task!.UserId == userId);

            if (attachment == null) return NotFound();

            // CORRECTION : "uploads" avec s — dossier correct dans wwwroot
            var filePath = Path.Combine(_env.WebRootPath, "uploads", attachment.StoredName);

            if (!System.IO.File.Exists(filePath)) return NotFound();

            // PhysicalFile envoie le fichier au navigateur.
            // ContentType = type MIME (le navigateur sait comment l'ouvrir).
            // OriginalName = nom affiché lors du téléchargement.
            return PhysicalFile(filePath, attachment.ContentType,
                                attachment.OriginalName);
        }

        // ── DELETE POST ───────────────────────────────────────────
        // POST /Attachment/Delete/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var userId = _userManager.GetUserId(User);

            var attachment = await _context.Attachments
                .Include(a => a.Task)
                .FirstOrDefaultAsync(a => a.Id == id
                                       && a.Task!.UserId == userId);

            if (attachment == null) return NotFound();

            var taskId   = attachment.TaskId;

            // CORRECTION : "uploads" avec s — dossier correct dans wwwroot
            var filePath = Path.Combine(_env.WebRootPath, "uploads", attachment.StoredName);

            // Supprimer le fichier physique sur le disque.
            if (System.IO.File.Exists(filePath))
                System.IO.File.Delete(filePath);

            // Supprimer l'entrée en base.
            _context.Attachments.Remove(attachment);
            await _context.SaveChangesAsync();

            return RedirectToAction("Details", "Todo", new { id = taskId });
        }
    }
}