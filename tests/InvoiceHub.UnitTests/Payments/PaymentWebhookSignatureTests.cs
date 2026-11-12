using System.Text;
using InvoiceHub.Application.Payments;

namespace InvoiceHub.UnitTests.Payments;

public sealed class PaymentWebhookSignatureTests
{
    private const string Secret = "whsec-test-secret-0123456789-abcdefghij";
    private const long Timestamp = 1_700_000_000;
    private static readonly byte[] Body = Encoding.UTF8.GetBytes("""{"eventId":"evt_1","amount":500.00}""");
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(Timestamp);
    private static readonly TimeSpan Tolerance = TimeSpan.FromMinutes(5);

    // Worked out independently with Python: hmac.new(secret, b"1700000000." + body, sha256).hexdigest()
    private const string KnownSignature = "sha256=fe9b8313f32bef5240a10ef47ca1e159a7b216afd91662ba0d04fe2ce901230e";

    private static SignatureCheck Verify(string? timestamp, string? signature, byte[]? body = null, DateTimeOffset? now = null, string secret = Secret) =>
        PaymentWebhookSignature.Verify(secret, timestamp, signature, body ?? Body, now ?? Now, Tolerance);

    [Fact]
    public void Sign_matches_a_value_computed_independently()
    {
        Assert.Equal(KnownSignature, PaymentWebhookSignature.Sign(Secret, Timestamp, Body));
    }

    [Fact]
    public void A_correct_signature_is_valid()
    {
        Assert.Equal(SignatureCheck.Valid, Verify(Timestamp.ToString(), KnownSignature));
    }

    [Fact]
    public void A_changed_body_is_rejected()
    {
        var tampered = Encoding.UTF8.GetBytes("""{"eventId":"evt_1","amount":5000.00}""");

        Assert.Equal(SignatureCheck.InvalidSignature, Verify(Timestamp.ToString(), KnownSignature, tampered));
    }

    [Fact]
    public void A_different_secret_is_rejected()
    {
        Assert.Equal(SignatureCheck.InvalidSignature, Verify(Timestamp.ToString(), KnownSignature, secret: Secret + "x"));
    }

    [Fact]
    public void A_changed_timestamp_is_rejected_because_it_is_part_of_the_signed_text()
    {
        Assert.Equal(SignatureCheck.InvalidSignature, Verify((Timestamp + 1).ToString(), KnownSignature));
    }

    [Theory]
    [InlineData(null, "sha256=abc")]
    [InlineData("", "sha256=abc")]
    [InlineData("1700000000", null)]
    [InlineData("1700000000", " ")]
    public void Missing_headers_are_reported(string? timestamp, string? signature)
    {
        Assert.Equal(SignatureCheck.MissingHeader, Verify(timestamp, signature));
    }

    [Theory]
    [InlineData("yesterday")]
    [InlineData("-5")]
    [InlineData("1.5")]
    public void A_timestamp_that_is_not_whole_seconds_is_rejected(string timestamp)
    {
        Assert.Equal(SignatureCheck.MalformedTimestamp, Verify(timestamp, KnownSignature));
    }

    [Fact]
    public void A_request_older_than_the_tolerance_is_rejected_even_with_a_correct_signature()
    {
        var late = Now.AddMinutes(5).AddSeconds(1);

        Assert.Equal(SignatureCheck.Expired, Verify(Timestamp.ToString(), KnownSignature, now: late));
    }

    [Fact]
    public void A_request_from_too_far_in_the_future_is_rejected()
    {
        var early = Now.AddMinutes(-5).AddSeconds(-1);

        Assert.Equal(SignatureCheck.Expired, Verify(Timestamp.ToString(), KnownSignature, now: early));
    }

    [Fact]
    public void A_request_right_at_the_edge_of_the_window_is_accepted()
    {
        Assert.Equal(SignatureCheck.Valid, Verify(Timestamp.ToString(), KnownSignature, now: Now.AddMinutes(5)));
    }

    [Theory]
    [InlineData("fe9b8313f32bef5240a10ef47ca1e159a7b216afd91662ba0d04fe2ce901230e")] // no prefix
    [InlineData("sha1=fe9b8313f32bef5240a10ef47ca1e159a7b216afd91662ba0d04fe2ce901230e")]
    [InlineData("sha256=fe9b")]                                                         // too short
    [InlineData("sha256=zz9b8313f32bef5240a10ef47ca1e159a7b216afd91662ba0d04fe2ce901230e")] // not hex
    [InlineData("sha256=")]
    public void A_malformed_signature_is_rejected_without_throwing(string signature)
    {
        Assert.Equal(SignatureCheck.InvalidSignature, Verify(Timestamp.ToString(), signature));
    }

    [Fact]
    public void Upper_case_hex_is_accepted()
    {
        var upper = "sha256=" + KnownSignature["sha256=".Length..].ToUpperInvariant();

        Assert.Equal(SignatureCheck.Valid, Verify(Timestamp.ToString(), upper));
    }
}
