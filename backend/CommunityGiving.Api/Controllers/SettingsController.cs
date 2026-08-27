using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CommunityGiving.Api.Data;
using CommunityGiving.Api.DTOs;
using CommunityGiving.Api.Models;

namespace CommunityGiving.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SettingsController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    public SettingsController(ApplicationDbContext db) => _db = db;

    // PUBLIC — the frontend needs this on first load to render the org's name, type, and
    // vocabulary (e.g. what to call "Programs") before the person has even logged in.
    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<OrganizationSettingsDto>> Get()
    {
        var s = await _db.OrganizationSettings.FirstOrDefaultAsync(x => x.Id == 1);
        // Should always exist — seeded on startup — but fall back defensively.
        s ??= new OrganizationSettings();
        return Ok(ToDto(s));
    }

    [HttpPut]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<OrganizationSettingsDto>> Update(UpdateOrganizationSettingsRequest request)
    {
        if (!Enum.TryParse<OrganizationType>(request.Type, true, out var type))
            return BadRequest("Invalid organization type. Use Temple, Church, Mosque, Synagogue, Ngo, CommunityCenter, or Other.");

        var s = await _db.OrganizationSettings.FirstOrDefaultAsync(x => x.Id == 1);
        if (s is null)
        {
            s = new OrganizationSettings { Id = 1 };
            _db.OrganizationSettings.Add(s);
        }

        s.Name = request.Name;
        s.Type = type;
        s.Tagline = request.Tagline;
        s.ContactEmail = request.ContactEmail;
        s.ContactPhone = request.ContactPhone;
        s.Address = request.Address;
        s.LogoUrl = request.LogoUrl;
        s.Currency = string.IsNullOrWhiteSpace(request.Currency) ? "aud" : request.Currency.ToLower();
        s.ProgramsLabel = request.ProgramsLabel;
        s.ProgramsEnabled = request.ProgramsEnabled;
        s.ReceiptFooterText = request.ReceiptFooterText;

        await _db.SaveChangesAsync();
        return Ok(ToDto(s));
    }

    private static OrganizationSettingsDto ToDto(OrganizationSettings s) =>
        new(s.Name, s.Type.ToString(), s.Tagline, s.ContactEmail, s.ContactPhone, s.Address,
            s.LogoUrl, s.Currency, s.ProgramsLabel, s.ProgramsEnabled);
}
