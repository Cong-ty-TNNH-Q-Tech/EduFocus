using EduFocus.Ports.Outbound.Services;

namespace EduFocus.UseCases.Tests.TestSupport;

internal sealed class FakeMonotonicClock : IMonotonicClockPort
{
    public TimeSpan Ticks { get; set; }

    public TimeSpan GetTickCount() => Ticks;
}
