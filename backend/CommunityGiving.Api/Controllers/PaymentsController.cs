using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Stripe;
using CommunityGiving.Api.Data;
using CommunityGiving.Api.DTOs;
using CommunityGiving.Api.Models;
using CommunityGiving.Api.Services;

namespace CommunityGiving.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PaymentsController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly IStripeService _stripe;
    private readonly IEmailSender _emailSender;
    private readonly ISmsSender _smsSender;
    private readonly IPdfDocumentService _pdf;
    private readonly IConfiguration _config;
    private readonly ILogger<PaymentsController> _logger;

    public PaymentsController(ApplicationDbContext db, IStripeService stripe, IEmailSender emailSender,
        ISmsSender smsSender, IPdfDocumentService pdf, IConfiguration config, ILogger<PaymentsController> logger)
    {
        _db = db;
        _stripe = stripe;
        _emailSender = emailSender;
        _smsSender = smsSender;
        _pdf = pdf;
        _config = config;
        _logger = logger;
    }

    // PUBLIC endpoint — this is the core requirement: members AND non-members can pay.
    // If the caller is authenticated we link the donation to their Member record automatically;
    // otherwise we store the donor's name/email directly on the Donation row.
    [HttpPost("create-intent")]
    [AllowAnonymous]
    public async Task<ActionResult<PaymentIntentResponse>> CreateIntent(CreatePaymentIntentRequest request)
    {
        if (request.Amount <= 0) return BadRequest("Amount must be greater than zero.");
        if (string.IsNullOrWhiteSpace(request.DonorEmail)) return BadRequest("Donor email is required for a receipt.");

        var fund = await _db.Funds.FindAsync(request.FundId);
        if (fund is null || !fund.IsActive) return BadRequest("This fund is not currently accepting donations.");

        int? memberId = null;
        if (User.Identity?.IsAuthenticated == true)
        {
            var memberClaim = User.FindFirstValue("memberId");
            if (memberClaim != null) memberId = int.Parse(memberClaim);
        }
        else if (!fund.AllowNonMemberDonations)
        {
            return Forbid("This fund only accepts donations from logged-in members.");
        }

        var donation = new Donation
        {
            Amount = request.Amount,
            Currency = string.IsNullOrWhiteSpace(request.Currency) ? "aud" : request.Currency.ToLower(),
            Status = PaymentStatus.Pending,
            IsAnonymous = request.IsAnonymous,
            Notes = request.Notes,
            FundId = request.FundId,
            MemberId = memberId,
            DonorName = request.DonorName,
            DonorEmail = request.DonorEmail,
            DonorPhone = request.DonorPhone,
            EventId = request.EventId
        };
        _db.Donations.Add(donation);
        await _db.SaveChangesAsync();

        var intent = await _stripe.CreatePaymentIntentAsync(request.Amount, donation.Currency, request.DonorEmail,
            new Dictionary<string, string> { { "donationId", donation.Id.ToString() }, { "fundId", fund.Id.ToString() } });

        donation.StripePaymentIntentId = intent.Id;
        await _db.SaveChangesAsync();

        return Ok(new PaymentIntentResponse(intent.ClientSecret, donation.Id));
    }

    // Admin tool: create a standalone Stripe payment link for a fund and email (and optionally
    // text) it directly to a member or non-member, instead of them needing to visit the donate
    // page themselves. Useful for pledge reminders or "please complete your payment" follow-ups.
    [HttpPost("email-payment-link")]
    [Authorize(Roles = "Admin,Treasurer")]
    public async Task<IActionResult> EmailPaymentLink(EmailPaymentLinkRequest request)
    {
        var fund = await _db.Funds.FindAsync(request.FundId);
        if (fund is null) return NotFound("Fund not found.");

        var org = await _db.OrganizationSettings.FirstOrDefaultAsync(x => x.Id == 1);
        var currency = org?.Currency ?? "aud";

        var priceService = new PriceService();
        var price = await priceService.CreateAsync(new PriceCreateOptions
        {
            Currency = currency,
            UnitAmount = (long)(request.Amount * 100),
            ProductData = new PriceProductDataOptions { Name = fund.Name }
        });

        var linkService = new PaymentLinkService();
        var link = await linkService.CreateAsync(new PaymentLinkCreateOptions
        {
            LineItems = new List<PaymentLinkLineItemOptions> { new() { Price = price.Id, Quantity = 1 } },
            Metadata = new Dictionary<string, string> { { "fundId", fund.Id.ToString() }, { "recipientEmail", request.RecipientEmail } }
        });

        var body = $"<p>Hello {request.RecipientName},</p><p>You've been sent a payment link for <strong>{fund.Name}</strong> (${request.Amount:0.00}).</p><p><a href=\"{link.Url}\">Click here to pay online</a></p>";
        var emailed = await _emailSender.SendAsync(request.RecipientEmail, request.RecipientName, $"Payment link: {fund.Name}", body);

        var texted = false;
        if (!string.IsNullOrWhiteSpace(request.RecipientPhone))
            texted = await _smsSender.SendAsync(request.RecipientPhone, $"Payment link for {fund.Name} (${request.Amount:0.00}): {link.Url}");

        return Ok(new { paymentLinkUrl = link.Url, emailed, texted });
    }

    // Stripe calls this server-to-server after payment succeeds/fails — the single source of truth
    // for marking a donation as paid. Never trust the frontend alone to confirm payment success.
    [HttpPost("webhook")]
    [AllowAnonymous]
    public async Task<IActionResult> Webhook()
    {
        var json = await new StreamReader(Request.Body).ReadToEndAsync();
        var webhookSecret = _config["Stripe:WebhookSecret"]!;

        Stripe.Event stripeEvent;
        try
        {
            stripeEvent = _stripe.ConstructWebhookEvent(json, Request.Headers["Stripe-Signature"]!, webhookSecret);
        }
        catch (StripeException ex)
        {
            _logger.LogWarning(ex, "Stripe webhook signature verification failed.");
            return BadRequest();
        }

        if (stripeEvent.Data.Object is PaymentIntent intent)
        {
            var donation = await _db.Donations.Include(d => d.Fund).FirstOrDefaultAsync(d => d.StripePaymentIntentId == intent.Id);
            if (donation != null)
            {
                if (stripeEvent.Type == "payment_intent.succeeded")
                {
                    donation.Status = PaymentStatus.Succeeded;
                    donation.ReceiptUrl = intent.LatestChargeId != null
                        ? $"https://dashboard.stripe.com/payments/{intent.Id}"
                        : donation.ReceiptUrl;

                    if (donation.EventId.HasValue)
                    {
                        var reg = await _db.EventRegistrations.FirstOrDefaultAsync(r => r.DonationId == donation.Id);
                        if (reg != null) reg.Status = RegistrationStatus.Paid;
                    }

                    await IssueAndEmailReceiptAsync(donation);
                }
                else if (stripeEvent.Type == "payment_intent.payment_failed")
                {
                    donation.Status = PaymentStatus.Failed;
                }
                await _db.SaveChangesAsync();
            }

            // Same payment intent ID space is also used by program registrations (yearly
            // student sign-ups) — check there too, since Stripe doesn't tell us which kind
            // of purchase this was, only that a PaymentIntent with this ID changed status.
            var batch = await _db.ProgramRegistrationBatches
                .Include(b => b.Children).Include(b => b.RegisteredByMember).Include(b => b.ProgramTerm)
                .FirstOrDefaultAsync(b => b.StripePaymentIntentId == intent.Id);
            if (batch != null)
            {
                if (stripeEvent.Type == "payment_intent.succeeded")
                {
                    batch.Status = RegistrationBatchStatus.Paid;
                    batch.PaidAtUtc = DateTime.UtcNow;

                    // Create the actual enrolled-child record for each paid registration, so the
                    // rest of the app (member portal, admin household view) sees them enrolled.
                    foreach (var child in batch.Children)
                    {
                        var participant = new ProgramParticipant
                        {
                            FirstName = child.FirstName,
                            LastName = child.LastName,
                            DateOfBirth = child.DateOfBirth,
                            ProgramGroup = child.ProgramGroup,
                            AllergiesOrNotes = child.AllergiesOrNotes,
                            ParentContactEmail = batch.RegisteredByMember?.Email ?? "",
                            ParentContactPhone = batch.RegisteredByMember?.Phone ?? "",
                            HouseholdId = batch.HouseholdId,
                            IsActive = true
                        };
                        _db.ProgramParticipants.Add(participant);
                        await _db.SaveChangesAsync(); // need the generated Id before linking
                        child.ProgramParticipantId = participant.Id;
                    }

                    if (batch.RegisteredByMember != null)
                    {
                        var childList = string.Join(", ", batch.Children.Select(c => $"{c.FirstName} {c.LastName}"));
                        await _email.SendAsync(batch.RegisteredByMember.Email, batch.RegisteredByMember.FirstName,
                            $"Registration confirmed — {batch.ProgramTerm?.Name}",
                            $"<p>Thank you! Registration is confirmed for: <strong>{childList}</strong>.</p>" +
                            $"<p>Total paid: {batch.TotalAmount:0.00} AUD.</p>");
                    }
                }
                else if (stripeEvent.Type == "payment_intent.payment_failed")
                {
                    batch.Status = RegistrationBatchStatus.Failed;
                }
                await _db.SaveChangesAsync();
            }
        }

        return Ok();
    }

    // Generates a receipt record + PDF and emails it automatically — works identically for
    // members and non-member donors since both are captured directly on the Donation row.
    private async Task IssueAndEmailReceiptAsync(Donation donation)
    {
        var year = DateTime.UtcNow.Year;
        var countThisYear = await _db.Receipts.CountAsync(r => r.IssuedAtUtc.Year == year);
        var receiptNumber = $"RCPT-{year}-{(countThisYear + 1):0000}";

        var org = await _db.OrganizationSettings.FirstOrDefaultAsync(x => x.Id == 1);
        var pdfBytes = _pdf.GenerateReceiptPdf(new ReceiptPdfModel(
            org?.Name ?? "Our Community", org?.Address, receiptNumber, DateTime.UtcNow,
            donation.IsAnonymous ? "Anonymous" : donation.DonorName, donation.DonorEmail,
            donation.Fund!.Name, donation.Amount, donation.Currency, org?.ReceiptFooterText));

        var emailed = await _emailSender.SendAsync(donation.DonorEmail, donation.DonorName,
            $"Your receipt {receiptNumber}",
            "<p>Thank you for your generous contribution. Your receipt is attached.</p>",
            pdfBytes, $"{receiptNumber}.pdf");

        _db.Receipts.Add(new Receipt
        {
            ReceiptNumber = receiptNumber,
            DonationId = donation.Id,
            EmailedSuccessfully = emailed
        });
    }

    // Lets the frontend poll/confirm final status right after the Stripe.js confirmation redirect.
    [HttpGet("{donationId:int}/status")]
    [AllowAnonymous]
    public async Task<ActionResult<DonationDto>> GetStatus(int donationId)
    {
        var d = await _db.Donations.Include(x => x.Fund).FirstOrDefaultAsync(x => x.Id == donationId);
        if (d is null) return NotFound();
        return Ok(new DonationDto(d.Id, d.Amount, d.Currency, d.Status.ToString(), d.CreatedAtUtc,
            d.Fund!.Name, d.IsAnonymous ? "Anonymous" : d.DonorName, d.IsAnonymous, d.ReceiptUrl));
    }
}
