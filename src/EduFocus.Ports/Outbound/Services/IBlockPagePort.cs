namespace EduFocus.Ports.Outbound.Services;

public interface IBlockPagePort
{
    bool IsAvailable { get; }
    Task<bool> StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}
