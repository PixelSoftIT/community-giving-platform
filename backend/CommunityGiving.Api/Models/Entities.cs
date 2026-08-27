using Microsoft.AspNetCore.Identity;

namespace CommunityGiving.Api.Models;

// Extends Identity so login/roles are built in. Roles used: "Admin", "Member"
public class ApplicationUser : IdentityUser
{
    public string FullName { get; set; } = string.Empty;
    public string? PhoneNumber2 { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;

    // Nullable: a logged-in user is usually linked to one Member record.
    public int? MemberId { get; set; }
    public Member? Member { get; set; }
}

public enum OrganizationType { Temple, Church, Mosque, Synagogue, Ngo, CommunityCenter, Other }

// Single-row settings table that drives the app's branding and vocabulary — one deployment
// serves one organization, but that organization could be a temple, church, mosque, synagogue,
// NGO, or general community center. Nothing about the domain model is hardcoded to any one of these.
public class OrganizationSettings
{
    public int Id { get; set; } = 1; // singleton row
    public string Name { get; set; } = "Our Community";
    public OrganizationType Type { get; set; } = OrganizationType.CommunityCenter;
    public string Tagline { get; set; } = string.Empty;
    public string ContactEmail { get; set; } = string.Empty;
    public string ContactPhone { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string? LogoUrl { get; set; }
    public string Currency { get; set; } = "aud";
    // What this org calls its enrollment-based offering, e.g. "Sunday School", "Youth Halaqa",
    // "Confirmation Classes", "Job Training Program" — shown in the UI instead of a fixed term.
    public string ProgramsLabel { get; set; } = "Programs & Classes";
    public bool ProgramsEnabled { get; set; } = true;
    public string ReceiptFooterText { get; set; } = string.Empty;
}

// A household unit — one membership can cover multiple people (family, or any group living together)
public class Household
{
    public int Id { get; set; }
    public string HouseholdName { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string PostalCode { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public DateTime JoinedDateUtc { get; set; } = DateTime.UtcNow;
    public MembershipStatus Status { get; set; } = MembershipStatus.Active;

    public List<Member> Members { get; set; } = new();
    public List<ProgramParticipant> ProgramParticipants { get; set; } = new();
}

public enum MembershipStatus { Active, Lapsed, Inactive }

public enum MemberRole { HeadOfHousehold, Spouse, Adult, Volunteer, ClergyOrLeader, BoardMember }

// An individual adult member. Children/youth in an enrollment program go in ProgramParticipant.
public class Member
{
    public int Id { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public DateOnly? DateOfBirth { get; set; }
    public MemberRole RoleInHousehold { get; set; } = MemberRole.Adult;
    public DateTime JoinedDateUtc { get; set; } = DateTime.UtcNow;
    public MembershipStatus Status { get; set; } = MembershipStatus.Active;

    public int HouseholdId { get; set; }
    public Household? Household { get; set; }

    public string? ApplicationUserId { get; set; }
    public List<Donation> Donations { get; set; } = new();
    public List<EventRegistration> EventRegistrations { get; set; } = new();
}

// A young person (or adult) enrolled in one of the org's programs/classes — generic enough for
// Sunday School, youth religious education, confirmation classes, NGO youth programs, etc.
// What it's called in the UI comes from OrganizationSettings.ProgramsLabel.
public class ProgramParticipant
{
    public int Id { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public DateOnly DateOfBirth { get; set; }
    public string ProgramGroup { get; set; } = string.Empty; // e.g. "Level 2", "Grade 5 Class", "Cohort A"
    public string? AllergiesOrNotes { get; set; }
    public string ParentContactEmail { get; set; } = string.Empty;
    public string ParentContactPhone { get; set; } = string.Empty;
    public DateTime EnrolledDateUtc { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;

    public int HouseholdId { get; set; }
    public Household? Household { get; set; }
}

public enum ProjectStatus { Planned, InProgress, OnHold, Completed, Cancelled }

// A managed project or initiative (e.g. "Roof Renovation 2026", "Annual Youth Program",
// "Winter Shelter Drive"). The umbrella; one or more Funds roll up into it so admins can track
// total budget vs. total raised across every fund tied to the effort, not just one campaign.
public class Project
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public ProjectStatus Status { get; set; } = ProjectStatus.Planned;
    public decimal? Budget { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? TargetCompletionDate { get; set; }
    public DateOnly? CompletedDate { get; set; }
    public string? ManagerName { get; set; }   // e.g. the committee member or staff lead overseeing it
    public string? ManagerContact { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public List<Fund> Funds { get; set; } = new();
}

// A fund/campaign that money can be raised for (e.g. "Building Renovation", "Youth Program Supplies",
// "Community Meal Program", "Disaster Relief")
public class Fund
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = "General"; // General, Programs, Event, Building, Outreach
    public decimal? GoalAmount { get; set; }
    public bool IsActive { get; set; } = true;
    public bool AllowNonMemberDonations { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? EndDateUtc { get; set; }

    // Optional: a fund can stand alone, or be managed under a Project umbrella
    public int? ProjectId { get; set; }
    public Project? Project { get; set; }

    public List<Donation> Donations { get; set; } = new();

    // Computed convenience (not mapped) — calculated in queries instead
}

public enum PaymentStatus { Pending, Succeeded, Failed, Refunded }

// A single donation/payment record — works for members and non-members
public class Donation
{
    public int Id { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "aud";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;
    public bool IsRecurring { get; set; } = false;
    public bool IsAnonymous { get; set; } = false;
    public string? Notes { get; set; }

    // Stripe references
    public string? StripePaymentIntentId { get; set; }
    public string? StripeChargeId { get; set; }
    public string? ReceiptUrl { get; set; }

    public int FundId { get; set; }
    public Fund? Fund { get; set; }

    // Nullable: non-member donors won't have a Member record
    public int? MemberId { get; set; }
    public Member? Member { get; set; }

    // Captured directly for non-members (so a receipt/thank-you can be sent)
    public string DonorName { get; set; } = string.Empty;
    public string DonorEmail { get; set; } = string.Empty;
    public string? DonorPhone { get; set; }

    public int? EventId { get; set; }
    public Event? Event { get; set; }
}

// Community events (open to members and public, e.g. a festival, worship service, fundraiser dinner, or workshop)
public class Event
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime StartUtc { get; set; }
    public DateTime? EndUtc { get; set; }
    public string Location { get; set; } = string.Empty;
    public decimal? TicketPrice { get; set; } // null = free
    public int? Capacity { get; set; }
    public bool OpenToPublic { get; set; } = true;
    public bool IsActive { get; set; } = true;
    public int? LinkedFundId { get; set; } // optional: ticket revenue rolls into one specific fund

    public List<EventRegistration> Registrations { get; set; } = new();
    // An event can support/highlight several projects at once (e.g. a festival raising money
    // toward both a building renovation and a new statue) — separate from LinkedFundId, which
    // is specifically where ticket sales go.
    public List<EventProject> EventProjects { get; set; } = new();
}

// Join entity for the Event <-> Project many-to-many relationship
public class EventProject
{
    public int EventId { get; set; }
    public Event? Event { get; set; }
    public int ProjectId { get; set; }
    public Project? Project { get; set; }
}

public enum RegistrationStatus { Registered, Paid, CancelledRefunded, Attended }

public class EventRegistration
{
    public int Id { get; set; }
    public int EventId { get; set; }
    public Event? Event { get; set; }

    public int? MemberId { get; set; }
    public Member? Member { get; set; }

    public string AttendeeName { get; set; } = string.Empty;
    public string AttendeeEmail { get; set; } = string.Empty;
    public string? AttendeePhone { get; set; }
    public int GuestCount { get; set; } = 1;

    public RegistrationStatus Status { get; set; } = RegistrationStatus.Registered;
    public DateTime RegisteredAtUtc { get; set; } = DateTime.UtcNow;
    public int? DonationId { get; set; } // link to the Donation/payment that paid for it
}
