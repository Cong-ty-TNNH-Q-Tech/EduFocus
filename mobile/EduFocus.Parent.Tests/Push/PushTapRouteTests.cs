using EduFocus.Parent.Core.Push;

namespace EduFocus.Parent.Tests.Push;

public sealed class PushTapRouteTests
{
    [Theory]
    [InlineData("daily_summary")]
    [InlineData("attention_alert")]
    public void Report_types_open_reports_with_date(string type)
    {
        var created = PushTapRoute.TryCreate(type, "2026-11-17", out var route);

        Assert.True(created);
        Assert.Equal("//reports?date=2026-11-17", route);
    }

    [Theory]
    [InlineData("daily_summary", null)]
    [InlineData("attention_alert", "17/11/2026")]
    [InlineData("daily_summary", "2026-13-01")]
    public void Report_types_still_open_reports_without_a_valid_date(string type, string? date)
    {
        var created = PushTapRoute.TryCreate(type, date, out var route);

        Assert.True(created);
        Assert.Equal("//reports", route);
    }

    [Theory]
    [InlineData("protection_off")]
    [InlineData("security_alert")]
    public void Alert_types_open_notifications(string type)
    {
        var created = PushTapRoute.TryCreate(type, "2026-11-17", out var route);

        Assert.True(created);
        Assert.Equal("//notifications", route);
    }

    [Theory]
    [InlineData("unknown_type")]
    [InlineData("")]
    [InlineData(null)]
    public void Unknown_types_are_ignored(string? type)
    {
        Assert.False(PushTapRoute.TryCreate(type, "2026-11-17", out var route));
        Assert.Null(route);
    }
}
