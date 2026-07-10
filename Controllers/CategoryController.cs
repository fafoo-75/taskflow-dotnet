using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TaskFlow.Data;
using TaskFlow.Models;

namespace TaskFlow.Controllers
{
    [Authorize]

    public class CategoryController : Controller
    {
        private readonly AppDbContext _context;

        public CategoryController (AppDbContext context)
        {
            _context = context;
        }

        // Index
        //Get /Category
        // Affiche toutes les catégories avec le nombre de tâche associées.
        public async Task<IActionResult> Index()
        {
            var categories = await _context.Categories
                .Include(c => c.Tasks)
                .AsNoTracking()
                .OrderBy(c => c.Name)
                .ToListAsync();
            return View(categories);
        }

        // CREATE GET
        public IActionResult Create()
        {
            return View();
        }

        // CREATE POST
        [HttpPost]
        [ValidateAntiForgeryToken]

        public async Task<IActionResult> Create(Category category)
        {
            if (!ModelState.IsValid)
                return View(category);
            
            _context.Add(category);
            await _context.SaveChangesAsync();

            TempData["Succes"] = "La catégorie a été créée.";
            return RedirectToAction(nameof(Index));
        }

        // EDIT GET
        public async Task<IActionResult> Edit(int id)
        {
            var category = await _context.Categories
                .FirstOrDefaultAsync(c => c.Id == id);

            if (category == null) return NotFound();
            return View(category);
        }

        // EDIT POST
        [HttpPost]
        [ValidateAntiForgeryToken]

        public async Task<IActionResult> Edit(int id, Category categoryModified)
        {
            if (id !=categoryModified.Id) return BadRequest();
            if (!ModelState.IsValid) return View(categoryModified);

            var category = await _context.Categories
                .FirstOrDefaultAsync(c => c.Id == id);

            if (category == null) return NotFound();

            category.Name = categoryModified.Name;
            category.Color = categoryModified.Color;

            await _context.SaveChangesAsync();

            TempData["Succes"] = "La catégorie a été mise à jour.";
            return RedirectToAction(nameof(Index));
        }

        // DELETE  GET
        public async Task<IActionResult> Delete(int id)
        {
            var category = await _context.Categories
                .Include(c => c.Tasks)
                .FirstOrDefaultAsync(c => c.Id == id);

            if (category == null) return NotFound();
            return View(category);
        }

        // DELETE POST

        [HttpPost]
        [ValidateAntiForgeryToken]

        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var category = await _context.Categories
                .FirstOrDefaultAsync(c => c.Id == id);

            if (category != null)
            {
                _context.Categories.Remove(category);
                await _context.SaveChangesAsync();
            }

            TempData["Succes"] = "La catégorie a bien été supprimée";
            return RedirectToAction(nameof(Index));
        }
    }
}