namespace InvoiceHub.Application.Common;

public sealed class BusinessCalendarOptions
{
    public const string Section = "BusinessCalendar";

    /// <summary>Minutes ahead of UTC (330 = India Standard Time).</summary>
    public int UtcOffsetMinutes { get; set; } = 330;
}
