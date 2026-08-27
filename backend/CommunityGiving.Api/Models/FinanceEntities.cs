namespace CommunityGiving.Api.Models;

public enum InvoiceStatus { Draft, Sent, Paid, Overdue, Void }

// A billable request sent to a member or non-member — dues, an event fee, a pledge
// installment, anything that isn't a spontaneous donation. Can carry a Stripe payment link
// so the recipient can pay online directly from the email.
public class Invoice
{
    public int Id { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty; // e.g. INV-2026-0001
    public InvoiceStatus Status { get; set; } = InvoiceStatus.Draft;

    public int? MemberId { get; set; }
    public Member? Member { get; set; }
    public int? ContactId { get; set; }
    public Contact? Contact { get; set; }

    // Captured directly so an invoice is self-contained even if the Member/Contact record changes later
    public string BillToName { get; set; } = string.Empty;
    public string BillToEmail { get; set; } = string.Empty;

    public DateOnly IssueDate { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow);
    public DateOnly DueDate { get; set; }
    public string? Notes { get; set; }

    public int? FundId { get; set; }
    public Fund? Fund { get; set; }
    public int? ProjectId { get; set; }
    public Project? Project { get; set; }

    public string? StripePaymentLinkUrl { get; set; }
    public string? StripePaymentLinkId { get; set; }
    public int? PaidByDonationId { get; set; } // set once the linked payment succeeds

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? SentAtUtc { get; set; }

    public List<InvoiceLineItem> LineItems { get; set; } = new();
}

public class InvoiceLineItem
{
    public int Id { get; set; }
    public int InvoiceId { get; set; }
    public Invoice? Invoice { get; set; }
    public string Description { get; set; } = string.Empty;
    public int Quantity { get; set; } = 1;
    public decimal UnitPrice { get; set; }
}

// A generated receipt for a completed payment (donation or paid invoice) — one receipt
// number per payment, emailed automatically and re-downloadable as a PDF on demand.
public class Receipt
{
    public int Id { get; set; }
    public string ReceiptNumber { get; set; } = string.Empty; // e.g. RCPT-2026-0001
    public int? DonationId { get; set; }
    public Donation? Donation { get; set; }
    public int? InvoiceId { get; set; }
    public Invoice? Invoice { get; set; }
    public DateTime IssuedAtUtc { get; set; } = DateTime.UtcNow;
    public bool EmailedSuccessfully { get; set; }
}

public enum ExpenseStatus { Pending, Approved, Paid, Rejected }

// An outgoing cost tracked against a Project and/or Fund — lets a treasurer see
// income vs. expense per project, not just per fund.
public class Expense
{
    public int Id { get; set; }
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = "General"; // Supplies, Utilities, Salaries, Maintenance, Programs, Other
    public decimal Amount { get; set; }
    public string? Vendor { get; set; }
    public DateOnly ExpenseDate { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow);
    public ExpenseStatus Status { get; set; } = ExpenseStatus.Pending;
    public string? ReceiptUrl { get; set; } // link to a scanned receipt/invoice image, if uploaded elsewhere

    public int? ProjectId { get; set; }
    public Project? Project { get; set; }
    public int? FundId { get; set; }
    public Fund? Fund { get; set; }

    public string SubmittedByUserId { get; set; } = string.Empty;
    public string? ApprovedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ApprovedAtUtc { get; set; }
}

public enum IncomeMethod { Cash, Check, BankTransfer, Stripe, Grant, Other }

// Manually recorded income that didn't come through the Stripe donation flow —
// a cash collection, a check, a grant award — still trackable per project/fund.
public class IncomeEntry
{
    public int Id { get; set; }
    public string Source { get; set; } = string.Empty; // e.g. "Cash collection - Sunday service"
    public string? Description { get; set; }
    public decimal Amount { get; set; }
    public DateOnly IncomeDate { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow);
    public IncomeMethod Method { get; set; } = IncomeMethod.Cash;

    public int? ProjectId { get; set; }
    public Project? Project { get; set; }
    public int? FundId { get; set; }
    public Fund? Fund { get; set; }

    public string RecordedByUserId { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
