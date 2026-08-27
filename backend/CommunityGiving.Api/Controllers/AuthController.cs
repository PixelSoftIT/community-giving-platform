using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CommunityGiving.Api.Data;
using CommunityGiving.Api.DTOs;
using CommunityGiving.Api.Models;
using CommunityGiving.Api.Services;

namespace CommunityGiving.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly ITokenService _tokenService;
    private readonly ApplicationDbContext _db;
    private readonly IEmailSender _emailSender;
    private readonly ILogger<AuthController> _logger;

    public AuthController(UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager,
        ITokenService tokenService, ApplicationDbContext db, IEmailSender emailSender, ILogger<AuthController> logger)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _tokenService = tokenService;
        _db = db;
        _emailSender = emailSender;
        _logger = logger;
    }

    // Self-service registration for parents/members. New accounts get the "Member" role only —
    // an admin must promote someone to "Admin"/"Treasurer"/"Secretary"; those can never be
    // granted through this public endpoint.
    [HttpPost("register")]
    public async Task<ActionResult<AuthResponseWithRefresh>> Register(RegisterRequest request)
    {
        var existing = await _userManager.FindByEmailAsync(request.Email);
        if (existing != null) return Conflict("An account with this email already exists.");

        var user = new ApplicationUser
        {
            UserName = request.Email,
            Email = request.Email,
            FullName = request.FullName,
            PhoneNumber = request.Phone
        };

        var result = await _userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
            return BadRequest(result.Errors.Select(e => e.Description));

        await _userManager.AddToRoleAsync(user, "Member");

        var (token, expires) = _tokenService.CreateToken(user, new[] { "Member" });
        var refreshToken = await IssueRefreshTokenAsync(user.Id);
        return Ok(new AuthResponseWithRefresh(token, expires, refreshToken, user.FullName, user.Email!, new[] { "Member" }, user.MemberId));
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponseWithRefresh>> Login(LoginRequest request)
    {
        var user = await _userManager.FindByEmailAsync(request.Email);
        if (user is null || !user.IsActive)
            return Unauthorized("Invalid email or password.");

        // CheckPasswordSignInAsync also enforces lockout after repeated failed attempts (see Program.cs config)
        var result = await _signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
        if (!result.Succeeded)
        {
            if (result.IsLockedOut)
                _logger.LogWarning("Account locked out due to repeated failed logins: {Email}", request.Email);
            return Unauthorized("Invalid email or password.");
        }

        var roles = await _userManager.GetRolesAsync(user);
        var (token, expires) = _tokenService.CreateToken(user, roles);
        var refreshToken = await IssueRefreshTokenAsync(user.Id);
        return Ok(new AuthResponseWithRefresh(token, expires, refreshToken, user.FullName, user.Email!, roles.ToArray(), user.MemberId));
    }

    // Exchanges a valid, unexpired refresh token for a new short-lived access token — lets the
    // person stay signed in without re-entering a password every 8 hours, while keeping the
    // bearer token itself short-lived if it's ever leaked. Rotates the refresh token on each use.
    [HttpPost("refresh")]
    public async Task<ActionResult<AuthResponseWithRefresh>> Refresh(RefreshTokenRequest request)
    {
        var hash = HashToken(request.RefreshToken);
        var stored = await _db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash);
        if (stored is null || !stored.IsActive) return Unauthorized("Refresh token is invalid or has expired.");

        var user = await _userManager.FindByIdAsync(stored.UserId);
        if (user is null || !user.IsActive) return Unauthorized();

        stored.RevokedAtUtc = DateTime.UtcNow;
        var newRefreshToken = await IssueRefreshTokenAsync(user.Id);
        stored.ReplacedByTokenHash = HashToken(newRefreshToken);
        await _db.SaveChangesAsync();

        var roles = await _userManager.GetRolesAsync(user);
        var (token, expires) = _tokenService.CreateToken(user, roles);
        return Ok(new AuthResponseWithRefresh(token, expires, newRefreshToken, user.FullName, user.Email!, roles.ToArray(), user.MemberId));
    }

    // Revokes a refresh token (e.g. on sign-out) so it can no longer be exchanged for new access tokens.
    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout(RefreshTokenRequest request)
    {
        var hash = HashToken(request.RefreshToken);
        var stored = await _db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash);
        if (stored != null)
        {
            stored.RevokedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }
        return NoContent();
    }

    // Always returns 200 regardless of whether the email exists, so this endpoint can't be used
    // to enumerate registered accounts. If the account exists, emails a reset link/token.
    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest request)
    {
        var user = await _userManager.FindByEmailAsync(request.Email);
        if (user != null && user.IsActive)
        {
            var resetToken = await _userManager.GeneratePasswordResetTokenAsync(user);
            var encodedToken = Uri.EscapeDataString(resetToken);
            // In production this URL should point at your frontend's reset-password page.
            var body = $"<p>Use this token to reset your password: <code>{encodedToken}</code></p>" +
                       "<p>If you didn't request this, you can safely ignore this email.</p>";
            await _emailSender.SendAsync(user.Email!, user.FullName, "Reset your password", body);
        }
        return Ok(new { message = "If an account exists for that email, a reset link has been sent." });
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request)
    {
        var user = await _userManager.FindByEmailAsync(request.Email);
        if (user is null) return BadRequest("Invalid request.");

        var result = await _userManager.ResetPasswordAsync(user, request.Token, request.NewPassword);
        if (!result.Succeeded) return BadRequest(result.Errors.Select(e => e.Description));

        // Revoke all existing refresh tokens on password reset — a stolen session shouldn't
        // survive the person changing their password.
        var tokens = await _db.RefreshTokens.Where(t => t.UserId == user.Id && t.RevokedAtUtc == null).ToListAsync();
        foreach (var t in tokens) t.RevokedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return NoContent();
    }

    private async Task<string> IssueRefreshTokenAsync(string userId)
    {
        var rawToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        _db.RefreshTokens.Add(new RefreshToken
        {
            UserId = userId,
            TokenHash = HashToken(rawToken),
            ExpiresAtUtc = DateTime.UtcNow.AddDays(30),
            CreatedByIp = HttpContext.Connection.RemoteIpAddress?.ToString()
        });
        await _db.SaveChangesAsync();
        return rawToken;
    }

    private static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)));
}
