namespace InvoiceHub.Api.Webhooks;

public sealed class PaymentWebhookOptions
{
    public const string SectionName = "PaymentWebhook";
    public const int MinSecretLength = 32;

    /// <summary>
    /// Shared secret the payment provider signs webhooks with. Never committed: supplied by
    /// user-secrets or the PaymentWebhook__Secret environment variable.
    /// </summary>
    public string Secret { get; init; } = "";

    /// <summary>How far the signed timestamp may be from the server clock, in seconds.</summary>
    public int ToleranceSeconds { get; init; } = 300;

    public TimeSpan Tolerance => TimeSpan.FromSeconds(ToleranceSeconds);

    public static PaymentWebhookOptions Load(IConfiguration configuration)
    {
        var section = configuration.GetSection(SectionName);
        var options = new PaymentWebhookOptions
        {
            Secret = section["Secret"] ?? "",
            ToleranceSeconds = int.TryParse(section["ToleranceSeconds"], out var seconds) ? seconds : 300,
        };
        options.Validate();
        return options;
    }

    public void Validate()
    {
        if (Secret.Length < MinSecretLength)
            throw new InvalidOperationException(
                $"PaymentWebhook:Secret must be at least {MinSecretLength} characters. " +
                "Set it with user-secrets or the PaymentWebhook__Secret environment variable; do not commit it.");
        if (ToleranceSeconds is < 30 or > 3600)
            throw new InvalidOperationException("PaymentWebhook:ToleranceSeconds must be between 30 and 3600.");
    }
}
