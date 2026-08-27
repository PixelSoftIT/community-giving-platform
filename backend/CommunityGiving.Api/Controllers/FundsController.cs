using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CommunityGiving.Api.Data;
using CommunityGiving.Api.DTOs;
using CommunityGiving.Api.Models;

namespace CommunityGiving.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class FundsController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    public FundsController(ApplicationDbContext db) => _db = db;

    // PUBLIC — non-members need to see active funds to donate without logging in.
    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<List<FundDto>>> GetActiveFunds()
    {
        var funds = await _db.Funds
            .Where(f => f.IsActive)
            .Select(f => new FundDto(f.Id, f.Name, f.Description, f.Category, f.GoalAmount,
                f.Donations.Where(d => d.Status == PaymentStatus.Succeeded).Sum(d => (decimal?)d.Amount) ?? 0,
                f.IsActive, f.AllowNonMemberDonations, f.EndDateUtc, f.ProjectId, f.Project != null ? f.Project.Name : null))
            .ToListAsync();
        return Ok(funds);
    }

    [HttpGet("{id:int}")]
    [AllowAnonymous]
    public async Task<ActionResult<FundDto>> GetFund(int id)
    {
        var f = await _db.Funds.Include(x => x.Project).FirstOrDefaultAsync(x => x.Id == id);
        if (f is null) return NotFound();
        var raised = await _db.Donations.Where(d => d.FundId == id && d.Status == PaymentStatus.Succeeded)
            .SumAsync(d => (decimal?)d.Amount) ?? 0;
        return Ok(new FundDto(f.Id, f.Name, f.Description, f.Category, f.GoalAmount, raised, f.IsActive,
            f.AllowNonMemberDonations, f.EndDateUtc, f.ProjectId, f.Project?.Name));
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<FundDto>> CreateFund(CreateFundRequest request)
    {
        Project? project = null;
        if (request.ProjectId.HasValue)
        {
            project = await _db.Projects.FindAsync(request.ProjectId.Value);
            if (project is null) return BadRequest("The selected project does not exist.");
        }

        var fund = new Fund
        {
            Name = request.Name,
            Description = request.Description,
            Category = request.Category,
            GoalAmount = request.GoalAmount,
            AllowNonMemberDonations = request.AllowNonMemberDonations,
            EndDateUtc = request.EndDateUtc,
            ProjectId = request.ProjectId
        };
        _db.Funds.Add(fund);
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetFund), new { id = fund.Id },
            new FundDto(fund.Id, fund.Name, fund.Description, fund.Category, fund.GoalAmount, 0, fund.IsActive,
                fund.AllowNonMemberDonations, fund.EndDateUtc, fund.ProjectId, project?.Name));
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> UpdateFund(int id, CreateFundRequest request)
    {
        var fund = await _db.Funds.FindAsync(id);
        if (fund is null) return NotFound();

        if (request.ProjectId.HasValue && !await _db.Projects.AnyAsync(p => p.Id == request.ProjectId))
            return BadRequest("The selected project does not exist.");

        fund.Name = request.Name;
        fund.Description = request.Description;
        fund.Category = request.Category;
        fund.GoalAmount = request.GoalAmount;
        fund.AllowNonMemberDonations = request.AllowNonMemberDonations;
        fund.EndDateUtc = request.EndDateUtc;
        fund.ProjectId = request.ProjectId;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("{id:int}/deactivate")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Deactivate(int id)
    {
        var fund = await _db.Funds.FindAsync(id);
        if (fund is null) return NotFound();
        fund.IsActive = false;
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
