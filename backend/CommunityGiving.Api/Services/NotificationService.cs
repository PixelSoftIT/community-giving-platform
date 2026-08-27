using CommunityGiving.Api.Data;
using CommunityGiving.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace CommunityGiving.Api.Services;

public record NotificationRecipientInput(string Name, string? Email, string? Phone);

public interface INotificationService
{
    Task<Notification> SendAsync(NotificationCategory category, NotificationChannel channel, string subject,
        string body, int? groupId, List<int>? memberIds, List<int>? contactIds, List<NotificationRecipientInput>? adHocRecipients,
        string sentByUserId);
}

// Resolves a notification's recipients (a saved group, specific members/contacts, and/or
// ad-hoc name+email/phone entries), then sends via email and/or SMS and logs per-recipient
// delivery outcomes so admins can see who was actually reached.
public class NotificationService : INotificationService
{
    private readonly ApplicationDbContext _db;
    private readonly IEmailSender _email;
    private readonly ISmsSender _sms;

    public NotificationService(ApplicationDbContext db, IEmailSender email, ISmsSender sms)
    {
        _db = db;
        _email = email;
        _sms = sms;
    }

    public async Task<Notification> SendAsync(NotificationCategory category, NotificationChannel channel, string subject,
        string body, int? groupId, List<int>? memberIds, List<int>? contactIds, List<NotificationRecipientInput>? adHocRecipients,
        string sentByUserId)
    {
        var recipients = new List<NotificationRecipientInput>();

        if (groupId.HasValue)
        {
            var group = await _db.NotificationGroups.Include(g => g.Recipients).ThenInclude(r => r.Member)
                .Include(g => g.Recipients).ThenInclude(r => r.Contact)
                .FirstOrDefaultAsync(g => g.Id == groupId);
            if (group != null)
            {
                foreach (var r in group.Recipients)
                {
                    var name = r.DisplayName;
                    var emailAddr = r.Email ?? r.Member?.Email ?? r.Contact?.Email;
                    var phone = r.Phone ?? r.Member?.Phone ?? r.Contact?.Phone;
                    if (string.IsNullOrWhiteSpace(name))
                        name = r.Member != null ? $"{r.Member.FirstName} {r.Member.LastName}" : r.Contact != null ? $"{r.Contact.FirstName} {r.Contact.LastName}" : "Recipient";
                    recipients.Add(new NotificationRecipientInput(name, emailAddr, phone));
                }
            }
        }

        if (memberIds is { Count: > 0 })
        {
            var members = await _db.Members.Where(m => memberIds.Contains(m.Id)).ToListAsync();
            recipients.AddRange(members.Select(m => new NotificationRecipientInput($"{m.FirstName} {m.LastName}", m.Email, m.Phone)));
        }

        if (contactIds is { Count: > 0 })
        {
            var contacts = await _db.Contacts.Where(c => contactIds.Contains(c.Id)).ToListAsync();
            recipients.AddRange(contacts.Select(c => new NotificationRecipientInput($"{c.FirstName} {c.LastName}", c.Email, c.Phone)));
        }

        if (adHocRecipients is { Count: > 0 })
            recipients.AddRange(adHocRecipients);

        // De-duplicate by email+phone so the same person on multiple lists doesn't get double-messaged
        recipients = recipients
            .GroupBy(r => (r.Email?.ToLower(), r.Phone))
            .Select(g => g.First())
            .ToList();

        var notification = new Notification
        {
            Category = category,
            Channel = channel,
            Subject = subject,
            Body = body,
            NotificationGroupId = groupId,
            SentByUserId = sentByUserId,
            Status = NotificationStatus.Sending,
            RecipientCount = recipients.Count
        };
        _db.Notifications.Add(notification);
        await _db.SaveChangesAsync();

        var failureCount = 0;
        foreach (var r in recipients)
        {
            var delivery = new NotificationDelivery
            {
                NotificationId = notification.Id,
                RecipientName = r.Name,
                Email = r.Email,
                Phone = r.Phone,
                Channel = channel
            };

            var anySucceeded = false;
            var anyAttempted = false;

            if ((channel == NotificationChannel.Email || channel == NotificationChannel.Both) && !string.IsNullOrWhiteSpace(r.Email))
            {
                anyAttempted = true;
                var ok = await _email.SendAsync(r.Email, r.Name, subject, body);
                anySucceeded |= ok;
            }
            if ((channel == NotificationChannel.Sms || channel == NotificationChannel.Both) && !string.IsNullOrWhiteSpace(r.Phone))
            {
                anyAttempted = true;
                var ok = await _sms.SendAsync(r.Phone, $"{subject}\n{body}");
                anySucceeded |= ok;
            }

            if (!anyAttempted)
            {
                delivery.Status = DeliveryStatus.Failed;
                delivery.ErrorMessage = "No email/phone on file for the requested channel.";
                failureCount++;
            }
            else if (anySucceeded)
            {
                delivery.Status = DeliveryStatus.Sent;
                delivery.DeliveredAtUtc = DateTime.UtcNow;
            }
            else
            {
                delivery.Status = DeliveryStatus.Failed;
                delivery.ErrorMessage = "Delivery failed — check email/SMS provider configuration.";
                failureCount++;
            }

            _db.NotificationDeliveries.Add(delivery);
        }

        notification.Status = NotificationStatus.Sent;
        notification.SentAtUtc = DateTime.UtcNow;
        notification.FailureCount = failureCount;
        await _db.SaveChangesAsync();

        return notification;
    }
}
