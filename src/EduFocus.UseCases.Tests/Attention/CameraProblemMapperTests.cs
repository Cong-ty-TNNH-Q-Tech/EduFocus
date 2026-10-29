using EduFocus.Domain.Attention;
using EduFocus.Ports.DTOs;
using EduFocus.UseCases.Attention;
using FluentAssertions;

namespace EduFocus.UseCases.Tests.Attention;

public sealed class CameraProblemMapperTests
{
    [Theory]
    [InlineData(CameraFailureKind.Busy, CameraHealthProblem.Busy)]
    [InlineData(CameraFailureKind.PermissionDenied, CameraHealthProblem.PermissionDenied)]
    [InlineData(CameraFailureKind.NotFound, CameraHealthProblem.NoCamera)]
    [InlineData(CameraFailureKind.Disconnected, CameraHealthProblem.Disconnected)]
    [InlineData(CameraFailureKind.Timeout, CameraHealthProblem.Timeout)]
    [InlineData(CameraFailureKind.None, CameraHealthProblem.None)]
    [InlineData(CameraFailureKind.NotStarted, CameraHealthProblem.Unknown)]
    [InlineData(CameraFailureKind.Unknown, CameraHealthProblem.Unknown)]
    public void Failures_map_to_the_matching_health_problem(
        CameraFailureKind failure,
        CameraHealthProblem expected)
    {
        CameraProblemMapper.FromFailure(failure).Should().Be(expected);
    }
}
