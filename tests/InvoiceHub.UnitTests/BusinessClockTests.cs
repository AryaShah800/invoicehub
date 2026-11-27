using InvoiceHub.Application;
using InvoiceHub.Application.Abstractions;
using InvoiceHub.Application.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace InvoiceHub.UnitTests;

public sealed class BusinessClockTests
{
    [Fact]
    public void Today_rolls_over_at_midnight_IST()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 26, 19, 0, 0, TimeSpan.Zero));
        var clock = new BusinessClock(time, Options.Create(new BusinessCalendarOptions()));

        Assert.Equal(new DateOnly(2026, 11, 27), clock.Today());
    }

    [Fact]
    public void Today_changes_at_the_business_midnight_boundary()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 26, 18, 29, 59, TimeSpan.Zero));
        var clock = new BusinessClock(time, Options.Create(new BusinessCalendarOptions()));

        Assert.Equal(new DateOnly(2026, 11, 26), clock.Today());
        time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(new DateOnly(2026, 11, 27), clock.Today());
    }

    [Theory]
    [InlineData(null, null, 27)]
    [InlineData(null, "0", 26)]
    [InlineData("330", "0", 27)]
    [InlineData("0", "330", 26)]
    [InlineData("-300", null, 26)]
    public void Registration_uses_the_new_offset_then_legacy_then_IST(string? offset, string? legacyOffset, int day)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["BusinessCalendar:UtcOffsetMinutes"] = offset,
            ["BackgroundJobs:UtcOffsetMinutes"] = legacyOffset,
        }).Build();
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(new DateTimeOffset(2026, 11, 26, 19, 0, 0, TimeSpan.Zero)));
        services.AddBusinessClock(configuration);
        using var provider = services.BuildServiceProvider();

        Assert.Equal(new DateOnly(2026, 11, day), provider.GetRequiredService<IBusinessClock>().Today());
    }
}
