using EduFocus.Parent.Core.Services;

namespace EduFocus.Parent.App.Services;

public sealed class SecureStorageTokenStore : ITokenStore
{
    private const string TokenKey = "edufocus.parent.mobile_token";

    public Task<string?> GetTokenAsync(CancellationToken cancellationToken = default) =>
        SecureStorage.Default.GetAsync(TokenKey);

    public Task SaveTokenAsync(string token, CancellationToken cancellationToken = default) =>
        SecureStorage.Default.SetAsync(TokenKey, token);

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        SecureStorage.Default.Remove(TokenKey);
        return Task.CompletedTask;
    }
}
