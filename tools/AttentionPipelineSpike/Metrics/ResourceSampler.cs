using System.Diagnostics;

namespace AttentionPipelineSpike.Metrics;

public sealed class ResourceSampler : IDisposable
{
    private readonly Process _process = Process.GetCurrentProcess();
    private readonly List<double> _cpuNormalized = [];
    private readonly List<double> _cpuSingleCore = [];
    private readonly List<double> _workingSetMb = [];
    private readonly List<double> _privateMb = [];
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public void Start()
    {
        _process.Refresh();
        var lastCpu = _process.TotalProcessorTime;
        var lastTime = DateTimeOffset.UtcNow;
        _cts = new CancellationTokenSource();
        _loop = Task.Run(async () =>
        {
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(1000, _cts.Token);
                }
                catch (TaskCanceledException)
                {
                    break;
                }

                _process.Refresh();
                var now = DateTimeOffset.UtcNow;
                var cpu = _process.TotalProcessorTime;
                var wallMs = Math.Max(1, (now - lastTime).TotalMilliseconds);
                var cpuMs = Math.Max(0, (cpu - lastCpu).TotalMilliseconds);
                var singleCore = cpuMs / wallMs * 100.0;

                _cpuSingleCore.Add(singleCore);
                _cpuNormalized.Add(singleCore / Math.Max(1, Environment.ProcessorCount));
                _workingSetMb.Add(_process.WorkingSet64 / 1024.0 / 1024.0);
                _privateMb.Add(_process.PrivateMemorySize64 / 1024.0 / 1024.0);

                lastCpu = cpu;
                lastTime = now;
            }
        });
    }

    public ResourceStats Stop()
    {
        _cts?.Cancel();
        try
        {
            _loop?.Wait(2000);
        }
        catch
        {
            // Sampler shutdown is best effort.
        }

        _cts?.Dispose();
        _cts = null;
        _loop = null;

        return new ResourceStats(
            Avg(_cpuNormalized),
            Max(_cpuNormalized),
            Avg(_cpuSingleCore),
            Max(_cpuSingleCore),
            Avg(_workingSetMb),
            Max(_workingSetMb),
            Avg(_privateMb),
            Max(_privateMb));
    }

    private static double Avg(List<double> values) => values.Count == 0 ? 0 : values.Average();
    private static double Max(List<double> values) => values.Count == 0 ? 0 : values.Max();

    public void Dispose() => Stop();
}
