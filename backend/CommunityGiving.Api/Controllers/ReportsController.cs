using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CommunityGiving.Api.Data;
using CommunityGiving.Api.Models;

namespace CommunityGiving.Api.Controllers;

[ApiController]
[Route("api/reports")]
[Authorize(Roles = "Admin,Treasurer,ProgramCoordinator")]
public class ReportsController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    public ReportsController(ApplicationDbContext db) => _db = db;

    // A funding report across all donations — optionally filtered by date range, fund, or
    // project — for a treasurer to reconcile against bank statements or file with the org's
    // committee. CSV opens directly in Excel/Sheets, no extra software needed.
    [HttpGet("funding.csv")]
    [Authorize(Roles = "Admin,Treasurer")]
    public async Task<IActionResult> FundingReport([FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] int? fundId, [FromQuery] int? projectId)
    {
        var query = _db.Donations.Include(d => d.Fund).ThenInclude(f => f!.Project).AsQueryable();
        if (from.HasValue) query = query.Where(d => d.CreatedAtUtc >= from);
        if (to.HasValue) query = query.Where(d => d.CreatedAtUtc <= to);
        if (fundId.HasValue) query = query.Where(d => d.FundId == fundId);
        if (projectId.HasValue) query = query.Where(d => d.Fund!.ProjectId == projectId);

        var donations = await query.OrderBy(d => d.CreatedAtUtc).ToListAsync();

        var sb = new StringBuilder();
        sb.AppendLine(CsvRow("Date", "Donor Name", "Donor Email", "Fund", "Project", "Amount", "Currency", "Status", "Anonymous", "Recurring"));
        foreach (var d in donations)
        {
            sb.AppendLine(CsvRow(
                d.CreatedAtUtc.ToString("yyyy-MM-dd HH:mm"),
                d.IsAnonymous ? "Anonymous" : d.DonorName,
                d.IsAnonymous ? "" : d.DonorEmail,
                d.Fund?.Name ?? "",
                d.Fund?.Project?.Name ?? "",
                d.Amount.ToString("0.00"),
                d.Currency.ToUpper(),
                d.Status.ToString(),
                d.IsAnonymous ? "Yes" : "No",
                d.IsRecurring ? "Yes" : "No"));
        }

        var totalSucceeded = donations.Where(d => d.Status == PaymentStatus.Succeeded).Sum(d => d.Amount);
        sb.AppendLine();
        sb.AppendLine(CsvRow("", "", "", "", "Total (succeeded only)", totalSucceeded.ToString("0.00")));

        return CsvFile(sb.ToString(), $"funding-report-{DateTime.UtcNow:yyyyMMdd}.csv");
    }

    // Every online student/program registration and its payment status — for reconciling
    // program fee income and for a coordinator's own enrollment records.
    [HttpGet("program-registrations.csv")]
    public async Task<IActionResult> ProgramRegistrationsReport([FromQuery] int? termId)
    {
        var query = _db.ProgramRegistrationBatches
            .Include(b => b.ProgramTerm).Include(b => b.Household).Include(b => b.RegisteredByMember)
            .Include(b => b.Children)
            .AsQueryable();
        if (termId.HasValue) query = query.Where(b => b.ProgramTermId == termId);

        var batches = await query.OrderBy(b => b.CreatedAtUtc).ToListAsync();

        var sb = new StringBuilder();
        sb.AppendLine(CsvRow("Registration Date", "Program Term", "Household", "Registered By", "Parent Email",
            "Child Name", "Date of Birth", "Level", "Position in Family", "Base Fee", "Discount %", "Fee Charged",
            "Batch Status", "Paid Date"));

        foreach (var b in batches)
        {
            foreach (var c in b.Children.OrderBy(c => c.ChildPositionInFamily))
            {
                sb.AppendLine(CsvRow(
                    b.CreatedAtUtc.ToString("yyyy-MM-dd HH:mm"),
                    b.ProgramTerm?.Name ?? "",
                    b.Household?.HouseholdName ?? "",
                    b.RegisteredByMember != null ? $"{b.RegisteredByMember.FirstName} {b.RegisteredByMember.LastName}" : "",
                    b.RegisteredByMember?.Email ?? "",
                    $"{c.FirstName} {c.LastName}",
                    c.DateOfBirth.ToString("yyyy-MM-dd"),
                    c.ProgramGroup,
                    c.ChildPositionInFamily.ToString(),
                    c.BaseFee.ToString("0.00"),
                    c.DiscountPercentApplied.ToString("0.##"),
                    c.FeeCharged.ToString("0.00"),
                    b.Status.ToString(),
                    b.PaidAtUtc?.ToString("yyyy-MM-dd") ?? ""));
            }
        }

        var totalCollected = batches.Where(b => b.Status == RegistrationBatchStatus.Paid).Sum(b => b.TotalAmount);
        var totalChildren = batches.Where(b => b.Status == RegistrationBatchStatus.Paid).Sum(b => b.Children.Count);
        sb.AppendLine();
        sb.AppendLine(CsvRow("", "", "", "", "", "", "", "", "", "", "Children (paid)", totalChildren.ToString(), "Total collected", totalCollected.ToString("0.00")));

        return CsvFile(sb.ToString(), $"program-registrations-report-{DateTime.UtcNow:yyyyMMdd}.csv");
    }

    private IActionResult CsvFile(string content, string fileName) =>
        File(Encoding.UTF8.GetBytes(content), "text/csv", fileName);

    // Minimal RFC 4180-style CSV escaping: wrap any field containing a comma, quote, or
    // newline in quotes, and double up any internal quotes.
    private static string CsvRow(params string[] fields) => string.Join(",", fields.Select(EscapeCsvField));

    private static string EscapeCsvField(string field)
    {
        if (string.IsNullOrEmpty(field)) return "";
        if (field.Contains(',') || field.Contains('"') || field.Contains('\n') || field.Contains('\r'))
            return "\"" + field.Replace("\"", "\"\"") + "\"";
        return field;
    }
}
