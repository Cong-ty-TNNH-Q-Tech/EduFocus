using EduFocus.Parent.Core.Push;

namespace EduFocus.Parent.App.Services;

public sealed class DisabledPushTokenProvider : IPushTokenProvider
{
    public Task<string?> GetTokenAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(null);
}
