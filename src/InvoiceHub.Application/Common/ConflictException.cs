namespace InvoiceHub.Application.Common;

/// <summary>The request conflicts with existing data, for example a duplicate invoice number.</summary>
public sealed class ConflictException(string message) : Exception(message);
