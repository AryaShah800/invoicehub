using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace InvoiceHub.Application.Payments;

public enum SignatureCheck
{
    Valid,
    MissingHeader,
    MalformedTimestamp,
    Expired,
    InvalidSignature,
}

/// <summary>
/// HMAC-SHA256 signing for the payment webhook. The provider signs the string "{timestamp}.{raw body}" with a
/// shared secret and sends the result as "sha256=&lt;hex&gt;" together with the timestamp (Unix seconds).
/// Signing the timestamp too means a captured request cannot be replayed later: it is rejected once the
/// timestamp is outside the tolerance window.
/// </summary>
public static class PaymentWebhookSignature
{
    public const string SignatureHeader = "X-Payment-Signature";
    public const string TimestampHeader = "X-Payment-Timestamp";

    private const string Prefix = "sha256=";
    private const int MacHexLength = 64;

    /// <summary>What the provider side would send as the signature header. Used by tests and local tooling.</summary>
    public static string Sign(string secret, long unixSeconds, ReadOnlySpan<byte> body) =>
        Prefix + Convert.ToHexString(ComputeMac(secret, unixSeconds, body)).ToLowerInvariant();

    public static SignatureCheck Verify(
        string secret,
        string? timestampHeader,
        string? signatureHeader,
        ReadOnlySpan<byte> body,
        DateTimeOffset now,
        TimeSpan tolerance)
    {
        if (string.IsNullOrWhiteSpace(timestampHeader) || string.IsNullOrWhiteSpace(signatureHeader))
            return SignatureCheck.MissingHeader;

        if (!long.TryParse(timestampHeader, NumberStyles.None, CultureInfo.InvariantCulture, out var timestamp))
            return SignatureCheck.MalformedTimestamp;

        if (Math.Abs(now.ToUnixTimeSeconds() - timestamp) > tolerance.TotalSeconds)
            return SignatureCheck.Expired;

        if (!signatureHeader.StartsWith(Prefix, StringComparison.Ordinal) ||
            signatureHeader.Length != Prefix.Length + MacHexLength)
            return SignatureCheck.InvalidSignature;

        byte[] provided;
        try
        {
            provided = Convert.FromHexString(signatureHeader.AsSpan(Prefix.Length));
        }
        catch (FormatException)
        {
            return SignatureCheck.InvalidSignature;
        }

        var expected = ComputeMac(secret, timestamp, body);

        // Constant-time comparison, so the response time does not reveal how much of a guess was right.
        return CryptographicOperations.FixedTimeEquals(expected, provided)
            ? SignatureCheck.Valid
            : SignatureCheck.InvalidSignature;
    }

    private static byte[] ComputeMac(string secret, long unixSeconds, ReadOnlySpan<byte> body)
    {
        var prefix = Encoding.UTF8.GetBytes(unixSeconds.ToString(CultureInfo.InvariantCulture) + ".");
        var signed = new byte[prefix.Length + body.Length];
        prefix.CopyTo(signed, 0);
        body.CopyTo(signed.AsSpan(prefix.Length));
        return HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), signed);
    }
}
