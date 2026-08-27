namespace CommunityGiving.Api.Models;

public enum MeetingType { Board, Committee, General, Volunteer, Other }
public enum MeetingStatus { Scheduled, Completed, Cancelled }

// Covers both "upcoming meeting on the calendar" and "record of a past meeting" — the same
// row moves from Scheduled to Completed once minutes are recorded, so there's one place to
// look for a group's meeting history instead of two disconnected features.
public class Meeting
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public MeetingType Type { get; set; } = MeetingType.General;
    public MeetingStatus Status { get; set; } = MeetingStatus.Scheduled;
    public DateTime ScheduledAtUtc { get; set; }
    public string Location { get; set; } = string.Empty; // physical address or a video call link
    public string? AgendaText { get; set; }

    public string? MinutesText { get; set; }
    public string? MinutesRecordedByUserId { get; set; }
    public DateTime? MinutesRecordedAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public List<MeetingAttendee> Attendees { get; set; } = new();
}

public class MeetingAttendee
{
    public int Id { get; set; }
    public int MeetingId { get; set; }
    public Meeting? Meeting { get; set; }

    public int? MemberId { get; set; }
    public Member? Member { get; set; }
    public string Name { get; set; } = string.Empty; // captured directly so non-members (guests) can be listed too
    public bool Attended { get; set; }
}
