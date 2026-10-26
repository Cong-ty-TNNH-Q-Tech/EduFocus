using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Threading.Channels;

internal static class Program
{
    private const string MutexName = "Local\\EduFocus.WatchdogSpike.v1";

    private static async Task<int> Main(string[] args)
    {
        if (args.Contains("--worker", StringComparer.Ordinal))
            return await RunWorkerAsync(args);

        if (args.Contains("--watchdog", StringComparer.Ordinal))
            return await RunWatchdogAsync(args);

        if (args.Contains("--demo", StringComparer.Ordinal))
            return await RunDemoAsync();

        Console.WriteLine("Usage: dotnet run --project tools/WatchdogSpike -- --demo");
        return 2;
    }

    private static async Task<int> RunWorkerAsync(string[] args)
    {
        Console.WriteLine($"WORKER_READY pid={Environment.ProcessId}");
        var exitAfterMs = GetIntArgument(args, "--exit-after-ms", defaultValue: -1);
        if (exitAfterMs < 0)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan);
            return 0;
        }

        await Task.Delay(exitAfterMs);
        return GetIntArgument(args, "--exit-code", defaultValue: 0);
    }

    private static async Task<int> RunWatchdogAsync(string[] args)
    {
        // Creation of the named object is the per-session lease; keeping the handle alive
        // reserves the name while async continuations move between worker threads.
        using var mutex = new Mutex(initiallyOwned: false, MutexName, out var createdNew);
        if (!createdNew)
        {
            Console.WriteLine("DUPLICATE_INSTANCE: an EduFocus watchdog is already active in this user session.");
            return 10;
        }

        var restartLimit = Math.Clamp(GetIntArgument(args, "--restart-limit", 3), 0, 10);
        var baseBackoffMs = Math.Clamp(GetIntArgument(args, "--backoff-base-ms", 5000), 50, 60000);
        var cleanExit = args.Contains("--clean-exit", StringComparer.Ordinal);
        using var stop = new CancellationTokenSource();
        ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            stop.Cancel();
        };
        Console.CancelKeyPress += cancelHandler;

        try
        {
            var restartCount = 0;
            while (true)
            {
                if (stop.IsCancellationRequested)
                    return 0;

                var workerArgs = cleanExit
                    ? new[] { "--worker", "--exit-after-ms=100", "--exit-code=0" }
                    : new[] { "--worker" };
                using var worker = StartSelf(workerArgs);
                Console.WriteLine($"CHILD_STARTED pid={worker.Id} restart={restartCount}");

                try
                {
                    await worker.WaitForExitAsync(stop.Token);
                }
                catch (OperationCanceledException)
                {
                    TryKill(worker);
                    WriteAudit("WatchdogStopped", restartCount, "cancelled");
                    return 0;
                }

                if (cleanExit && worker.ExitCode == 0)
                {
                    WriteAudit("WatchdogCleanExit", restartCount, "authorized-clean-exit");
                    return 0;
                }

                if (restartCount >= restartLimit)
                {
                    WriteAudit("WatchdogCircuitOpen", restartCount, "restart-limit-reached");
                    return 11;
                }

                restartCount++;
                var delayMs = (int)Math.Min(30000, baseBackoffMs * Math.Pow(2, restartCount - 1));
                WriteAudit("WatchdogRestart", restartCount, $"unexpected-exit-{worker.ExitCode}");
                Console.WriteLine($"BACKOFF ms={delayMs}");

                try
                {
                    await Task.Delay(delayMs, stop.Token);
                }
                catch (OperationCanceledException)
                {
                    WriteAudit("WatchdogStopped", restartCount, "cancelled-during-backoff");
                    return 0;
                }
            }
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }
    }

    private static async Task<int> RunDemoAsync()
    {
        var childPids = Channel.CreateUnbounded<int>();
        var transcript = new ConcurrentQueue<string>();
        using var supervisor = StartSelf("--watchdog", "--restart-limit=2", "--backoff-base-ms=100");
        var pump = PumpSupervisorOutputAsync(supervisor, childPids.Writer, transcript);

        try
        {
            var firstChild = await ReadChildPidAsync(childPids.Reader, TimeSpan.FromSeconds(10));

            using (var duplicate = StartSelf("--watchdog", "--restart-limit=2", "--backoff-base-ms=100"))
            {
                await duplicate.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                var duplicateOutput = await duplicate.StandardOutput.ReadToEndAsync();
                if (duplicate.ExitCode != 10 || !duplicateOutput.Contains("DUPLICATE_INSTANCE", StringComparison.Ordinal))
                    throw new InvalidOperationException("A second watchdog was not rejected by the single-instance guard.");
                Console.WriteLine("PASS: duplicate watchdog instance rejected");
            }

            var currentChild = firstChild;
            for (var killNumber = 1; killNumber <= 3; killNumber++)
            {
                using (var child = Process.GetProcessById(currentChild))
                    child.Kill(entireProcessTree: true);
                Console.WriteLine($"DEMO_KILLED_CHILD pid={currentChild}");

                if (killNumber < 3)
                    currentChild = await ReadChildPidAsync(childPids.Reader, TimeSpan.FromSeconds(10));
            }

            await supervisor.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            await pump;
            if (supervisor.ExitCode != 11)
                throw new InvalidOperationException($"Expected restart circuit to open (exit 11), received {supervisor.ExitCode}.");

            var lines = transcript.ToArray();
            var restartEvents = lines.Count(line => line.Contains("\"event\":\"WatchdogRestart\"", StringComparison.Ordinal));
            if (restartEvents != 2 || !lines.Any(line => line.Contains("\"event\":\"WatchdogCircuitOpen\"", StringComparison.Ordinal)))
                throw new InvalidOperationException("Expected two audited restarts followed by an open circuit.");
            Console.WriteLine("PASS: killed worker restarted twice with exponential backoff, then circuit breaker stopped the loop");

            using var cleanWatchdog = StartSelf("--watchdog", "--clean-exit", "--restart-limit=2", "--backoff-base-ms=100");
            var cleanLines = new ConcurrentQueue<string>();
            var cleanPump = PumpSupervisorOutputAsync(cleanWatchdog, null, cleanLines);
            await cleanWatchdog.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            await cleanPump;
            if (cleanWatchdog.ExitCode != 0 || cleanLines.Any(line => line.Contains("\"event\":\"WatchdogRestart\"", StringComparison.Ordinal)))
                throw new InvalidOperationException("A clean exit incorrectly caused a watchdog restart.");
            Console.WriteLine("PASS: clean exit did not restart the app");
            Console.WriteLine("DEMO_RESULT: PASS");
            return 0;
        }
        catch
        {
            TryKill(supervisor);
            throw;
        }
    }

    private static async Task PumpSupervisorOutputAsync(Process process, ChannelWriter<int>? childPids, ConcurrentQueue<string> transcript)
    {
        while (await process.StandardOutput.ReadLineAsync() is { } line)
        {
            transcript.Enqueue(line);
            Console.WriteLine(line);
            if (childPids is not null && line.StartsWith("CHILD_STARTED pid=", StringComparison.Ordinal))
            {
                var pidText = line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1][4..];
                if (int.TryParse(pidText, out var pid))
                    await childPids.WriteAsync(pid);
            }
        }

        childPids?.TryComplete();
    }

    private static async Task<int> ReadChildPidAsync(ChannelReader<int> reader, TimeSpan timeout)
    {
        using var cancellation = new CancellationTokenSource(timeout);
        return await reader.ReadAsync(cancellation.Token);
    }

    private static Process StartSelf(params string[] arguments)
    {
        var executablePath = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot determine current executable path.");
        var startInfo = new ProcessStartInfo(executablePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };

        if (Path.GetFileNameWithoutExtension(executablePath).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            startInfo.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        return Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start watchdog spike child process.");
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
    }

    private static int GetIntArgument(string[] args, string name, int defaultValue)
    {
        var value = args.FirstOrDefault(argument => argument.StartsWith(name + "=", StringComparison.Ordinal));
        return value is not null && int.TryParse(value[(name.Length + 1)..], out var parsed) ? parsed : defaultValue;
    }

    private static void WriteAudit(string eventType, int restartCount, string reason)
    {
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            timestampUtc = DateTimeOffset.UtcNow,
            @event = eventType,
            processId = Environment.ProcessId,
            restartCount,
            reason
        }));
    }
}
