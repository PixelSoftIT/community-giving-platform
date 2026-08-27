using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CommunityGiving.Api.Data;
using CommunityGiving.Api.DTOs;
using CommunityGiving.Api.Models;

namespace CommunityGiving.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin")]
public class AdminController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    public AdminController(ApplicationDbContext db) => _db = db;

    [HttpGet("dashboard")]
    public async Task<ActionResult<DashboardSummary>> GetDashboard()
    {
        var succeeded = _db.Donations.Where(d => d.Status == PaymentStatus.Succeeded);

        var totalAllTime = await succeeded.SumAsync(d => (decimal?)d.Amount) ?? 0;
        var monthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var totalThisMonth = await succeeded.Where(d => d.CreatedAtUtc >= monthStart).SumAsync(d => (decimal?)d.Amount) ?? 0;

        var activeMembers = await _db.Members.CountAsync(m => m.Status == MembershipStatus.Active);
        var totalHouseholds = await _db.Households.CountAsync();
        var upcomingEvents = await _db.Events.CountAsync(e => e.IsActive && e.StartUtc >= DateTime.UtcNow);
        var activeProjects = await _db.Projects.CountAsync(p => p.IsActive && p.Status != ProjectStatus.Completed && p.Status != ProjectStatus.Cancelled);

        var topFunds = await _db.Funds.Where(f => f.IsActive)
            .Select(f => new FundDto(f.Id, f.Name, f.Description, f.Category, f.GoalAmount,
                f.Donations.Where(d => d.Status == PaymentStatus.Succeeded).Sum(d => (decimal?)d.Amount) ?? 0,
                f.IsActive, f.AllowNonMemberDonations, f.EndDateUtc, f.ProjectId, f.Project != null ? f.Project.Name : null))
            .OrderByDescending(f => f.RaisedAmount)
            .Take(5)
            .ToListAsync();

        return Ok(new DashboardSummary(totalAllTime, totalThisMonth, activeMembers, totalHouseholds, upcomingEvents, activeProjects, topFunds));
    }

    // Full donation ledger for bookkeeping/export
    [HttpGet("donations")]
    public async Task<ActionResult<List<DonationDto>>> GetAllDonations([FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        var query = _db.Donations.Include(d => d.Fund).AsQueryable();
        if (from.HasValue) query = query.Where(d => d.CreatedAtUtc >= from);
        if (to.HasValue) query = query.Where(d => d.CreatedAtUtc <= to);

        var donations = await query.OrderByDescending(d => d.CreatedAtUtc)
            .Select(d => new DonationDto(d.Id, d.Amount, d.Currency, d.Status.ToString(), d.CreatedAtUtc,
                d.Fund!.Name, d.IsAnonymous ? "Anonymous" : d.DonorName, d.IsAnonymous, d.ReceiptUrl))
            .ToListAsync();
        return Ok(donations);
    }

    [HttpGet("users")]
    public async Task<ActionResult> GetUsers([FromServices] Microsoft.AspNetCore.Identity.UserManager<Models.ApplicationUser> userManager)
    {
        var users = await _db.Users.ToListAsync();
        var result = new List<object>();
        foreach (var u in users)
        {
            var roles = await userManager.GetRolesAsync(u);
            result.Add(new { u.Id, u.Email, u.FullName, u.IsActive, Roles = roles });
        }
        return Ok(result);
    }

    // Assign an admin-managed role (Admin, Treasurer, Secretary) to an existing user account.
    // "Member" is the only role the public register endpoint can grant on its own.
    [HttpPost("users/{userId}/assign-role")]
    public async Task<IActionResult> AssignRole(string userId, [FromBody] AssignRoleRequest request,
        [FromServices] Microsoft.AspNetCore.Identity.UserManager<Models.ApplicationUser> userManager)
    {
        if (request.Role is not ("Admin" or "Treasurer" or "Secretary" or "ProgramCoordinator" or "Member"))
            return BadRequest("Role must be one of: Admin, Treasurer, Secretary, ProgramCoordinator, Member.");

        var user = await userManager.FindByIdAsync(userId);
        if (user is null) return NotFound();
        if (!await userManager.IsInRoleAsync(user, request.Role))
            await userManager.AddToRoleAsync(user, request.Role);
        return NoContent();
    }

    [HttpPost("users/{userId}/remove-role")]
    public async Task<IActionResult> RemoveRole(string userId, [FromBody] AssignRoleRequest request,
        [FromServices] Microsoft.AspNetCore.Identity.UserManager<Models.ApplicationUser> userManager)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user is null) return NotFound();
        await userManager.RemoveFromRoleAsync(user, request.Role);
        return NoContent();
    }
}
