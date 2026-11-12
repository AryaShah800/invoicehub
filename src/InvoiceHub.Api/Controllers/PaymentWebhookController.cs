using System.Text.Json;
using InvoiceHub.Api.Webhooks;
using InvoiceHub.Application.Payments;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InvoiceHub.Api.Controllers;

/// <summary>The JSON the payment provider sends.</summary>
public sealed record PaymentWebhookPayload(string? EventId, Guid InvoiceId, decimal Amount, string? Currency, string? Reference);

/// <summary>
/// Receives payment events from the payment provider. There is no signed-in user here, so the request is
/// authenticated by its HMAC-SHA256 signature over the raw body instead of a JWT.
/// </summary>
[ApiController]
[Route("api/webhooks/payments")]
[AllowAnonymous]
public sealed class PaymentWebhookController(
    ISender sender,
    PaymentWebhookOptions options,
    TimeProvider timeProvider,
    ILogger<PaymentWebhookController> logger) : ControllerBase
{
    private const int MaxBodyBytes = 64 * 1024;

    /// <summary>
    /// Applies a payment to an invoice. Responses: 200 applied (or already applied, for a retried event),
    /// 400 malformed or invalid payload, 401 missing, stale or wrong signature, 404 unknown invoice,
    /// 409 conflict (event id reused, or the invoice changed at the same moment: retry),
    /// 413 body too large, 422 payment not allowed (draft, cancelled, already paid, or more than is due).
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(RecordPaymentResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> Receive(CancellationToken cancellationToken)
    {
        if (Request.ContentLength > MaxBodyBytes)
            return StatusCode(StatusCodes.Status413PayloadTooLarge);

        // The signature covers the exact bytes that were sent, so read them before anything parses the JSON.
        var body = await ReadBodyAsync(cancellationToken);
        if (body is null)
            return StatusCode(StatusCodes.Status413PayloadTooLarge);

        var check = PaymentWebhookSignature.Verify(
            options.Secret,
            Request.Headers[PaymentWebhookSignature.TimestampHeader].ToString(),
            Request.Headers[PaymentWebhookSignature.SignatureHeader].ToString(),
            body,
            timeProvider.GetUtcNow(),
            options.Tolerance);

        if (check != SignatureCheck.Valid)
        {
            // The caller only gets a bare 401; the reason is for our own logs.
            logger.LogWarning("Rejected a payment webhook: {Reason}.", check);
            return Unauthorized();
        }

        PaymentWebhookPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<PaymentWebhookPayload>(body, JsonOptions);
        }
        catch (JsonException)
        {
            payload = null;
        }

        if (payload is null)
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "The payload is not valid JSON for a payment event.");

        // The signature is the only credential here. If a user token was attached as well, ignore it, so the
        // tenant always comes from the invoice and never from whoever happened to be signed in.
        HttpContext.User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity());

        var result = await sender.Send(
            new RecordPaymentCommand(payload.InvoiceId, payload.EventId ?? "", payload.Amount, payload.Currency ?? "", payload.Reference),
            cancellationToken);
        return Ok(result);
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Reads the body, giving up if it is larger than allowed even without a Content-Length header.</summary>
    private async Task<byte[]?> ReadBodyAsync(CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[4096];
        int read;
        while ((read = await Request.Body.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > MaxBodyBytes)
                return null;
            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }
}
