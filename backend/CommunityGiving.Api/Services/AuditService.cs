using CommunityGiving.Api.Data;
using CommunityGiving.Api.Models;

namespace CommunityGiving.Api.Services;

public interface IAuditService
{
    Task LogAsync(string userId, string userEmail, string action, string entityType, string? entityId, string? details, string? ipAddress);
}

// Writes an append-only trail of sensitive actions (finance approvals, invoice sends,
// notification blasts, role changes). Kept deliberately simple — a full SIEM pipeline is
// out of scope, but every finance/notification controller calls this so there's always a
// "who did what, when" record to audit later.
public class AuditService : IAuditService
{
    private readonly ApplicationDbContext _db;
    public AuditService(ApplicationDbContext db) => _db = db;

    public async Task LogAsync(string userId, string userEmail, string action, string entityType, string? entityId, string? details, string? ipAddress)
    {
        _db.AuditLogs.Add(new AuditLog
        {
            UserId = userId,
            UserEmail = userEmail,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Details = details,
            IpAddress = ipAddress
        });
        await _db.SaveChangesAsync();
    }
}
