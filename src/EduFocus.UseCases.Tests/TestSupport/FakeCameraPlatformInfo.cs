using EduFocus.Ports.Outbound.Services;

namespace EduFocus.UseCases.Tests.TestSupport;

internal sealed class FakeCameraPlatformInfo : ICameraPlatformInfo
{
    public bool SupportsMultiAppCamera { get; set; }
}
