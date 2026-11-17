namespace EduFocus.Parent.Core.Services;

public interface ITokenStore
{
    Task<string?> GetTokenAsync(CancellationToken cancellationToken = default);

    Task SaveTokenAsync(string token, CancellationToken cancellationToken = default);

    Task ClearAsync(CancellationToken cancellationToken = default);
}
