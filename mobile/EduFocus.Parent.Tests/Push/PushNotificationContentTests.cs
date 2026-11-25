using EduFocus.Parent.Core.Push;

namespace EduFocus.Parent.Tests.Push;

public sealed class PushNotificationContentTests
{
    private static readonly string[] ForbiddenFragments =
    [
        "http://",
        "https://",
        "www.",
        ".com",
        ".vn",
        "học sinh",
        "student",
    ];

    [Theory]
    [InlineData(PushNotificationType.DailySummary)]
    [InlineData(PushNotificationType.AttentionAlert)]
    [InlineData(PushNotificationType.ProtectionOff)]
    [InlineData(PushNotificationType.SecurityAlert)]
    [InlineData(PushNotificationType.BlockedAttempt)]
    public void Content_is_generic_and_free_of_sensitive_details(PushNotificationType type)
    {
        var content = PushNotificationContentFactory.Create(type);

        Assert.False(string.IsNullOrWhiteSpace(content.Title));
        Assert.False(string.IsNullOrWhiteSpace(content.Body));
        Assert.DoesNotContain("EduFocus", content.Title);
        Assert.DoesNotContain("EduFocus", content.Body);

        var text = $"{content.Title} {content.Body}";
        foreach (var fragment in ForbiddenFragments)
            Assert.DoesNotContain(fragment, text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Titles_are_distinct_per_type()
    {
        var titles = Enum.GetValues<PushNotificationType>()
            .Select(type => PushNotificationContentFactory.Create(type).Title)
            .ToArray();

        Assert.Equal(titles.Length, titles.Distinct(StringComparer.Ordinal).Count());
    }
}
