using EduFocus.Domain.Attention;
using FluentAssertions;
using Xunit;

namespace EduFocus.Domain.Tests.Attention;

public class AttentionStateWindowTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static AttentionClassification Classification(
        RawAttentionState state,
        float confidence = 0.8f,
        string reason = "test") =>
        new(state, confidence, reason);

    [Fact]
    public void Observe_BeforeMinimumSamples_ReturnsNull()
    {
        var window = new AttentionStateWindow(TimeSpan.FromSeconds(6), minimumSamples: 3);

        window.Observe(Classification(RawAttentionState.Focused), Start).Should().BeNull();
        window.Observe(Classification(RawAttentionState.Focused), Start.AddSeconds(1)).Should().BeNull();

        window.SampleCount.Should().Be(2);
    }

    [Fact]
    public void Observe_WithMajority_ReturnsMajorityStateAndAveragedConfidence()
    {
        var window = new AttentionStateWindow(TimeSpan.FromSeconds(6), minimumSamples: 3);
        window.Observe(Classification(RawAttentionState.Distracted, 0.6f), Start);
        window.Observe(Classification(RawAttentionState.Focused, 0.9f), Start.AddSeconds(1));

        var result = window.Observe(
            Classification(RawAttentionState.Focused, 0.81f),
            Start.AddSeconds(2));

        result.Should().NotBeNull();
        result!.State.Should().Be(RawAttentionState.Focused);
        result.Confidence.Should().BeApproximately(0.855f, 0.0001f);
    }

    [Fact]
    public void Observe_TieBetweenStates_ReturnsNull()
    {
        var window = new AttentionStateWindow(TimeSpan.FromSeconds(6), minimumSamples: 4);
        window.Observe(Classification(RawAttentionState.Focused), Start);
        window.Observe(Classification(RawAttentionState.Distracted), Start.AddSeconds(1));
        window.Observe(Classification(RawAttentionState.Focused), Start.AddSeconds(2));

        var result = window.Observe(
            Classification(RawAttentionState.Distracted),
            Start.AddSeconds(3));

        result.Should().BeNull();
    }

    [Fact]
    public void Observe_StaleSamples_ArePrunedFromTheWindow()
    {
        var window = new AttentionStateWindow(TimeSpan.FromSeconds(5), minimumSamples: 3);
        window.Observe(Classification(RawAttentionState.Focused), Start);
        window.Observe(Classification(RawAttentionState.Focused), Start.AddSeconds(1));
        window.Observe(Classification(RawAttentionState.Focused), Start.AddSeconds(2));

        var result = window.Observe(
            Classification(RawAttentionState.Focused),
            Start.AddSeconds(10));

        result.Should().BeNull();
        window.SampleCount.Should().Be(1);
    }

    [Fact]
    public void Observe_CameraUnavailable_BypassesAndResetsTheWindow()
    {
        var window = new AttentionStateWindow(TimeSpan.FromSeconds(6), minimumSamples: 3);
        window.Observe(Classification(RawAttentionState.Focused), Start);
        window.Observe(Classification(RawAttentionState.Focused), Start.AddSeconds(1));

        var camera = Classification(RawAttentionState.CameraUnavailable, 0, "camera-unavailable");
        window.Observe(camera, Start.AddSeconds(2)).Should().BeSameAs(camera);
        window.SampleCount.Should().Be(0);

        window.Observe(Classification(RawAttentionState.Focused), Start.AddSeconds(3)).Should().BeNull();
        window.Observe(Classification(RawAttentionState.Focused), Start.AddSeconds(4)).Should().BeNull();
    }

    [Fact]
    public void Constructor_RejectsInvalidOptions()
    {
        var invalid = new (TimeSpan Window, int Samples, double Share)[]
        {
            (TimeSpan.Zero, 3, 0.6),
            (TimeSpan.FromSeconds(61), 3, 0.6),
            (TimeSpan.FromSeconds(6), 1, 0.6),
            (TimeSpan.FromSeconds(6), 3, 0),
            (TimeSpan.FromSeconds(6), 3, 1.1)
        };

        foreach (var (window, samples, share) in invalid)
        {
            var create = () => new AttentionStateWindow(window, samples, share);
            create.Should().Throw<ArgumentOutOfRangeException>();
        }
    }
}
