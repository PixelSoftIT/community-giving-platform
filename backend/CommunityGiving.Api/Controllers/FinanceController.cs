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
[Authorize(Roles = "Admin,Treasurer")]
public class FinanceController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly IAuditService _audit;
    public FinanceController(ApplicationDbContext db, IAuditService audit) { _db = db; _audit = audit; }

    // ---------- Expenses ----------
    [HttpGet("expenses")]
    public async Task<ActionResult<List<ExpenseDto>>> GetExpenses([FromQuery] int? projectId, [FromQuery] int? fundId)
    {
        var query = _db.Expenses.Include(e => e.Project).Include(e => e.Fund).AsQueryable();
        if (projectId.HasValue) query = query.Where(e => e.ProjectId == projectId);
        if (fundId.HasValue) query = query.Where(e => e.FundId == fundId);

        var expenses = await query.OrderByDescending(e => e.ExpenseDate)
            .Select(e => new ExpenseDto(e.Id, e.Description, e.Category, e.Amount, e.Vendor, e.ExpenseDate,
                e.Status.ToString(), e.ProjectId, e.Project != null ? e.Project.Name : null,
                e.FundId, e.Fund != null ? e.Fund.Name : null, e.ReceiptUrl))
            .ToListAsync();
        return Ok(expenses);
    }

    [HttpPost("expenses")]
    public async Task<ActionResult<ExpenseDto>> CreateExpense(CreateExpenseRequest request)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown";
        var expense = new Expense
        {
            Description = request.Description,
            Category = request.Category,
            Amount = request.Amount,
            Vendor = request.Vendor,
            ExpenseDate = request.ExpenseDate,
            ProjectId = request.ProjectId,
            FundId = request.FundId,
            ReceiptUrl = request.ReceiptUrl,
            SubmittedByUserId = userId,
            Status = ExpenseStatus.Pending
        };
        _db.Expenses.Add(expense);
        await _db.SaveChangesAsync();
        return Ok(new ExpenseDto(expense.Id, expense.Description, expense.Category, expense.Amount, expense.Vendor,
            expense.ExpenseDate, expense.Status.ToString(), expense.ProjectId, null, expense.FundId, null, expense.ReceiptUrl));
    }

    [HttpPost("expenses/{id:int}/approve")]
    public async Task<IActionResult> ApproveExpense(int id)
    {
        var expense = await _db.Expenses.FindAsync(id);
        if (expense is null) return NotFound();

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown";
        var userEmail = User.FindFirstValue(ClaimTypes.Email) ?? "unknown";
        expense.Status = ExpenseStatus.Approved;
        expense.ApprovedByUserId = userId;
        expense.ApprovedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        await _audit.LogAsync(userId, userEmail, "Expense.Approve", "Expense", id.ToString(), $"Amount={expense.Amount}", HttpContext.Connection.RemoteIpAddress?.ToString());
        return NoContent();
    }

    [HttpPost("expenses/{id:int}/mark-paid")]
    public async Task<IActionResult> MarkExpensePaid(int id)
    {
        var expense = await _db.Expenses.FindAsync(id);
        if (expense is null) return NotFound();
        expense.Status = ExpenseStatus.Paid;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("expenses/{id:int}/reject")]
    public async Task<IActionResult> RejectExpense(int id)
    {
        var expense = await _db.Expenses.FindAsync(id);
        if (expense is null) return NotFound();
        expense.Status = ExpenseStatus.Rejected;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // ---------- Manual income ----------
    [HttpGet("income")]
    public async Task<ActionResult<List<IncomeDto>>> GetIncome([FromQuery] int? projectId, [FromQuery] int? fundId)
    {
        var query = _db.IncomeEntries.Include(i => i.Project).Include(i => i.Fund).AsQueryable();
        if (projectId.HasValue) query = query.Where(i => i.ProjectId == projectId);
        if (fundId.HasValue) query = query.Where(i => i.FundId == fundId);

        var income = await query.OrderByDescending(i => i.IncomeDate)
            .Select(i => new IncomeDto(i.Id, i.Source, i.Description, i.Amount, i.IncomeDate, i.Method.ToString(),
                i.ProjectId, i.Project != null ? i.Project.Name : null, i.FundId, i.Fund != null ? i.Fund.Name : null))
            .ToListAsync();
        return Ok(income);
    }

    [HttpPost("income")]
    public async Task<ActionResult<IncomeDto>> CreateIncome(CreateIncomeRequest request)
    {
        if (!Enum.TryParse<IncomeMethod>(request.Method, true, out var method))
            return BadRequest("Invalid method. Use Cash, Check, BankTransfer, Stripe, Grant, or Other.");

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown";
        var income = new IncomeEntry
        {
            Source = request.Source,
            Description = request.Description,
            Amount = request.Amount,
            IncomeDate = request.IncomeDate,
            Method = method,
            ProjectId = request.ProjectId,
            FundId = request.FundId,
            RecordedByUserId = userId
        };
        _db.IncomeEntries.Add(income);
        await _db.SaveChangesAsync();
        return Ok(new IncomeDto(income.Id, income.Source, income.Description, income.Amount, income.IncomeDate,
            income.Method.ToString(), income.ProjectId, null, income.FundId, null));
    }

    // ---------- Per-project financial summary: donations + manual income − expenses ----------
    [HttpGet("projects/{projectId:int}/summary")]
    public async Task<ActionResult<ProjectFinancialSummaryDto>> GetProjectSummary(int projectId)
    {
        var project = await _db.Projects.Include(p => p.Funds).FirstOrDefaultAsync(p => p.Id == projectId);
        if (project is null) return NotFound();

        var fundIds = project.Funds.Select(f => f.Id).ToList();
        var totalDonations = await _db.Donations
            .Where(d => fundIds.Contains(d.FundId) && d.Status == PaymentStatus.Succeeded)
            .SumAsync(d => (decimal?)d.Amount) ?? 0;

        var totalManualIncome = await _db.IncomeEntries.Where(i => i.ProjectId == projectId).SumAsync(i => (decimal?)i.Amount) ?? 0;
        var totalExpenses = await _db.Expenses.Where(e => e.ProjectId == projectId && e.Status != ExpenseStatus.Rejected).SumAsync(e => (decimal?)e.Amount) ?? 0;

        var totalIncome = totalDonations + totalManualIncome;
        return Ok(new ProjectFinancialSummaryDto(project.Id, project.Name, totalDonations, totalManualIncome,
            totalIncome, totalExpenses, totalIncome - totalExpenses, project.Budget));
    }
}
