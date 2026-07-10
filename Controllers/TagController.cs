using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TaskFlow.Data;
using TaskFlow.Models;

namespace TaskFlow.Controllers
{
    [Authorize]

    public class TagController : Controller
    {
        private readonly AppDbContext _context;
        private readonly UserManager<IdentityUser> _userManager;

        public TagController(AppDbContext context,
                              UserManager<IdentityUser> userManager)
        {
            _context     = context;
            _userManager = userManager;
        }

        // Index
        //Get /Tag
        // Affiche les tags de l'utilisateur connecté.
        public async Task<IActionResult> Index()
        {
            var userId = _userManager.GetUserId(User);

            var tags = await _context.Tags
                .Where(t => t.UserId == userId)
                .AsNoTracking()
                .OrderBy(t => t.Name)
                .ToListAsync();
            return View(tags);
        }

        // CREATE GET
        public IActionResult Create()
        {
            return View();
        }

        // CREATE POST
        [HttpPost]
        [ValidateAntiForgeryToken]

        public async Task<IActionResult> Create(Tag tag)
        {
            if (!ModelState.IsValid)
                return View(tag);

            tag.UserId = _userManager.GetUserId(User);

            _context.Add(tag);
            await _context.SaveChangesAsync();

            TempData["Succes"] = "Le tag a été créé.";
            return RedirectToAction(nameof(Index));
        }

        // EDIT GET
        public async Task<IActionResult> Edit(int id)
        {
            var userId = _userManager.GetUserId(User);

            var tag = await _context.Tags
                .FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId);

            if (tag == null) return NotFound();
            return View(tag);
        }

        // EDIT POST
        [HttpPost]
        [ValidateAntiForgeryToken]

        public async Task<IActionResult> Edit(int id, Tag tagModified)
        {
            if (id != tagModified.Id) return BadRequest();
            if (!ModelState.IsValid) return View(tagModified);

            var userId = _userManager.GetUserId(User);

            var tag = await _context.Tags
                .FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId);

            if (tag == null) return NotFound();

            tag.Name = tagModified.Name;
            tag.Color = tagModified.Color;

            await _context.SaveChangesAsync();

            TempData["Succes"] = "Le tag a été mis à jour.";
            return RedirectToAction(nameof(Index));
        }

        // DELETE  GET
        public async Task<IActionResult> Delete(int id)
        {
            var userId = _userManager.GetUserId(User);

            var tag = await _context.Tags
                .FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId);

            if (tag == null) return NotFound();
            return View(tag);
        }

        // DELETE POST

        [HttpPost]
        [ValidateAntiForgeryToken]

        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var userId = _userManager.GetUserId(User);

            var tag = await _context.Tags
                .FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId);

            if (tag != null)
            {
                _context.Tags.Remove(tag);
                await _context.SaveChangesAsync();
            }

            TempData["Succes"] = "Le tag a bien été supprimé";
            return RedirectToAction(nameof(Index));
        }
    }
}
