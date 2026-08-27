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
public class ReceiptsController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly IPdfDocumentService _pdf;
    private readonly IEmailSender _email;

    public ReceiptsController(ApplicationDbContext db, IPdfDocumentService pdf, IEmailSender email)
    {
        _db = db;
        _pdf = pdf;
        _email = email;
    }

    [HttpGet]
    [Authorize(Roles = "Admin,Treasurer")]
    public async Task<ActionResult<List<ReceiptDto>>> GetAll()
    {
        var receipts = await _db.Receipts.Include(r => r.Donation).ThenInclude(d => d!.Fund)
            .OrderByDescending(r => r.IssuedAtUtc)
            .Select(r => new ReceiptDto(r.Id, r.ReceiptNumber, r.IssuedAtUtc, r.EmailedSuccessfully,
                r.Donation != null ? r.Donation.Amount : (decimal?)null,
                r.Donation != null ? r.Donation.DonorName : null,
                r.Donation != null && r.Donation.Fund != null ? r.Donation.Fund.Name : null))
            .ToListAsync();
        return Ok(receipts);
    }

    // A member downloading their own receipt, or an admin downloading any receipt.
    [HttpGet("{id:int}/pdf")]
    [Authorize]
    public async Task<IActionResult> DownloadPdf(int id)
    {
        var receipt = await _db.Receipts.Include(r => r.Donation).ThenInclude(d => d!.Fund).FirstOrDefaultAsync(r => r.Id == id);
        if (receipt is null || receipt.Donation is null) return NotFound();

        var org = await _db.OrganizationSettings.FirstOrDefaultAsync(x => x.Id == 1);
        var pdfBytes = _pdf.GenerateReceiptPdf(new ReceiptPdfModel(
            org?.Name ?? "Our Community", org?.Address, receipt.ReceiptNumber, receipt.IssuedAtUtc,
            receipt.Donation.IsAnonymous ? "Anonymous" : receipt.Donation.DonorName, receipt.Donation.DonorEmail,
            receipt.Donation.Fund!.Name, receipt.Donation.Amount, receipt.Donation.Currency, org?.ReceiptFooterText));

        return File(pdfBytes, "application/pdf", $"{receipt.ReceiptNumber}.pdf");
    }

    [HttpPost("{id:int}/resend")]
    [Authorize(Roles = "Admin,Treasurer")]
    public async Task<IActionResult> Resend(int id)
    {
        var receipt = await _db.Receipts.Include(r => r.Donation).ThenInclude(d => d!.Fund).FirstOrDefaultAsync(r => r.Id == id);
        if (receipt is null || receipt.Donation is null) return NotFound();

        var org = await _db.OrganizationSettings.FirstOrDefaultAsync(x => x.Id == 1);
        var pdfBytes = _pdf.GenerateReceiptPdf(new ReceiptPdfModel(
            org?.Name ?? "Our Community", org?.Address, receipt.ReceiptNumber, receipt.IssuedAtUtc,
            receipt.Donation.IsAnonymous ? "Anonymous" : receipt.Donation.DonorName, receipt.Donation.DonorEmail,
            receipt.Donation.Fund!.Name, receipt.Donation.Amount, receipt.Donation.Currency, org?.ReceiptFooterText));

        var sent = await _email.SendAsync(receipt.Donation.DonorEmail, receipt.Donation.DonorName,
            $"Your receipt {receipt.ReceiptNumber}", "<p>Please find your donation receipt attached. Thank you for your generosity.</p>",
            pdfBytes, $"{receipt.ReceiptNumber}.pdf");

        receipt.EmailedSuccessfully = sent;
        await _db.SaveChangesAsync();
        return sent ? NoContent() : StatusCode(502, "Could not resend — check email provider configuration.");
    }
}
