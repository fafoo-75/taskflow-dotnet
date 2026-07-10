using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TaskFlow.Data;
using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using TaskFlow.Models;

namespace TaskFlow.Controllers;

public class HomeController : Controller
{
    private readonly AppDbContext _context;
    private readonly UserManager<IdentityUser> _userManager;

    public HomeController(AppDbContext context,
        UserManager<IdentityUser> userManager)

    {
        _context = context;
        _userManager = userManager;
    }

    public async Task<IActionResult> Index()
    {
        if (!User.Identity!.IsAuthenticated)
        return View(new DashboardViewModel());

    var userId = _userManager.GetUserId(User);

    var tasks = await _context.TodoTasks
        .Where(t => t.UserId == userId)
        .AsNoTracking()
        .ToListAsync();

    var vm = new DashboardViewModel
        {
            Total = tasks.Count,

            // Tâches terminées
            Completed = tasks.Count(task => task.EstTerminee),

            // Tâches en cours : non terminées ET date date non dépassée
            InProgress = tasks.Count(t => !t.EstTerminee && t.DateEcheance >= DateTime.Today),

            // Tâche en retard : non terminées ET date dépassée
            Late = tasks.Count(t => !t.EstTerminee && t.DateEcheance < DateTime.Today),

            TopTasks = tasks
                .Where(t => !t.EstTerminee)
                .OrderByDescending(t => t.Priorite)
                .ThenBy(t => t.DateEcheance)
                .Take(5)
                .ToList()
        };

        return View(vm);
    }
}
