namespace CommunityGiving.Api.Models;

// Supports short-lived access tokens (8h, as configured) backed by a longer-lived refresh
// token so a stolen access token expires quickly, while the person doesn't have to log in
// constantly. Refresh tokens are stored hashed and can be revoked individually.
public class RefreshToken
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string TokenHash { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public string? ReplacedByTokenHash { get; set; }
    public string? CreatedByIp { get; set; }

    public bool IsActive => RevokedAtUtc is null && DateTime.UtcNow < ExpiresAtUtc;
}

// A tamper-evident trail of sensitive admin actions (finance, invoicing, notifications,
// role changes) — important for any organization handling money and member data.
public class AuditLog
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string UserEmail { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;      // e.g. "Expense.Approve", "Invoice.Send"
    public string EntityType { get; set; } = string.Empty;  // e.g. "Expense"
    public string? EntityId { get; set; }
    public string? Details { get; set; }
    public string? IpAddress { get; set; }
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
}
