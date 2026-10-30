using EduFocus.Ports.DTOs;
using EduFocus.UseCases.Tests.TestSupport;
using EduFocus.UseCases.Watchdog;
using FluentAssertions;

namespace EduFocus.UseCases.Tests.Watchdog;

public sealed class WatchdogAwareProtectionServiceTests
{
    [Theory]
    [InlineData("enable")]
    [InlineData("disable")]
    [InlineData("disable-exit")]
    [InlineData("reconcile")]
    [InlineData("enable-schedule")]
    [InlineData("disable-schedule")]
    public async Task Changed_protection_operations_synchronize_the_watchdog(string operation)
    {
        var inner = new FakeProtectionUseCase();
        var coordinator = new FakeWatchdogCoordinator();
        var service = new WatchdogAwareProtectionService(inner, coordinator);

        var result = operation switch
        {
            "enable" => await service.EnableAsync(),
            "disable" => await service.DisableAsync(),
            "disable-exit" => await service.DisableForExitAsync(),
            "reconcile" => await service.ReconcileAsync(),
            "enable-schedule" => await service.EnableForScheduleAsync(),
            "disable-schedule" => await service.DisableForScheduleAsync(),
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };

        result.Changed.Should().BeTrue();
        inner.Calls.Should().Be(1);
        coordinator.SynchronizeCount.Should().Be(1);
    }

    [Fact]
    public async Task Unchanged_protection_operations_do_not_synchronize_the_watchdog()
    {
        var inner = new FakeProtectionUseCase
        {
            Result = new ProtectionOperationResult(
                new ProtectionStatus(true, null, true),
                Changed: false,
                Warnings: [])
        };
        var coordinator = new FakeWatchdogCoordinator();
        var service = new WatchdogAwareProtectionService(inner, coordinator);

        var result = await service.EnableAsync();

        result.Changed.Should().BeFalse();
        coordinator.SynchronizeCount.Should().Be(0);
    }

    [Fact]
    public async Task Reading_the_status_does_not_synchronize_the_watchdog()
    {
        var inner = new FakeProtectionUseCase();
        var coordinator = new FakeWatchdogCoordinator();
        var service = new WatchdogAwareProtectionService(inner, coordinator);

        await service.GetStatusAsync();

        coordinator.SynchronizeCount.Should().Be(0);
    }
}
