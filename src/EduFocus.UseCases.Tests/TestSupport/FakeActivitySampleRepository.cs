using EduFocus.Domain.Entities;
using EduFocus.Domain.ValueObjects;
using EduFocus.Ports.DTOs;
using EduFocus.Ports.Outbound.Persistence;

namespace EduFocus.UseCases.Tests.TestSupport;

internal sealed class FakeActivitySampleRepository : IActivitySampleRepository
{
    public List<ActivitySample> Samples { get; } = [];

    public List<ReportActivitySample> ReportSamples { get; } = [];

    public bool ThrowOnAdd { get; set; }

    public bool ThrowOnReport { get; set; }

    public int ReportCalls { get; private set; }

    public int LastReportMaxCount { get; private set; }

    public IReadOnlyCollection<StudySessionId>? LastReportSessionIds { get; private set; }

    public Task AddAsync(ActivitySample sample, CancellationToken cancellationToken = default)
    {
        if (ThrowOnAdd)
            throw new InvalidOperationException("The sample store is unavailable.");

        Samples.Add(sample);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ReportActivitySample>> ListForReportAsync(
        IReadOnlyCollection<StudySessionId> sessionIds,
        int maxCount,
        CancellationToken cancellationToken = default)
    {
        ReportCalls++;
        LastReportMaxCount = maxCount;
        LastReportSessionIds = sessionIds;

        if (ThrowOnReport)
            throw new InvalidOperationException("The sample store is unavailable.");

        var ids = sessionIds.ToHashSet();
        IReadOnlyList<ReportActivitySample> result = ReportSamples
            .Where(sample => ids.Contains(sample.SessionId))
            .OrderByDescending(sample => sample.SampledAtUtc)
            .Take(maxCount)
            .ToList();

        return Task.FromResult(result);
    }
}
