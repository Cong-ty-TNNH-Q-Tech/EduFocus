using System.Text.Json;
using EduFocus.Ports.Inbound;
using EduFocus.Ports.Outbound.Persistence;
using EduFocus.Ports.Outbound.Services;

namespace EduFocus.UseCases.Cloud;

/// <summary>
/// Gate in front of the outbox for aggregate producers (#88). Nothing is
/// written while the cloud link is absent, disabled or still pairing, and a
/// storage or policy failure is logged without the payload and swallowed so
/// protection, attention and schedule flows never depend on the relay.
/// </summary>
public sealed class CloudEventSink : ICloudEventSink
{
    private readonly ICloudLinkRepository _links;
    private readonly ICloudOutboxUseCase _outbox;
    private readonly IAppLogger _logger;

    public CloudEventSink(
        ICloudLinkRepository links,
        ICloudOutboxUseCase outbox,
        IAppLogger logger)
    {
        _links = links ?? throw new ArgumentNullException(nameof(links));
        _outbox = outbox ?? throw new ArgumentNullException(nameof(outbox));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<bool> TryEnqueueAsync(
        string type,
        JsonElement payload,
        string? clientEventId = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var link = await _links.GetAsync(cancellationToken).ConfigureAwait(false);
            if (link is not { IsLinked: true })
                return false;

            await _outbox
                .EnqueueAsync(type, payload, clientEventId, cancellationToken)
                .ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Warning(
                LogCategories.Cloud,
                $"Could not queue a cloud event ({ex.GetType().Name}).");
            return false;
        }
    }
}
