using Twilio;
using Twilio.Rest.Api.V2010.Account;
using Twilio.Types;

namespace CommunityGiving.Api.Services;

public interface ISmsSender
{
    Task<bool> SendAsync(string toPhone, string body);
}

// Twilio-based SMS sender. If Sms:TwilioAccountSid isn't configured, this safely no-ops and
// logs instead of throwing — lets the app run before an org has set up SMS. Swap this
// implementation out for another provider (e.g. Vonage, AWS SNS) by re-implementing ISmsSender.
public class TwilioSmsSender : ISmsSender
{
    private readonly IConfiguration _config;
    private readonly ILogger<TwilioSmsSender> _logger;
    private static bool _initialized;

    public TwilioSmsSender(IConfiguration config, ILogger<TwilioSmsSender> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task<bool> SendAsync(string toPhone, string body)
    {
        var sid = _config["Sms:TwilioAccountSid"];
        var token = _config["Sms:TwilioAuthToken"];
        var fromNumber = _config["Sms:TwilioFromNumber"];

        if (string.IsNullOrWhiteSpace(sid) || string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(fromNumber))
        {
            _logger.LogWarning("SMS not configured (Sms:Twilio* missing) — skipping send to {Phone}", toPhone);
            return false;
        }

        try
        {
            if (!_initialized)
            {
                TwilioClient.Init(sid, token);
                _initialized = true;
            }

            await MessageResource.CreateAsync(
                body: body,
                from: new PhoneNumber(fromNumber),
                to: new PhoneNumber(toPhone));
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send SMS to {Phone}", toPhone);
            return false;
        }
    }
}
