using System.Globalization;
using EduFocus.Domain.Reports;
using EduFocus.Ports.DTOs;
using EduFocus.Ports.Inbound;
using EduFocus.Ports.Outbound.Persistence;
using EduFocus.Ports.Outbound.Services;

namespace EduFocus.UseCases.Reports;

/// <summary>
/// Exports the report snapshot the parent is viewing to a flattened CSV
/// (UC10/UC11, issue #37). Summary/day/session totals come from the snapshot so
/// they match the screen exactly; activity samples are read through the
/// privacy-safe report projection and filtered to the selected local dates.
/// </summary>
public sealed class ReportExportService : IReportExportUseCase
{
    public const int MaxActivitySamples = 100_000;

    private readonly IActivitySampleRepository _samples;
    private readonly ICsvExportStore _csvStore;
    private readonly TimeProvider _timeProvider;

    public ReportExportService(
        IActivitySampleRepository samples,
        ICsvExportStore csvStore,
        TimeProvider timeProvider)
    {
        _samples = samples ?? throw new ArgumentNullException(nameof(samples));
        _csvStore = csvStore ?? throw new ArgumentNullException(nameof(csvStore));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<ReportExportResult> ExportCsvAsync(
        StudyReport report,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);

        if (report.Period.Start == default)
            throw new ArgumentException("A report period is required.", nameof(report));

        var warnings = new List<string>(report.Warnings);

        if (report.SessionsTruncated)
        {
            warnings.Add(
                $"File CSV chỉ gồm {ReportService.MaxDisplayedSessions} phiên gần nhất; " +
                "tổng hợp kỳ vẫn tính mọi phiên.");
        }

        if (report.AttentionEventsTruncated)
        {
            warnings.Add(
                $"File CSV chỉ gồm {ReportService.MaxAttentionEvents} sự kiện tập trung mới nhất.");
        }

        var samples = await LoadSamplesAsync(report, warnings, cancellationToken).ConfigureAwait(false);
        var events = report.AttentionEvents
            .Where(attentionEvent => IsInPeriod(attentionEvent.StartedAtUtc, report.Period))
            .ToArray();

        var exportedAt = _timeProvider.GetLocalNow();
        var fileName = $"EduFocus-bao-cao-{exportedAt.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}.csv";
        var content = ReportCsvWriter.Build(
            report,
            samples,
            events,
            exportedAt,
            _timeProvider.LocalTimeZone,
            warnings);
        var filePath = await _csvStore
            .SaveAsync(fileName, content, cancellationToken)
            .ConfigureAwait(false);

        return new ReportExportResult(
            fileName,
            filePath,
            report.Days.Count(day => day.HasSessions),
            report.Sessions.Count,
            samples.Count,
            events.Length,
            warnings);
    }

    private async Task<IReadOnlyList<ReportActivitySample>> LoadSamplesAsync(
        StudyReport report,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        if (report.Sessions.Count == 0)
            return [];

        var sessionIds = report.Sessions.Select(session => session.Id).ToArray();

        // One extra row detects the cap without loading unbounded detail rows.
        var samples = await _samples
            .ListForReportAsync(sessionIds, MaxActivitySamples + 1, cancellationToken)
            .ConfigureAwait(false);

        if (samples.Count > MaxActivitySamples)
        {
            warnings.Add("Mẫu hoạt động vượt giới hạn xuất; chỉ các mẫu mới nhất được ghi.");
            samples = samples.Take(MaxActivitySamples).ToArray();
        }

        return samples
            .Where(sample => IsInPeriod(sample.SampledAtUtc, report.Period))
            .ToArray();
    }

    private bool IsInPeriod(DateTime utc, ReportPeriod period)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(AsUtc(utc), _timeProvider.LocalTimeZone);
        return period.Contains(DateOnly.FromDateTime(local));
    }

    private static DateTime AsUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
