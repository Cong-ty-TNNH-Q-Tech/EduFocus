using System.Text.Json;
using EduFocus.Ports.DTOs;
using EduFocus.Ports.Inbound;
using EduFocus.Ports.Outbound.Cloud;
using EduFocus.Ports.Outbound.Services;

namespace EduFocus.UseCases.Cloud;

/// <summary>
/// Reports parent-initiated protection changes to the cloud (#88). Parent and
/// exit disables queue a protection_off event; every disable that actually
/// completes a session also captures the daily summary. Schedule-driven
/// disables stay silent to avoid alerting outside study hours. Cloud work is
/// best-effort and never changes the protection result.
/// </summary>
public sealed class CloudEventProtectionDecorator : IProtectionUseCase
{
    private const string ParentSource = "parent";
    private const string ExitSource = "exit";

    private readonly IProtectionUseCase _inner;
    private readonly CloudDailySummaryProducer _summaries;
    private readonly ICloudEventSink _sink;
    private readonly IAppLogger _logger;
    private readonly TimeProvider _timeProvider;

    public CloudEventProtectionDecorator(
        IProtectionUseCase inner,
        CloudDailySummaryProducer summaries,
        ICloudEventSink sink,
        IAppLogger logger,
        TimeProvider timeProvider)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _summaries = summaries ?? throw new ArgumentNullException(nameof(summaries));
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public Task<ProtectionStatus> GetStatusAsync(CancellationToken cancellationToken = default) =>
        _inner.GetStatusAsync(cancellationToken);

    public Task<ProtectionOperationResult> EnableAsync(CancellationToken cancellationToken = default) =>
        _inner.EnableAsync(cancellationToken);

    public Task<ProtectionOperationResult> EnableForScheduleAsync(CancellationToken cancellationToken = default) =>
        _inner.EnableForScheduleAsync(cancellationToken);

    public Task<ProtectionOperationResult> ReconcileAsync(CancellationToken cancellationToken = default) =>
        _inner.ReconcileAsync(cancellationToken);

    public Task<ProtectionOperationResult> ReloadRulesAsync(CancellationToken cancellationToken = default) =>
        _inner.ReloadRulesAsync(cancellationToken);

    public Task<ProtectionOperationResult> DisableAsync(CancellationToken cancellationToken = default) =>
        DisableCoreAsync(
            () => _inner.DisableAsync(cancellationToken),
            ParentSource,
            cancellationToken);

    public Task<ProtectionOperationResult> DisableForExitAsync(CancellationToken cancellationToken = default) =>
        DisableCoreAsync(
            () => _inner.DisableForExitAsync(cancellationToken),
            ExitSource,
            cancellationToken);

    public Task<ProtectionOperationResult> DisableForScheduleAsync(CancellationToken cancellationToken = default) =>
        DisableCoreAsync(
            () => _inner.DisableForScheduleAsync(cancellationToken),
            source: null,
            cancellationToken);

    private async Task<ProtectionOperationResult> DisableCoreAsync(
        Func<Task<ProtectionOperationResult>> operation,
        string? source,
        CancellationToken cancellationToken)
    {
        var wasEnabled = await ReadEnabledAsync(cancellationToken).ConfigureAwait(false);
        var result = await operation().ConfigureAwait(false);

        if (!result.Changed)
            return result;

        if (wasEnabled && source is not null)
            await TryReportProtectionOffAsync(source).ConfigureAwait(false);

        await _summaries.TryPublishAsync(CancellationToken.None).ConfigureAwait(false);

        return result;
    }

    private async Task<bool> ReadEnabledAsync(CancellationToken cancellationToken)
    {
        try
        {
            var status = await _inner.GetStatusAsync(cancellationToken).ConfigureAwait(false);
            return status.IsEnabled;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Warning(
                LogCategories.Cloud,
                $"Could not read the protection state before disabling ({ex.GetType().Name}).");
            return false;
        }
    }

    private async Task TryReportProtectionOffAsync(string source)
    {
        try
        {
            var localDate = DateOnly.FromDateTime(_timeProvider.GetLocalNow().DateTime);
            var payload = JsonSerializer.SerializeToElement(new
            {
                source,
                localDate,
            });

            await _sink
                .TryEnqueueAsync(CloudEventTypes.ProtectionOff, payload, clientEventId: null, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Warning(
                LogCategories.Cloud,
                $"Could not queue the protection-off event ({ex.GetType().Name}).");
        }
    }
}
