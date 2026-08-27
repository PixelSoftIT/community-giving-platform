using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CommunityGiving.Api.Data;
using CommunityGiving.Api.DTOs;
using CommunityGiving.Api.Models;

namespace CommunityGiving.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class EventsController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    public EventsController(ApplicationDbContext db) => _db = db;

    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<List<EventDto>>> GetUpcoming()
    {
        var events = await _db.Events
            .Include(e => e.Registrations)
            .Include(e => e.EventProjects).ThenInclude(ep => ep.Project)
            .Where(e => e.IsActive && e.StartUtc >= DateTime.UtcNow)
            .OrderBy(e => e.StartUtc)
            .ToListAsync();
        return Ok(events.Select(ToDto).ToList());
    }

    // Admin console needs to see past/inactive events too, not just the public upcoming list
    [HttpGet("all")]
    [Authorize(Roles = "Admin,Secretary")]
    public async Task<ActionResult<List<EventDto>>> GetAll()
    {
        var events = await _db.Events
            .Include(e => e.Registrations)
            .Include(e => e.EventProjects).ThenInclude(ep => ep.Project)
            .OrderByDescending(e => e.StartUtc)
            .ToListAsync();
        return Ok(events.Select(ToDto).ToList());
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult> Create(CreateEventRequest request)
    {
        var ev = new Event
        {
            Title = request.Title,
            Description = request.Description,
            StartUtc = request.StartUtc,
            EndUtc = request.EndUtc,
            Location = request.Location,
            TicketPrice = request.TicketPrice,
            Capacity = request.Capacity,
            OpenToPublic = request.OpenToPublic,
            LinkedFundId = request.LinkedFundId
        };
        if (request.ProjectIds != null)
            foreach (var projectId in request.ProjectIds.Distinct())
                ev.EventProjects.Add(new EventProject { ProjectId = projectId });

        _db.Events.Add(ev);
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetUpcoming), new { }, new { ev.Id });
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Update(int id, CreateEventRequest request)
    {
        var ev = await _db.Events.Include(e => e.EventProjects).FirstOrDefaultAsync(e => e.Id == id);
        if (ev is null) return NotFound();

        ev.Title = request.Title;
        ev.Description = request.Description;
        ev.StartUtc = request.StartUtc;
        ev.EndUtc = request.EndUtc;
        ev.Location = request.Location;
        ev.TicketPrice = request.TicketPrice;
        ev.Capacity = request.Capacity;
        ev.OpenToPublic = request.OpenToPublic;
        ev.LinkedFundId = request.LinkedFundId;

        _db.EventProjects.RemoveRange(ev.EventProjects);
        if (request.ProjectIds != null)
            foreach (var projectId in request.ProjectIds.Distinct())
                ev.EventProjects.Add(new EventProject { EventId = id, ProjectId = projectId });

        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("{id:int}/deactivate")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Deactivate(int id)
    {
        var ev = await _db.Events.FindAsync(id);
        if (ev is null) return NotFound();
        ev.IsActive = false;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // Free events register directly here. Paid events register via /api/payments/create-intent
    // (passing eventId) and get marked Paid by the Stripe webhook.
    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<ActionResult> Register(RegisterForEventRequest request)
    {
        var ev = await _db.Events.FindAsync(request.EventId);
        if (ev is null || !ev.IsActive) return NotFound("Event not found.");
        if (!ev.OpenToPublic && !(User.Identity?.IsAuthenticated ?? false))
            return Forbid("This event is for members only — please log in.");

        if (ev.Capacity.HasValue)
        {
            var currentCount = await _db.EventRegistrations
                .CountAsync(r => r.EventId == ev.Id && r.Status != RegistrationStatus.CancelledRefunded);
            if (currentCount + request.GuestCount > ev.Capacity)
                return BadRequest("Not enough spots remaining for this event.");
        }

        var reg = new EventRegistration
        {
            EventId = ev.Id,
            AttendeeName = request.AttendeeName,
            AttendeeEmail = request.AttendeeEmail,
            AttendeePhone = request.AttendeePhone,
            GuestCount = request.GuestCount,
            Status = ev.TicketPrice is null or 0 ? RegistrationStatus.Registered : RegistrationStatus.Registered // set to Paid via webhook if ticketed
        };
        _db.EventRegistrations.Add(reg);
        await _db.SaveChangesAsync();
        return Ok(new { reg.Id, RequiresPayment = ev.TicketPrice is > 0 });
    }

    [HttpGet("{id:int}/registrations")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult> GetRegistrations(int id)
    {
        var regs = await _db.EventRegistrations.Where(r => r.EventId == id)
            .Select(r => new { r.Id, r.AttendeeName, r.AttendeeEmail, r.GuestCount, r.Status })
            .ToListAsync();
        return Ok(regs);
    }

    private static EventDto ToDto(Event e) => new(e.Id, e.Title, e.Description, e.StartUtc, e.EndUtc, e.Location,
        e.TicketPrice, e.Capacity, e.Registrations.Count(r => r.Status != RegistrationStatus.CancelledRefunded), e.OpenToPublic,
        e.EventProjects.Where(ep => ep.Project != null).Select(ep => new LinkedProjectRef(ep.ProjectId, ep.Project!.Name)).ToList());
}
