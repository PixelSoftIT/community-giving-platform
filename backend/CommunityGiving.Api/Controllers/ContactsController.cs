using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CommunityGiving.Api.Data;
using CommunityGiving.Api.DTOs;
using CommunityGiving.Api.Models;

namespace CommunityGiving.Api.Controllers;

// Non-member contacts: guest donors, prospective members, vendors — anyone the org wants to
// keep on file for invoicing/notifications without granting them a login or full membership.
[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin,Treasurer,Secretary")]
public class ContactsController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    public ContactsController(ApplicationDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<List<ContactDto>>> GetAll([FromQuery] string? search)
    {
        var query = _db.Contacts.AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(c => c.FirstName.Contains(search) || c.LastName.Contains(search) || (c.Email != null && c.Email.Contains(search)));

        var contacts = await query.OrderBy(c => c.LastName)
            .Select(c => new ContactDto(c.Id, c.FirstName, c.LastName, c.Email, c.Phone, c.Notes, c.CreatedAtUtc))
            .ToListAsync();
        return Ok(contacts);
    }

    [HttpPost]
    public async Task<ActionResult<ContactDto>> Create(CreateContactRequest request)
    {
        var contact = new Contact
        {
            FirstName = request.FirstName,
            LastName = request.LastName,
            Email = request.Email,
            Phone = request.Phone,
            Notes = request.Notes
        };
        _db.Contacts.Add(contact);
        await _db.SaveChangesAsync();
        return Ok(new ContactDto(contact.Id, contact.FirstName, contact.LastName, contact.Email, contact.Phone, contact.Notes, contact.CreatedAtUtc));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, CreateContactRequest request)
    {
        var contact = await _db.Contacts.FindAsync(id);
        if (contact is null) return NotFound();

        contact.FirstName = request.FirstName;
        contact.LastName = request.LastName;
        contact.Email = request.Email;
        contact.Phone = request.Phone;
        contact.Notes = request.Notes;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var contact = await _db.Contacts.FindAsync(id);
        if (contact is null) return NotFound();
        _db.Contacts.Remove(contact);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
