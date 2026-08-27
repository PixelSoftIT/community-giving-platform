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
[Authorize]
public class MeetingsController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    public MeetingsController(ApplicationDbContext db) => _db = db;

    // Any logged-in member can see the meeting calendar and published minutes —
    // scheduling/recording minutes is restricted to Admin/Secretary.
    [HttpGet]
    public async Task<ActionResult<List<MeetingDto>>> GetAll([FromQuery] string? status)
    {
        var query = _db.Meetings.Include(m => m.Attendees).AsQueryable();
        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<MeetingStatus>(status, true, out var s))
            query = query.Where(m => m.Status == s);

        var meetings = await query.OrderByDescending(m => m.ScheduledAtUtc).ToListAsync();
        return Ok(meetings.Select(ToDto).ToList());
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<MeetingDto>> GetById(int id)
    {
        var meeting = await _db.Meetings.Include(m => m.Attendees).FirstOrDefaultAsync(m => m.Id == id);
        if (meeting is null) return NotFound();
        return Ok(ToDto(meeting));
    }

    [HttpPost]
    [Authorize(Roles = "Admin,Secretary")]
    public async Task<ActionResult<MeetingDto>> Create(CreateMeetingRequest request)
    {
        if (!Enum.TryParse<MeetingType>(request.Type, true, out var type))
            return BadRequest("Invalid meeting type. Use Board, Committee, General, Volunteer, or Other.");

        var meeting = new Meeting
        {
            Title = request.Title,
            Type = type,
            ScheduledAtUtc = request.ScheduledAtUtc,
            Location = request.Location,
            AgendaText = request.AgendaText,
            Status = MeetingStatus.Scheduled
        };
        _db.Meetings.Add(meeting);
        await _db.SaveChangesAsync();
        return Ok(ToDto(meeting));
    }

    // Records minutes + attendance for a meeting, flipping it from Scheduled to Completed.
    [HttpPost("{id:int}/minutes")]
    [Authorize(Roles = "Admin,Secretary")]
    public async Task<IActionResult> RecordMinutes(int id, RecordMinutesRequest request)
    {
        var meeting = await _db.Meetings.Include(m => m.Attendees).FirstOrDefaultAsync(m => m.Id == id);
        if (meeting is null) return NotFound();

        meeting.MinutesText = request.MinutesText;
        meeting.MinutesRecordedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        meeting.MinutesRecordedAtUtc = DateTime.UtcNow;
        meeting.Status = MeetingStatus.Completed;

        _db.MeetingAttendees.RemoveRange(meeting.Attendees);
        foreach (var a in request.Attendees)
            meeting.Attendees.Add(new MeetingAttendee { MemberId = a.MemberId, Name = a.Name, Attended = a.Attended });

        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("{id:int}/cancel")]
    [Authorize(Roles = "Admin,Secretary")]
    public async Task<IActionResult> Cancel(int id)
    {
        var meeting = await _db.Meetings.FindAsync(id);
        if (meeting is null) return NotFound();
        meeting.Status = MeetingStatus.Cancelled;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    private static MeetingDto ToDto(Meeting m) => new(m.Id, m.Title, m.Type.ToString(), m.Status.ToString(),
        m.ScheduledAtUtc, m.Location, m.AgendaText, m.MinutesText, m.MinutesRecordedAtUtc,
        m.Attendees.Select(a => new MeetingAttendeeDto(a.MemberId, a.Name, a.Attended)).ToList());
}
