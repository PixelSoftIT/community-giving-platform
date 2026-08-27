using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CommunityGiving.Api.Data;
using CommunityGiving.Api.DTOs;
using CommunityGiving.Api.Models;

namespace CommunityGiving.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ProjectsController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    public ProjectsController(ApplicationDbContext db) => _db = db;

    // PUBLIC — lets the donate page group funds under their parent project (e.g. show progress
    // toward the whole "Roof Renovation 2026" effort, not just one line-item fund at a time).
    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<List<ProjectDto>>> GetAll([FromQuery] bool activeOnly = true)
    {
        var query = _db.Projects.Include(p => p.Funds).ThenInclude(f => f.Donations).AsQueryable();
        if (activeOnly) query = query.Where(p => p.IsActive);

        var projects = await query.ToListAsync();
        return Ok(projects.Select(ToDto).ToList());
    }

    [HttpGet("{id:int}")]
    [AllowAnonymous]
    public async Task<ActionResult<ProjectDto>> GetById(int id)
    {
        var project = await _db.Projects.Include(p => p.Funds).ThenInclude(f => f.Donations)
            .FirstOrDefaultAsync(p => p.Id == id);
        if (project is null) return NotFound();
        return Ok(ToDto(project));
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<ProjectDto>> Create(CreateProjectRequest request)
    {
        if (!Enum.TryParse<ProjectStatus>(request.Status, true, out var status))
            return BadRequest("Invalid status. Use Planned, InProgress, OnHold, Completed, or Cancelled.");

        var project = new Project
        {
            Name = request.Name,
            Description = request.Description,
            Status = status,
            Budget = request.Budget,
            StartDate = request.StartDate,
            TargetCompletionDate = request.TargetCompletionDate,
            ManagerName = request.ManagerName,
            ManagerContact = request.ManagerContact
        };
        _db.Projects.Add(project);
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetById), new { id = project.Id }, ToDto(project));
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Update(int id, CreateProjectRequest request)
    {
        var project = await _db.Projects.FindAsync(id);
        if (project is null) return NotFound();
        if (!Enum.TryParse<ProjectStatus>(request.Status, true, out var status))
            return BadRequest("Invalid status. Use Planned, InProgress, OnHold, Completed, or Cancelled.");

        project.Name = request.Name;
        project.Description = request.Description;
        project.Status = status;
        project.Budget = request.Budget;
        project.StartDate = request.StartDate;
        project.TargetCompletionDate = request.TargetCompletionDate;
        project.ManagerName = request.ManagerName;
        project.ManagerContact = request.ManagerContact;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // Quick status-only update — e.g. moving a project from "InProgress" to "Completed"
    // without re-submitting the whole form.
    [HttpPatch("{id:int}/status")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> UpdateStatus(int id, UpdateProjectStatusRequest request)
    {
        var project = await _db.Projects.FindAsync(id);
        if (project is null) return NotFound();
        if (!Enum.TryParse<ProjectStatus>(request.Status, true, out var status))
            return BadRequest("Invalid status.");

        project.Status = status;
        if (status == ProjectStatus.Completed)
            project.CompletedDate = request.CompletedDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("{id:int}/archive")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Archive(int id)
    {
        var project = await _db.Projects.FindAsync(id);
        if (project is null) return NotFound();
        project.IsActive = false;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    private static ProjectDto ToDto(Project p)
    {
        var raised = p.Funds.SelectMany(f => f.Donations)
            .Where(d => d.Status == PaymentStatus.Succeeded)
            .Sum(d => d.Amount);

        var fundDtos = p.Funds.Select(f => new FundDto(
            f.Id, f.Name, f.Description, f.Category, f.GoalAmount,
            f.Donations.Where(d => d.Status == PaymentStatus.Succeeded).Sum(d => d.Amount),
            f.IsActive, f.AllowNonMemberDonations, f.EndDateUtc, f.ProjectId, p.Name)).ToList();

        return new ProjectDto(p.Id, p.Name, p.Description, p.Status.ToString(), p.Budget, raised,
            p.StartDate, p.TargetCompletionDate, p.CompletedDate, p.ManagerName, p.ManagerContact,
            p.IsActive, p.Funds.Count, fundDtos);
    }
}
