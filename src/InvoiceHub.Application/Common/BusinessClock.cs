using InvoiceHub.Application.Abstractions;
using Microsoft.Extensions.Options;

namespace InvoiceHub.Application.Common;

public sealed class BusinessClock(TimeProvider time, IOptions<BusinessCalendarOptions> options) : IBusinessClock
{
    public DateOnly Today() =>
        DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime.AddMinutes(options.Value.UtcOffsetMinutes));
}
