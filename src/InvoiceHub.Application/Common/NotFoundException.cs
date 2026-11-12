namespace InvoiceHub.Application.Common;

/// <summary>
/// The requested record does not exist, or belongs to another tenant. Both look the same on purpose.
/// </summary>
public sealed class NotFoundException(string entity, object key)
    : Exception($"{entity} '{key}' was not found.");
