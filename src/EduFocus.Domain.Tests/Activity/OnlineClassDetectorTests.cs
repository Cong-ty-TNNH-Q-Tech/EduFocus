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
    public void Class_domains_are_detected(string value)
    {
        OnlineClassDetector
            .IsOnlineClass(DomainName.Parse(value), processName: null, windowTitle: null)
            .Should()
            .BeTrue();
    }

    [Fact]
    public void Class_subdomain_is_detected()
    {
        OnlineClassDetector
            .IsOnlineClass(DomainName.Parse("meet.google.com"), null, null)
            .Should()
            .BeTrue();
    }

    [Theory]
    [InlineData("ms-teams")]
    [InlineData(@"C:\Program Files\Microsoft Teams\ms-teams.exe")]
    [InlineData("Zoom")]
    public void Class_processes_are_detected(string processName)
    {
        OnlineClassDetector
            .IsOnlineClass(domain: null, processName, windowTitle: null)
            .Should()
            .BeTrue();
    }

    [Fact]
    public void Meet_window_title_is_detected()
    {
        OnlineClassDetector
            .IsOnlineClass(domain: null, processName: null, "Google Meet - Physics")
            .Should()
            .BeTrue();
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
