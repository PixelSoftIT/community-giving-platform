using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CommunityGiving.Api.Data;
using CommunityGiving.Api.DTOs;
using CommunityGiving.Api.Models;

namespace CommunityGiving.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize] // every action here requires login; household/member-level ones further require Admin
public class MembersController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    public MembersController(ApplicationDbContext db) => _db = db;

    [HttpGet]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<List<MemberDto>>> GetAll([FromQuery] string? search)
    {
        var query = _db.Members.Include(m => m.Household).AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(m => m.FirstName.Contains(search) || m.LastName.Contains(search) || m.Email.Contains(search));

        var members = await query.Select(m => new MemberDto(m.Id, m.FirstName, m.LastName, m.Email, m.Phone,
            m.RoleInHousehold.ToString(), m.Status.ToString(), m.HouseholdId, m.Household!.HouseholdName)).ToListAsync();
        return Ok(members);
    }

    // Admin creates a new household with its members and (optionally) program participants in one go.
    [HttpPost("households")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult> CreateHousehold(CreateHouseholdRequest request)
    {
        var household = new Household
        {
            HouseholdName = request.HouseholdName,
            Address = request.Address,
            City = request.City,
            PostalCode = request.PostalCode
        };

        foreach (var m in request.Members)
        {
            household.Members.Add(new Member
            {
                FirstName = m.FirstName,
                LastName = m.LastName,
                Email = m.Email,
                Phone = m.Phone,
                DateOfBirth = m.DateOfBirth,
                RoleInHousehold = Enum.Parse<MemberRole>(m.RoleInHousehold)
            });
        }

        if (request.ProgramParticipants != null)
        {
            foreach (var s in request.ProgramParticipants)
            {
                household.ProgramParticipants.Add(new ProgramParticipant
                {
                    FirstName = s.FirstName,
                    LastName = s.LastName,
                    DateOfBirth = s.DateOfBirth,
                    ProgramGroup = s.ProgramGroup,
                    AllergiesOrNotes = s.AllergiesOrNotes,
                    ParentContactEmail = s.ParentContactEmail,
                    ParentContactPhone = s.ParentContactPhone
                });
            }
        }

        _db.Households.Add(household);
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetAll), new { }, new { household.Id });
    }

    [HttpGet("households")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult> GetHouseholds()
    {
        var households = await _db.Households
            .Select(h => new
            {
                h.Id,
                h.HouseholdName,
                h.City,
                h.Status,
                MemberCount = h.Members.Count,
                ParticipantCount = h.ProgramParticipants.Count
            }).ToListAsync();
        return Ok(households);
    }

    // A logged-in member viewing their OWN profile + household (client portal "My Household" page)
    [HttpGet("me")]
    public async Task<ActionResult> GetMyProfile()
    {
        var memberIdClaim = User.FindFirstValue("memberId");
        if (memberIdClaim is null) return NotFound("No member profile linked to this account yet. Contact the office.");

        var member = await _db.Members.Include(m => m.Household).ThenInclude(h => h!.ProgramParticipants)
            .FirstOrDefaultAsync(m => m.Id == int.Parse(memberIdClaim));
        if (member is null) return NotFound();

        return Ok(new
        {
            member.Id,
            member.FirstName,
            member.LastName,
            member.Email,
            member.Phone,
            Household = new
            {
                member.Household!.Id,
                member.Household.HouseholdName,
                Members = member.Household.Members.Select(m => new { m.Id, m.FirstName, m.LastName, m.RoleInHousehold }),
                ProgramParticipants = member.Household.ProgramParticipants.Select(s => new { s.Id, s.FirstName, s.LastName, s.ProgramGroup })
            }
        });
    }

    // A logged-in member's own donation history (client portal "My Giving" page)
    [HttpGet("me/donations")]
    public async Task<ActionResult<List<DonationDto>>> GetMyDonations()
    {
        var memberIdClaim = User.FindFirstValue("memberId");
        if (memberIdClaim is null) return Ok(new List<DonationDto>());

        var donations = await _db.Donations.Include(d => d.Fund)
            .Where(d => d.MemberId == int.Parse(memberIdClaim))
            .OrderByDescending(d => d.CreatedAtUtc)
            .Select(d => new DonationDto(d.Id, d.Amount, d.Currency, d.Status.ToString(), d.CreatedAtUtc,
                d.Fund!.Name, d.DonorName, d.IsAnonymous, d.ReceiptUrl))
            .ToListAsync();
        return Ok(donations);
    }
}
