namespace CommunityGiving.Api.Models;

// A non-member the org wants to keep on file — a regular guest donor, a prospective member,
// a vendor contact, etc. Distinct from Member so we're not forcing full membership just to
// send someone a receipt or a notification.
public class Contact
{
    public int Id { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public enum NotificationCategory { General, Payments, Events, Meetings, Finance, Urgent }
public enum NotificationChannel { Email, Sms, Both }
public enum NotificationStatus { Draft, Sending, Sent, Failed }

// A named list of recipients an admin can message repeatedly (e.g. "Board Members",
// "Sunday School Parents", "Volunteers") without re-picking people every time.
public class NotificationGroup
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public List<NotificationGroupRecipient> Recipients { get; set; } = new();
}

// A recipient in a group — points at a Member OR a Contact OR is just a raw email/phone
// for someone not otherwise on file.
public class NotificationGroupRecipient
{
    public int Id { get; set; }
    public int NotificationGroupId { get; set; }
    public NotificationGroup? NotificationGroup { get; set; }

    public int? MemberId { get; set; }
    public Member? Member { get; set; }
    public int? ContactId { get; set; }
    public Contact? Contact { get; set; }

    // Denormalized so a send doesn't need extra joins, and so ad-hoc (non-Member/Contact)
    // recipients are supported directly.
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string DisplayName { get; set; } = string.Empty;
}

// A record of a notification blast — sent by category, to a group, and/or to ad-hoc recipients.
public class Notification
{
    public int Id { get; set; }
    public NotificationCategory Category { get; set; } = NotificationCategory.General;
    public NotificationChannel Channel { get; set; } = NotificationChannel.Email;
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public NotificationStatus Status { get; set; } = NotificationStatus.Draft;

    public int? NotificationGroupId { get; set; }
    public NotificationGroup? NotificationGroup { get; set; }

    public string SentByUserId { get; set; } = string.Empty;
    public DateTime? SentAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public int RecipientCount { get; set; }
    public int FailureCount { get; set; }

    public List<NotificationDelivery> Deliveries { get; set; } = new();
}

public enum DeliveryStatus { Pending, Sent, Failed }

// Per-recipient delivery outcome, so an admin can see who did/didn't get a message.
public class NotificationDelivery
{
    public int Id { get; set; }
    public int NotificationId { get; set; }
    public Notification? Notification { get; set; }

    public string RecipientName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public NotificationChannel Channel { get; set; }
    public DeliveryStatus Status { get; set; } = DeliveryStatus.Pending;
    public string? ErrorMessage { get; set; }
    public DateTime? DeliveredAtUtc { get; set; }
}
