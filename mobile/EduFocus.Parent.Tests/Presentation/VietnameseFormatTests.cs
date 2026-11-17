using EduFocus.Parent.Core.Presentation;

namespace EduFocus.Parent.Tests.Presentation;

public sealed class VietnameseFormatTests
{
    [Theory]
    [InlineData(0, "0 phút")]
    [InlineData(30, "< 1 phút")]
    [InlineData(60, "1 phút")]
    [InlineData(3540, "59 phút")]
    [InlineData(7200, "2 giờ")]
    [InlineData(8100, "2 giờ 15 phút")]
    [InlineData(36660, "10 giờ 11 phút")]
    public void Duration_uses_hours_and_minutes(long seconds, string expected) =>
        Assert.Equal(expected, VietnameseFormat.Duration(seconds));

    [Theory]
    [InlineData(82.34, "82,3%")]
    [InlineData(100, "100,0%")]
    [InlineData(42.44, "42,4%")]
    [InlineData(0, "0,0%")]
    public void Percent_has_one_decimal_with_comma(double value, string expected) =>
        Assert.Equal(expected, VietnameseFormat.Percent(value));

    [Fact]
    public void Null_percent_is_a_dash() =>
        Assert.Equal("—", VietnameseFormat.Percent(null));

    [Fact]
    public void Date_long_uses_vietnamese_weekday()
    {
        Assert.Equal("Thứ Ba, 17/11", VietnameseFormat.DateLong(new DateOnly(2026, 11, 17)));
        Assert.Equal("Chủ Nhật, 22/11", VietnameseFormat.DateLong(new DateOnly(2026, 11, 22)));
    }

    [Fact]
    public void Date_time_is_converted_to_family_time_zone()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");
        var utc = new DateTimeOffset(2026, 11, 17, 10, 30, 0, TimeSpan.Zero);

        Assert.Equal("17:30 17/11/2026", VietnameseFormat.DateTimeLocal(utc, zone));
    }
}
