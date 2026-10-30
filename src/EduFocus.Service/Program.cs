using System.Runtime.Versioning;
using EduFocus.Ports.Outbound.Services;
using EduFocus.Service.Composition;
using EduFocus.Service.Hosting;
using EduFocus.Service.Runtime;
using Microsoft.Extensions.DependencyInjection;

namespace EduFocus.Service;

public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length == 0)
            return RunWindowsService();

        if (Array.Exists(args, argument => argument is "--health" or "-h"))
            return ServiceHealthProbe.Run();

        if (Array.Exists(args, argument => argument == "--console"))
            return RunConsoleAsync().GetAwaiter().GetResult();

        Console.Error.WriteLine(
            "Usage: EduFocus.Service [--console | --health]. Without arguments the service must be started by the Service Control Manager.");
        return 2;
    }

    [SupportedOSPlatform("windows")]
    private static int RunWindowsService()
    {
        var power = new ServicePowerNotifier();
        var provider = ServiceComposition.Build(power);

        try
        {
            return new WindowsServiceHost(provider, power).Run();
        }
        catch (Exception ex)
        {
            TryLogFatal(provider, ex);
            return 1;
        }
        finally
        {
            DisposeProvider(provider);
        }
    }

    [SupportedOSPlatform("windows")]
    private static async Task<int> RunConsoleAsync()
    {
        var power = new ServicePowerNotifier();
        await using var provider = ServiceComposition.Build(power);
        var runtime = provider.GetRequiredService<ServiceRuntime>();

        using var stop = new ManualResetEventSlim();
        ConsoleCancelEventHandler handler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            stop.Set();
        };

        Console.CancelKeyPress += handler;

        try
        {
            await runtime.StartAsync();
            Console.WriteLine("EduFocus.Service is running in console mode. Press Ctrl+C to stop.");
            stop.Wait();
            await runtime.StopAsync();
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"EduFocus.Service failed: {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
        finally
        {
            Console.CancelKeyPress -= handler;
        }
    }

    private static void DisposeProvider(ServiceProvider provider)
    {
        try
        {
            provider.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(10));
        }
        catch
        {
            // Process exit continues even when a service fails to dispose.
        }
    }

    private static void TryLogFatal(ServiceProvider provider, Exception exception)
    {
        try
        {
            provider.GetService<IAppLogger>()?.Error(
                "App",
                "The protection service host failed.",
                exception);
        }
        catch
        {
        }
    }
}
