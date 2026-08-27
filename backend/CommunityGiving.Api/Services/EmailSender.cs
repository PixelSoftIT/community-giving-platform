using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace CommunityGiving.Api.Services;

public interface IEmailSender
{
    Task<bool> SendAsync(string toEmail, string toName, string subject, string htmlBody, byte[]? attachmentBytes = null, string? attachmentFileName = null);
}

// SMTP-based sender using MailKit — works with any provider (SendGrid, Postmark, Amazon SES,
// Gmail/Workspace, or a plain mail server) since they all speak SMTP. Configure under
// "Email:Smtp*" in appsettings/environment variables. If SmtpHost isn't configured, this
// safely no-ops and logs instead of throwing, so the app still runs in a fresh environment.
public class SmtpEmailSender : IEmailSender
{
    private readonly IConfiguration _config;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(IConfiguration config, ILogger<SmtpEmailSender> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task<bool> SendAsync(string toEmail, string toName, string subject, string htmlBody, byte[]? attachmentBytes = null, string? attachmentFileName = null)
    {
        var host = _config["Email:SmtpHost"];
        if (string.IsNullOrWhiteSpace(host))
        {
            _logger.LogWarning("Email not configured (Email:SmtpHost missing) — skipping send to {Email}: {Subject}", toEmail, subject);
            return false;
        }

        try
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(_config["Email:FromName"] ?? "Community Giving Platform", _config["Email:FromAddress"]));
            message.To.Add(new MailboxAddress(toName, toEmail));
            message.Subject = subject;

            var builder = new BodyBuilder { HtmlBody = htmlBody };
            if (attachmentBytes != null && attachmentFileName != null)
                builder.Attachments.Add(attachmentFileName, attachmentBytes, ContentType.Parse("application/pdf"));
            message.Body = builder.ToMessageBody();

            using var client = new SmtpClient();
            var port = int.TryParse(_config["Email:SmtpPort"], out var p) ? p : 587;
            await client.ConnectAsync(host, port, SecureSocketOptions.StartTls);

            var user = _config["Email:SmtpUsername"];
            if (!string.IsNullOrWhiteSpace(user))
                await client.AuthenticateAsync(user, _config["Email:SmtpPassword"]);

            await client.SendAsync(message);
            await client.DisconnectAsync(true);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to {Email}", toEmail);
            return false;
        }
    }
}
