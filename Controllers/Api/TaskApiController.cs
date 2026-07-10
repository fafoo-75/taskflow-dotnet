using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TaskFlow;
using TaskFlow.Data;
using TaskFlow.Models;

namespace TaskFlow.Controllers.Api
{
    [ApiController]

    [Route("api/[controller]")]

    [Authorize]

    public class TaskApiController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly UserManager<IdentityUser> _userManager;

        public TaskApiController(AppDbContext context, 
                                    UserManager<IdentityUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        // GET /api/task
        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var userId = _userManager.GetUserId(User);

            var tasks = await _context.TodoTasks
                .Where(t => t.UserId == userId)
                .Include(tasks => tasks.Category)
                .AsNoTracking()
                .OrderBy(t => t.EstTerminee)
                .ThenBy(t => t.DateEcheance)
                .Select(tasks => new
                {
                    tasks.Id,
                    tasks.Titre,
                    tasks.Description,
                    tasks.Priorite,
                    tasks.EstTerminee,
                    tasks.DateEcheance,
                    tasks.CategoryId,
                    CategoryName = tasks.Category !=null ? tasks.Category.Name : null,
                    CategoryColor = tasks.Category !=null ? tasks.Category.Color : null                
                    })

                    .ToListAsync();

            return Ok(tasks);
        
        }
        // GET /api/task/4

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(int id)
        {
            var userId = _userManager.GetUserId(User);

            var task = await _context.TodoTasks
                .Include(t => t.Category)
                .AsNoTracking()
                .Where(t => t.Id == id && t.UserId == userId)
                .Select(tasks => new
                {
                    tasks.Id,
                    tasks.Titre,
                    tasks.Description,
                    tasks.Priorite,
                    tasks.EstTerminee,
                    tasks.DateEcheance,
                    tasks.CategoryId,
                    CategoryName = tasks.Category !=null ? tasks.Category.Name : null
                    })
                    .FirstOrDefaultAsync();
            if (task == null)
                
                return NotFound(new { message = "Tâche non trouvée."});

            return Ok(task);
        }

        // POST
        [HttpPost]
        public async Task<IActionResult> Create(TodoTask task)
        {
            task.UserId = _userManager.GetUserId(User);

            _context.Add(task);
            await _context.SaveChangesAsync();

            return CreatedAtAction(
                nameof(GetById),
                new { id = task.Id },
                new { task.Id, task.Titre, task.Priorite, task.DateEcheance }
            );
        }

        // PUT /api/task/4
        [HttpPut("{id}")]
        public async Task<IActionResult> Update(int id, TodoTask taskModified)
        {
            if (id != taskModified.Id)
                return BadRequest(new { message = "Id manquant."});

            var userId = _userManager.GetUserId(User);
            var task = await _context.TodoTasks
                .FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId);
            
            if (task == null)
                return NotFound(new { message = " Tâche non trouvée."});

            task.Titre = taskModified.Titre;
            task.Description = taskModified.Description;
            task.Priorite = taskModified.Priorite;
            task.EstTerminee = taskModified.EstTerminee;
            task.DateEcheance = taskModified.DateEcheance;
            task.CategoryId = taskModified.CategoryId;

            await _context.SaveChangesAsync();

            return NoContent();
        }

        // DELETE /api/task/4
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            var userId = _userManager.GetUserId(User);
            var task = await _context.TodoTasks
                .FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId);

            if (task == null)
                return NotFound(new { message = "Tâche non trouvée."});

            _context.TodoTasks.Remove(task);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        // PATCH /api/task/5/toggle
        [HttpPatch("{id}")]
        public async Task<IActionResult> Toggle(int id)
        {
            var userId = _userManager.GetUserId(User);
            var task = await _context.TodoTasks
                .FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId);

            if (task == null)
                return NotFound(new { message = "Tâche non trouvée."});

            _context.TodoTasks.Remove(task);
            await _context.SaveChangesAsync();

            return Ok(new{ task.Id, task.EstTerminee});
        }
    }
}