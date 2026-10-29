using EduFocus.Domain.Activity;
using EduFocus.Domain.ValueObjects;
using FluentAssertions;

namespace EduFocus.Domain.Tests.Activity;

public class OnlineClassDetectorTests
{
    [Theory]
    [InlineData("meet.google.com")]
    [InlineData("teams.microsoft.com")]
    [InlineData("zoom.us")]
    [InlineData("us02web.zoom.us")]
    public void Class_domains_and_subdomains_are_detected(string value)
    {
        OnlineClassDetector
            .IsOnlineClass(DomainName.Parse(value), processName: null, windowTitle: null)
            .Should()
            .BeTrue();
    }

    [Theory]
    [InlineData("ms-teams")]
    [InlineData("Teams")]
    [InlineData("zoom")]
    [InlineData("zoom.exe")]
    [InlineData(@"C:\Program Files\Microsoft Teams\ms-teams.exe")]
    [InlineData(@"""C:\Users\Student\AppData\Local\Zoom\bin\Zoom.exe""")]
    public void Class_processes_are_detected_across_name_changes(string processName)
    {
        OnlineClassDetector
            .IsOnlineClass(domain: null, processName, windowTitle: null)
            .Should()
            .BeTrue();
    }

    [Theory]
    [InlineData("Google Meet - Physics")]
    [InlineData("Google Meet – Lớp Vật lý")]
    [InlineData("Meet - abc-defg-hij")]
    [InlineData("Meet – Toán 10A")]
    [InlineData("Meet: Tiếng Anh")]
    [InlineData("Meet")]
    public void Meet_window_titles_are_detected(string windowTitle)
    {
        OnlineClassDetector
            .IsOnlineClass(domain: null, processName: "chrome", windowTitle)
            .Should()
            .BeTrue();
    }

    [Theory]
    [InlineData("Meeting notes - Google Docs")]
    [InlineData("Meet the team")]
    [InlineData("Weekly meeting")]
    [InlineData("Microsoft Teams")]
    public void Generic_titles_are_not_class_indicators(string windowTitle)
    {
        OnlineClassDetector
            .IsOnlineClass(domain: null, processName: "chrome", windowTitle)
            .Should()
            .BeFalse();
    }

    [Theory]
    [InlineData("discord")]
    [InlineData("slack")]
    [InlineData("Zalo")]
    [InlineData("Messenger")]
    [InlineData("zoominfo")]
    public void Chat_processes_are_not_detected(string processName)
    {
        OnlineClassDetector
            .IsOnlineClass(domain: null, processName, windowTitle: null)
            .Should()
            .BeFalse();
    }

    [Fact]
    public void Unrelated_activity_is_not_an_online_class()
    {
        OnlineClassDetector
            .IsOnlineClass(DomainName.Parse("classroom.google.com"), "chrome", "Homework")
            .Should()
            .BeFalse();

        OnlineClassDetector
            .IsOnlineClass(domain: null, processName: null, windowTitle: null)
            .Should()
            .BeFalse();
    }
}
