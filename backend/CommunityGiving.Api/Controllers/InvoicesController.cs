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
public class InvoicesController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly IEmailSender _email;
    private readonly IPdfDocumentService _pdf;
    private readonly IAuditService _audit;

    public InvoicesController(ApplicationDbContext db, IEmailSender email, IPdfDocumentService pdf, IAuditService audit)
    {
        _db = db;
        _email = email;
        _pdf = pdf;
        _audit = audit;
    }

    [HttpGet]
    public async Task<ActionResult<List<InvoiceDto>>> GetAll([FromQuery] string? status)
    {
        var query = _db.Invoices.Include(i => i.LineItems).AsQueryable();
        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<InvoiceStatus>(status, true, out var s))
            query = query.Where(i => i.Status == s);

        var invoices = await query.OrderByDescending(i => i.CreatedAtUtc).ToListAsync();
        return Ok(invoices.Select(ToDto).ToList());
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<InvoiceDto>> GetById(int id)
    {
        var invoice = await _db.Invoices.Include(i => i.LineItems).FirstOrDefaultAsync(i => i.Id == id);
        if (invoice is null) return NotFound();
        return Ok(ToDto(invoice));
    }

    [HttpPost]
    public async Task<ActionResult<InvoiceDto>> Create(CreateInvoiceRequest request)
    {
        if (request.LineItems.Count == 0) return BadRequest("An invoice needs at least one line item.");

        var invoiceNumber = await NextInvoiceNumberAsync();
        var invoice = new Invoice
        {
            InvoiceNumber = invoiceNumber,
            MemberId = request.MemberId,
            ContactId = request.ContactId,
            BillToName = request.BillToName,
            BillToEmail = request.BillToEmail,
            DueDate = request.DueDate,
            Notes = request.Notes,
            FundId = request.FundId,
            ProjectId = request.ProjectId,
            Status = InvoiceStatus.Draft
        };
        foreach (var li in request.LineItems)
            invoice.LineItems.Add(new InvoiceLineItem { Description = li.Description, Quantity = li.Quantity, UnitPrice = li.UnitPrice });

        if (request.CreatePaymentLink)
        {
            var total = request.LineItems.Sum(l => l.Quantity * l.UnitPrice);
            var (linkId, linkUrl) = await CreateStripePaymentLinkAsync(total, invoiceNumber);
            invoice.StripePaymentLinkId = linkId;
            invoice.StripePaymentLinkUrl = linkUrl;
        }

        _db.Invoices.Add(invoice);
        await _db.SaveChangesAsync();

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown";
        var userEmail = User.FindFirstValue(ClaimTypes.Email) ?? "unknown";
        await _audit.LogAsync(userId, userEmail, "Invoice.Create", "Invoice", invoice.Id.ToString(), invoiceNumber, HttpContext.Connection.RemoteIpAddress?.ToString());

        return Ok(ToDto(invoice));
    }

    // Emails the invoice (with PDF attached) to the bill-to address, including the Stripe
    // payment link if one was created — this is the "email payment links to members and
    // non-members" requirement.
    [HttpPost("{id:int}/send")]
    public async Task<IActionResult> SendInvoice(int id)
    {
        var invoice = await _db.Invoices.Include(i => i.LineItems).FirstOrDefaultAsync(i => i.Id == id);
        if (invoice is null) return NotFound();

        var org = await _db.OrganizationSettings.FirstOrDefaultAsync(x => x.Id == 1);
        var pdfBytes = _pdf.GenerateInvoicePdf(new InvoicePdfModel(
            org?.Name ?? "Our Community", org?.Address, invoice.InvoiceNumber, invoice.IssueDate, invoice.DueDate,
            invoice.BillToName, invoice.BillToEmail,
            invoice.LineItems.Select(l => new InvoiceLineItemPdfModel(l.Description, l.Quantity, l.UnitPrice)).ToList(),
            invoice.Notes, invoice.StripePaymentLinkUrl));

        var total = invoice.LineItems.Sum(l => l.Quantity * l.UnitPrice);
        var htmlBody = $@"
            <p>Hello {invoice.BillToName},</p>
            <p>You have a new invoice ({invoice.InvoiceNumber}) for <strong>{total:0.00}</strong>, due {invoice.DueDate:MMMM d, yyyy}.</p>
            {(invoice.StripePaymentLinkUrl != null ? $"<p><a href=\"{invoice.StripePaymentLinkUrl}\">Click here to pay online</a></p>" : "")}
            <p>A copy of the invoice is attached.</p>";

        var sent = await _email.SendAsync(invoice.BillToEmail, invoice.BillToName, $"Invoice {invoice.InvoiceNumber}", htmlBody,
            pdfBytes, $"{invoice.InvoiceNumber}.pdf");

        invoice.Status = InvoiceStatus.Sent;
        invoice.SentAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown";
        var userEmail = User.FindFirstValue(ClaimTypes.Email) ?? "unknown";
        await _audit.LogAsync(userId, userEmail, "Invoice.Send", "Invoice", invoice.Id.ToString(), $"To={invoice.BillToEmail}, EmailSent={sent}", HttpContext.Connection.RemoteIpAddress?.ToString());

        return sent ? NoContent() : StatusCode(502, "Invoice was saved but the email could not be sent — check email provider configuration.");
    }

    [HttpGet("{id:int}/pdf")]
    public async Task<IActionResult> DownloadPdf(int id)
    {
        var invoice = await _db.Invoices.Include(i => i.LineItems).FirstOrDefaultAsync(i => i.Id == id);
        if (invoice is null) return NotFound();

        var org = await _db.OrganizationSettings.FirstOrDefaultAsync(x => x.Id == 1);
        var pdfBytes = _pdf.GenerateInvoicePdf(new InvoicePdfModel(
            org?.Name ?? "Our Community", org?.Address, invoice.InvoiceNumber, invoice.IssueDate, invoice.DueDate,
            invoice.BillToName, invoice.BillToEmail,
            invoice.LineItems.Select(l => new InvoiceLineItemPdfModel(l.Description, l.Quantity, l.UnitPrice)).ToList(),
            invoice.Notes, invoice.StripePaymentLinkUrl));

        return File(pdfBytes, "application/pdf", $"{invoice.InvoiceNumber}.pdf");
    }

    [HttpPost("{id:int}/void")]
    public async Task<IActionResult> Void(int id)
    {
        var invoice = await _db.Invoices.FindAsync(id);
        if (invoice is null) return NotFound();
        invoice.Status = InvoiceStatus.Void;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    private async Task<string> NextInvoiceNumberAsync()
    {
        var year = DateTime.UtcNow.Year;
        var countThisYear = await _db.Invoices.CountAsync(i => i.CreatedAtUtc.Year == year);
        return $"INV-{year}-{(countThisYear + 1):0000}";
    }

    private async Task<(string linkId, string linkUrl)> CreateStripePaymentLinkAsync(decimal amount, string reference)
    {
        var org = await _db.OrganizationSettings.FirstOrDefaultAsync(x => x.Id == 1);
        var currency = org?.Currency ?? "aud";

        var priceService = new Stripe.PriceService();
        var price = await priceService.CreateAsync(new Stripe.PriceCreateOptions
        {
            Currency = currency,
            UnitAmount = (long)(amount * 100),
            ProductData = new Stripe.PriceProductDataOptions { Name = $"Invoice {reference}" }
        });

        var linkService = new Stripe.PaymentLinkService();
        var link = await linkService.CreateAsync(new Stripe.PaymentLinkCreateOptions
        {
            LineItems = new List<Stripe.PaymentLinkLineItemOptions> { new() { Price = price.Id, Quantity = 1 } },
            Metadata = new Dictionary<string, string> { { "invoiceReference", reference } }
        });

        return (link.Id, link.Url);
    }

    private static InvoiceDto ToDto(Invoice i) => new(i.Id, i.InvoiceNumber, i.Status.ToString(), i.BillToName, i.BillToEmail,
        i.IssueDate, i.DueDate, i.LineItems.Sum(l => l.Quantity * l.UnitPrice), i.Notes, i.StripePaymentLinkUrl, i.SentAtUtc,
        i.LineItems.Select(l => new InvoiceLineItemRequest(l.Description, l.Quantity, l.UnitPrice)).ToList());
}
