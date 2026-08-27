using Stripe;

namespace CommunityGiving.Api.Services;

public interface IStripeService
{
    Task<PaymentIntent> CreatePaymentIntentAsync(decimal amount, string currency, string donorEmail, Dictionary<string, string> metadata);
    Task<PaymentIntent> GetPaymentIntentAsync(string paymentIntentId);
    Event ConstructWebhookEvent(string json, string signatureHeader, string webhookSecret);
}

// All Stripe API calls go through here so there's one place to swap providers or add logging.
public class StripeService : IStripeService
{
    public StripeService(IConfiguration config)
    {
        // Secret key set once at startup (see Program.cs) — never expose this to the frontend.
        StripeConfiguration.ApiKey = config["Stripe:SecretKey"];
    }

    public async Task<PaymentIntent> CreatePaymentIntentAsync(decimal amount, string currency, string donorEmail, Dictionary<string, string> metadata)
    {
        var service = new PaymentIntentService();
        var options = new PaymentIntentCreateOptions
        {
            // Stripe expects the smallest currency unit (cents)
            Amount = (long)(amount * 100),
            Currency = currency,
            ReceiptEmail = donorEmail,
            AutomaticPaymentMethods = new PaymentIntentAutomaticPaymentMethodsOptions { Enabled = true },
            Metadata = metadata
        };
        return await service.CreateAsync(options);
    }

    public async Task<PaymentIntent> GetPaymentIntentAsync(string paymentIntentId)
    {
        var service = new PaymentIntentService();
        return await service.GetAsync(paymentIntentId);
    }

    public Event ConstructWebhookEvent(string json, string signatureHeader, string webhookSecret)
        => EventUtility.ConstructEvent(json, signatureHeader, webhookSecret);
}
