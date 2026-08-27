namespace CommunityGiving.Api.Models;

// A specific enrollment cycle (e.g. "2027 Daham Pasala Year") with its own per-child fee.
// Admins open one term at a time for registration; closing it (IsOpenForRegistration=false)
// stops new sign-ups without deleting historical registration records.
public class ProgramTerm
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty; // e.g. "2027 Program Year"
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public decimal FeePerChild { get; set; }
    public bool IsOpenForRegistration { get; set; } = true;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public List<ProgramRegistrationChild> RegisteredChildren { get; set; } = new();
}

// Admin-configurable sibling discount by birth-order position within a single registration
// submission (1st child = position 1, and so on). Any position beyond the highest configured
// tier reuses that highest tier's discount, so admins don't have to define every position —
// e.g. defining positions 1-4 automatically covers a 5th, 6th child at the position-4 rate.
public class SiblingDiscountTier
{
    public int Id { get; set; }
    public int ChildPosition { get; set; } // 1, 2, 3, 4...
    public decimal DiscountPercent { get; set; } // 0-100
}

// Admin-managed list of enrollment levels (e.g. "Prep" through "Year 12" for a school-style
// program, or whatever naming fits another org's program — "Beginner/Intermediate/Advanced",
// age-based groups, etc.). Keeps the registration form's level picker a dropdown instead of
// free text, while staying generic across org types since the org defines its own list.
public class ProgramLevel
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty; // e.g. "Year 5"
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

public enum RegistrationBatchStatus { Pending, Paid, Cancelled, Failed }

// One checkout submission — a parent registering one or more of their children for a term in
// a single payment. Mirrors the Donation/PaymentIntent pattern used elsewhere in the app.
public class ProgramRegistrationBatch
{
    public int Id { get; set; }
    public int ProgramTermId { get; set; }
    public ProgramTerm? ProgramTerm { get; set; }

    public int HouseholdId { get; set; }
    public Household? Household { get; set; }
    public int RegisteredByMemberId { get; set; }
    public Member? RegisteredByMember { get; set; }

    public decimal TotalAmount { get; set; }
    public RegistrationBatchStatus Status { get; set; } = RegistrationBatchStatus.Pending;
    public string? StripePaymentIntentId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? PaidAtUtc { get; set; }

    public List<ProgramRegistrationChild> Children { get; set; } = new();
}

// One child within a registration batch — captures the fee/discount actually applied at the
// time of registration (kept even if the discount tiers or term fee change later), and links
// to the resulting ProgramParticipant once payment succeeds.
public class ProgramRegistrationChild
{
    public int Id { get; set; }
    public int BatchId { get; set; }
    public ProgramRegistrationBatch? Batch { get; set; }

    public int ProgramTermId { get; set; }
    public ProgramTerm? ProgramTerm { get; set; }

    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public DateOnly DateOfBirth { get; set; }
    public string ProgramGroup { get; set; } = string.Empty;
    public string? AllergiesOrNotes { get; set; }

    public int ChildPositionInFamily { get; set; } // 1 = oldest registered this submission
    public decimal BaseFee { get; set; }
    public decimal DiscountPercentApplied { get; set; }
    public decimal FeeCharged { get; set; }

    // Set once payment succeeds — the actual enrolled-child record used everywhere else in the app
    public int? ProgramParticipantId { get; set; }
    public ProgramParticipant? ProgramParticipant { get; set; }
}
