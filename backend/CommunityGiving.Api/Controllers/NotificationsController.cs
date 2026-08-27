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
[Route("api/[controller]")]
[Authorize(Roles = "Admin,Secretary")]
public class NotificationsController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly INotificationService _notifications;
    private readonly IAuditService _audit;

    public NotificationsController(ApplicationDbContext db, INotificationService notifications, IAuditService audit)
    {
        _db = db;
        _notifications = notifications;
        _audit = audit;
    }

    // ---------- Groups ----------
    [HttpGet("groups")]
    public async Task<ActionResult<List<NotificationGroupDto>>> GetGroups()
    {
        var groups = await _db.NotificationGroups
            .Select(g => new NotificationGroupDto(g.Id, g.Name, g.Description, g.Recipients.Count))
            .ToListAsync();
        return Ok(groups);
    }

    [HttpPost("groups")]
    public async Task<ActionResult<NotificationGroupDto>> CreateGroup(CreateNotificationGroupRequest request)
    {
        var group = new NotificationGroup { Name = request.Name, Description = request.Description };
        _db.NotificationGroups.Add(group);
        await _db.SaveChangesAsync();
        return Ok(new NotificationGroupDto(group.Id, group.Name, group.Description, 0));
    }

    [HttpPost("groups/{groupId:int}/recipients")]
    public async Task<IActionResult> AddRecipient(int groupId, AddGroupRecipientRequest request)
    {
        var group = await _db.NotificationGroups.FindAsync(groupId);
        if (group is null) return NotFound();

        _db.NotificationGroupRecipients.Add(new NotificationGroupRecipient
        {
            NotificationGroupId = groupId,
            MemberId = request.MemberId,
            ContactId = request.ContactId,
            Email = request.Email,
            Phone = request.Phone,
            DisplayName = request.DisplayName ?? string.Empty
        });
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("groups/{groupId:int}/recipients/{recipientId:int}")]
    public async Task<IActionResult> RemoveRecipient(int groupId, int recipientId)
    {
        var recipient = await _db.NotificationGroupRecipients.FirstOrDefaultAsync(r => r.Id == recipientId && r.NotificationGroupId == groupId);
        if (recipient is null) return NotFound();
        _db.NotificationGroupRecipients.Remove(recipient);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet("groups/{groupId:int}/recipients")]
    public async Task<ActionResult> GetGroupRecipients(int groupId)
    {
        var recipients = await _db.NotificationGroupRecipients
            .Include(r => r.Member).Include(r => r.Contact)
            .Where(r => r.NotificationGroupId == groupId)
            .Select(r => new
            {
                r.Id,
                Name = r.DisplayName != "" ? r.DisplayName : (r.Member != null ? r.Member.FirstName + " " + r.Member.LastName : r.Contact != null ? r.Contact.FirstName + " " + r.Contact.LastName : "Recipient"),
                Email = r.Email ?? (r.Member != null ? r.Member.Email : r.Contact != null ? r.Contact.Email : null),
                Phone = r.Phone ?? (r.Member != null ? r.Member.Phone : r.Contact != null ? r.Contact.Phone : null)
            })
            .ToListAsync();
        return Ok(recipients);
    }

    // ---------- Sending ----------
    // Sends by category (General/Payments/Events/Meetings/Finance/Urgent) to a saved group,
    // specific members/contacts, and/or ad-hoc recipients — via email, SMS, or both.
    [HttpPost("send")]
    public async Task<ActionResult<NotificationDto>> Send(SendNotificationRequest request)
    {
        if (!Enum.TryParse<NotificationCategory>(request.Category, true, out var category))
            return BadRequest("Invalid category.");
        if (!Enum.TryParse<NotificationChannel>(request.Channel, true, out var channel))
            return BadRequest("Invalid channel. Use Email, Sms, or Both.");

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown";
        var userEmail = User.FindFirstValue(ClaimTypes.Email) ?? "unknown";

        var adHoc = request.AdHocRecipients?.Select(a => new NotificationRecipientInput(a.Name, a.Email, a.Phone)).ToList();

        var notification = await _notifications.SendAsync(category, channel, request.Subject, request.Body,
            request.GroupId, request.MemberIds, request.ContactIds, adHoc, userId);

        await _audit.LogAsync(userId, userEmail, "Notification.Send", "Notification", notification.Id.ToString(),
            $"Category={category}, Channel={channel}, Recipients={notification.RecipientCount}", HttpContext.Connection.RemoteIpAddress?.ToString());

        return Ok(new NotificationDto(notification.Id, notification.Category.ToString(), notification.Channel.ToString(),
            notification.Subject, notification.Status.ToString(), notification.SentAtUtc, notification.RecipientCount, notification.FailureCount, null));
    }

    [HttpGet("history")]
    public async Task<ActionResult<List<NotificationDto>>> GetHistory()
    {
        var history = await _db.Notifications.OrderByDescending(n => n.CreatedAtUtc)
            .Select(n => new NotificationDto(n.Id, n.Category.ToString(), n.Channel.ToString(), n.Subject,
                n.Status.ToString(), n.SentAtUtc, n.RecipientCount, n.FailureCount, null))
            .Take(100)
            .ToListAsync();
        return Ok(history);
    }

    [HttpGet("{id:int}/deliveries")]
    public async Task<ActionResult<List<NotificationDeliveryDto>>> GetDeliveries(int id)
    {
        var deliveries = await _db.NotificationDeliveries.Where(d => d.NotificationId == id)
            .Select(d => new NotificationDeliveryDto(d.RecipientName, d.Email, d.Phone, d.Status.ToString(), d.ErrorMessage))
            .ToListAsync();
        return Ok(deliveries);
    }
}
