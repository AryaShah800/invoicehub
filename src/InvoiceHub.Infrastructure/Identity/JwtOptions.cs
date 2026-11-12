using Microsoft.Extensions.Configuration;

namespace InvoiceHub.Infrastructure.Identity;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";
    public const int MinSigningKeyLength = 32;

    public string Issuer { get; init; } = "";
    public string Audience { get; init; } = "";

    /// <summary>HMAC key. Never committed: supplied by user-secrets or an environment variable.</summary>
    public string SigningKey { get; init; } = "";

    public int ExpiryMinutes { get; init; } = 60;

    public static JwtOptions Load(IConfiguration configuration)
    {
        var section = configuration.GetSection(SectionName);
        var options = new JwtOptions
        {
            Issuer = section["Issuer"] ?? "",
            Audience = section["Audience"] ?? "",
            SigningKey = section["SigningKey"] ?? "",
            ExpiryMinutes = int.TryParse(section["ExpiryMinutes"], out var minutes) ? minutes : 60,
        };
        options.Validate();
        return options;
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Issuer))
            throw new InvalidOperationException("Jwt:Issuer is not configured.");
        if (string.IsNullOrWhiteSpace(Audience))
            throw new InvalidOperationException("Jwt:Audience is not configured.");
        if (SigningKey.Length < MinSigningKeyLength)
            throw new InvalidOperationException(
                $"Jwt:SigningKey must be at least {MinSigningKeyLength} characters. " +
                "Set it with user-secrets or the Jwt__SigningKey environment variable; do not commit it.");
        if (ExpiryMinutes is < 1 or > 1440)
            throw new InvalidOperationException("Jwt:ExpiryMinutes must be between 1 and 1440.");
    }
}
