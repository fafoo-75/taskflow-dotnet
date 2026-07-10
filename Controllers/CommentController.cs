using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TaskFlow.Data;
using TaskFlow.Models;

namespace TaskFlow.Controllers
{
    [Authorize]
    public class CommentController : Controller
    {
        private readonly AppDbContext _context;
        private readonly UserManager<IdentityUser> _userManager;

        public CommentController(AppDbContext context, UserManager<IdentityUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        // ADD COMMENT POST
        // POST /Comment/Add

        [HttpPost]
        [ValidateAntiForgeryToken]

        public async Task<IActionResult> Add(Comment comment)
        {
            // On vérifie que la tâche ciblée existe et appartient à l'user.
            // On laisse un user commenter une tâche qui lui appartient pas
            var userId = _userManager.GetUserId(User);
            var task = await _context.TodoTasks
                .FirstOrDefaultAsync(t => t.Id == comment.TaskId
                    && t.UserId == userId);
            
            if (task == null) return NotFound();

            // Si la validation échoue (comm vide),on redirige 
            // vers la page de la tâche avec un message d'erreur.
            if (!ModelState.IsValid)
            {
                TempData["Erreur"] = "Le commentaire ne peut être vide";
                return RedirectToAction("Details", "Todo",
                new { id = comment.TaskId });
            }

            // Remplir les champs calculés côté serveur.
            comment.UserId = userId;
            comment.UserName = _userManager.GetUserName(User);
            comment.CreatedAt = DateTime.Now;

            _context.Comments.Add(comment);
            await _context.SaveChangesAsync();

            // On redirige vers la page Details de la tâche après ajout.
            return RedirectToAction("Details" , "Todo",
                new { id = comment.TaskId});
        }

        // DELETE COMMENT POST
        // Supprime un commentaire - uniquement si l'auteur est l'user connecté.

        [HttpPost]
        [ValidateAntiForgeryToken]

        public async Task<IActionResult> Delete(int id)
        {
            var userId = _userManager.GetUserId(User);

            var comment = await _context.Comments
                .FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId);

                if (comment == null) return NotFound();

                var TaskId = comment.TaskId;
                _context.Comments.Remove(comment);
                await _context.SaveChangesAsync();

                return RedirectToAction("Details", "Todo",
                    new { id = TaskId});
        }
    }
}