using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CommunityGiving.Api.Data;
using CommunityGiving.Api.DTOs;
using CommunityGiving.Api.Models;
using CommunityGiving.Api.Services;

namespace CommunityGiving.Api.Controllers;

[ApiController]
[Route("api")]
[Authorize] // every action here requires login, per the "parents must have an account" decision
public class ProgramRegistrationsController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly IStripeService _stripe;

    public ProgramRegistrationsController(ApplicationDbContext db, IStripeService stripe)
    {
        _db = db;
        _stripe = stripe;
    }

    // ---------- Program levels (e.g. Prep-Year 12, or any org's own naming) ----------
    [HttpGet("program-terms/levels")]
    public async Task<ActionResult<List<ProgramLevelDto>>> GetLevels()
    {
        var levels = await _db.ProgramLevels.Where(l => l.IsActive).OrderBy(l => l.SortOrder)
            .Select(l => new ProgramLevelDto(l.Id, l.Name, l.SortOrder, l.IsActive))
            .ToListAsync();
        return Ok(levels);
    }

    [HttpPost("program-terms/levels")]
    [Authorize(Roles = "Admin,ProgramCoordinator")]
    public async Task<ActionResult<ProgramLevelDto>> CreateLevel(CreateProgramLevelRequest request)
    {
        var level = new ProgramLevel { Name = request.Name, SortOrder = request.SortOrder };
        _db.ProgramLevels.Add(level);
        await _db.SaveChangesAsync();
        return Ok(new ProgramLevelDto(level.Id, level.Name, level.SortOrder, level.IsActive));
    }

    [HttpDelete("program-terms/levels/{id:int}")]
    [Authorize(Roles = "Admin,ProgramCoordinator")]
    public async Task<IActionResult> DeactivateLevel(int id)
    {
        var level = await _db.ProgramLevels.FindAsync(id);
        if (level is null) return NotFound();
        level.IsActive = false; // soft-delete: keeps historical registrations readable
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // ---------- Program terms ----------
    [HttpGet("program-terms")]
    public async Task<ActionResult<List<ProgramTermDto>>> GetTerms()
    {
        var terms = await _db.ProgramTerms
            .Select(t => new ProgramTermDto(t.Id, t.Name, t.StartDate, t.EndDate, t.FeePerChild,
                t.IsOpenForRegistration, t.IsActive, t.RegisteredChildren.Count))
            .ToListAsync();
        return Ok(terms);
    }

    [HttpPost("program-terms")]
    [Authorize(Roles = "Admin,ProgramCoordinator")]
    public async Task<ActionResult<ProgramTermDto>> CreateTerm(CreateProgramTermRequest request)
    {
        var term = new ProgramTerm
        {
            Name = request.Name,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            FeePerChild = request.FeePerChild
        };
        _db.ProgramTerms.Add(term);
        await _db.SaveChangesAsync();
        return Ok(new ProgramTermDto(term.Id, term.Name, term.StartDate, term.EndDate, term.FeePerChild, term.IsOpenForRegistration, term.IsActive, 0));
    }

    [HttpPost("program-terms/{id:int}/toggle-open")]
    [Authorize(Roles = "Admin,ProgramCoordinator")]
    public async Task<IActionResult> ToggleOpen(int id)
    {
        var term = await _db.ProgramTerms.FindAsync(id);
        if (term is null) return NotFound();
        term.IsOpenForRegistration = !term.IsOpenForRegistration;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // ---------- Sibling discount tiers ----------
    [HttpGet("program-terms/discount-tiers")]
    [Authorize(Roles = "Admin,ProgramCoordinator")]
    public async Task<ActionResult<List<SiblingDiscountTierDto>>> GetDiscountTiers()
    {
        var tiers = await _db.SiblingDiscountTiers.OrderBy(t => t.ChildPosition)
            .Select(t => new SiblingDiscountTierDto(t.Id, t.ChildPosition, t.DiscountPercent))
            .ToListAsync();
        return Ok(tiers);
    }

    // Create or update the tier for a given child position — keeps the admin UI simple
    // (one row per position, just save it) rather than needing separate create/edit flows.
    [HttpPost("program-terms/discount-tiers")]
    [Authorize(Roles = "Admin,ProgramCoordinator")]
    public async Task<IActionResult> UpsertDiscountTier(UpsertSiblingDiscountTierRequest request)
    {
        if (request.ChildPosition < 1) return BadRequest("Child position must be 1 or greater.");
        if (request.DiscountPercent < 0 || request.DiscountPercent > 100) return BadRequest("Discount percent must be between 0 and 100.");

        var existing = await _db.SiblingDiscountTiers.FirstOrDefaultAsync(t => t.ChildPosition == request.ChildPosition);
        if (existing != null)
        {
            existing.DiscountPercent = request.DiscountPercent;
        }
        else
        {
            _db.SiblingDiscountTiers.Add(new SiblingDiscountTier { ChildPosition = request.ChildPosition, DiscountPercent = request.DiscountPercent });
        }
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("program-terms/discount-tiers/{id:int}")]
    [Authorize(Roles = "Admin,ProgramCoordinator")]
    public async Task<IActionResult> DeleteDiscountTier(int id)
    {
        var tier = await _db.SiblingDiscountTiers.FindAsync(id);
        if (tier is null) return NotFound();
        _db.SiblingDiscountTiers.Remove(tier);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // ---------- Quote (live pricing preview, no payment yet) ----------
    [HttpPost("program-registrations/quote")]
    public async Task<ActionResult<RegistrationQuoteResponse>> Quote(RegistrationQuoteRequest request)
    {
        if (request.Children.Count == 0) return BadRequest("Add at least one child to get a quote.");
        List<(RegistrationChildInput input, int position, decimal baseFee, decimal discountPercent, decimal feeCharged)> lines;
        try { lines = await ComputeQuoteAsync(request.ProgramTermId, request.Children); }
        catch (InvalidOperationException ex) { return BadRequest(ex.Message); }

        var dtoLines = lines.Select(l => new RegistrationQuoteLineDto(l.input.FirstName, l.input.LastName, l.position, l.baseFee, l.discountPercent, l.feeCharged)).ToList();
        return Ok(new RegistrationQuoteResponse(dtoLines, dtoLines.Sum(l => l.FeeCharged)));
    }

    // ---------- Create the payment-backed registration ----------
    [HttpPost("program-registrations/create-intent")]
    public async Task<ActionResult<RegistrationIntentResponse>> CreateIntent(CreateRegistrationIntentRequest request)
    {
        if (request.Children.Count == 0) return BadRequest("Add at least one child to register.");

        var memberIdClaim = User.FindFirstValue("memberId");
        if (memberIdClaim is null) return BadRequest("Your login isn't linked to a member/household record yet. Contact the office.");
        var member = await _db.Members.FindAsync(int.Parse(memberIdClaim));
        if (member is null) return BadRequest("Member record not found.");

        List<(RegistrationChildInput input, int position, decimal baseFee, decimal discountPercent, decimal feeCharged)> lines;
        try { lines = await ComputeQuoteAsync(request.ProgramTermId, request.Children); }
        catch (InvalidOperationException ex) { return BadRequest(ex.Message); }

        var total = lines.Sum(l => l.feeCharged);

        var batch = new ProgramRegistrationBatch
        {
            ProgramTermId = request.ProgramTermId,
            HouseholdId = member.HouseholdId,
            RegisteredByMemberId = member.Id,
            TotalAmount = total,
            Status = RegistrationBatchStatus.Pending
        };
        foreach (var l in lines)
        {
            batch.Children.Add(new ProgramRegistrationChild
            {
                ProgramTermId = request.ProgramTermId,
                FirstName = l.input.FirstName,
                LastName = l.input.LastName,
                DateOfBirth = l.input.DateOfBirth,
                ProgramGroup = l.input.ProgramGroup,
                AllergiesOrNotes = l.input.AllergiesOrNotes,
                ChildPositionInFamily = l.position,
                BaseFee = l.baseFee,
                DiscountPercentApplied = l.discountPercent,
                FeeCharged = l.feeCharged
            });
        }
        _db.ProgramRegistrationBatches.Add(batch);
        await _db.SaveChangesAsync();

        var intent = await _stripe.CreatePaymentIntentAsync(total, "aud", member.Email,
            new Dictionary<string, string> { { "registrationBatchId", batch.Id.ToString() } });
        batch.StripePaymentIntentId = intent.Id;
        await _db.SaveChangesAsync();

        return Ok(new RegistrationIntentResponse(intent.ClientSecret, batch.Id, total));
    }

    // ---------- History ----------
    [HttpGet("program-registrations/mine")]
    public async Task<ActionResult<List<RegistrationBatchDto>>> GetMine()
    {
        var memberIdClaim = User.FindFirstValue("memberId");
        if (memberIdClaim is null) return Ok(new List<RegistrationBatchDto>());
        var member = await _db.Members.FindAsync(int.Parse(memberIdClaim));
        if (member is null) return Ok(new List<RegistrationBatchDto>());

        var batches = await _db.ProgramRegistrationBatches
            .Include(b => b.ProgramTerm).Include(b => b.Household).Include(b => b.RegisteredByMember).Include(b => b.Children)
            .Where(b => b.HouseholdId == member.HouseholdId)
            .OrderByDescending(b => b.CreatedAtUtc)
            .ToListAsync();
        return Ok(batches.Select(ToDto).ToList());
    }

    [HttpGet("program-registrations")]
    [Authorize(Roles = "Admin,ProgramCoordinator")]
    public async Task<ActionResult<List<RegistrationBatchDto>>> GetAll([FromQuery] int? termId)
    {
        var query = _db.ProgramRegistrationBatches
            .Include(b => b.ProgramTerm).Include(b => b.Household).Include(b => b.RegisteredByMember).Include(b => b.Children)
            .AsQueryable();
        if (termId.HasValue) query = query.Where(b => b.ProgramTermId == termId);

        var batches = await query.OrderByDescending(b => b.CreatedAtUtc).ToListAsync();
        return Ok(batches.Select(ToDto).ToList());
    }

    // Shared by both the live quote preview and the actual charge, so what a parent sees
    // before paying always matches exactly what they're charged.
    private async Task<List<(RegistrationChildInput input, int position, decimal baseFee, decimal discountPercent, decimal feeCharged)>> ComputeQuoteAsync(
        int programTermId, List<RegistrationChildInput> children)
    {
        var term = await _db.ProgramTerms.FindAsync(programTermId);
        if (term is null || !term.IsOpenForRegistration)
            throw new InvalidOperationException("This program term is not currently open for registration.");

        var tiers = await _db.SiblingDiscountTiers.OrderBy(t => t.ChildPosition).ToListAsync();

        decimal DiscountForPosition(int position)
        {
            if (tiers.Count == 0) return 0;
            var exact = tiers.FirstOrDefault(t => t.ChildPosition == position);
            if (exact != null) return exact.DiscountPercent;
            var highest = tiers[^1]; // highest ChildPosition, since the list is ordered ascending
            return position > highest.ChildPosition ? highest.DiscountPercent : 0;
        }

        // Oldest child = position 1, so the discount naturally favours the youngest children —
        // a common convention for sibling-discount schemes.
        var ordered = children.OrderBy(c => c.DateOfBirth).ToList();
        var result = new List<(RegistrationChildInput, int, decimal, decimal, decimal)>();
        for (var i = 0; i < ordered.Count; i++)
        {
            var position = i + 1;
            var discountPercent = DiscountForPosition(position);
            var baseFee = term.FeePerChild;
            var feeCharged = Math.Round(baseFee * (1 - discountPercent / 100m), 2);
            result.Add((ordered[i], position, baseFee, discountPercent, feeCharged));
        }
        return result;
    }

    private static RegistrationBatchDto ToDto(ProgramRegistrationBatch b) => new(
        b.Id, b.ProgramTerm?.Name ?? "", b.Household?.HouseholdName ?? "",
        b.RegisteredByMember != null ? $"{b.RegisteredByMember.FirstName} {b.RegisteredByMember.LastName}" : "",
        b.TotalAmount, b.Status.ToString(), b.CreatedAtUtc, b.PaidAtUtc,
        b.Children.Select(c => new RegistrationChildDto(c.Id, c.FirstName, c.LastName, c.DateOfBirth,
            c.ProgramGroup, c.ChildPositionInFamily, c.BaseFee, c.DiscountPercentApplied, c.FeeCharged)).ToList());
}
