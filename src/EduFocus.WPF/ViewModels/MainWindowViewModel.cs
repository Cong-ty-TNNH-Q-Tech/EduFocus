namespace EduFocus.WPF.ViewModels;

/// <summary>
/// Shell view model. Protection state is shared with Parent Mode and the tray
/// icon so every surface reflects the same persisted value.
/// </summary>
public sealed class MainWindowViewModel
{
    public UpdateViewModel Update { get; }

    public ProtectionStatusViewModel Protection { get; }

    public CameraStatusViewModel Camera { get; }

    public MainWindowViewModel(
        UpdateViewModel update,
        ProtectionStatusViewModel protection,
        CameraStatusViewModel camera)
    {
        Update = update ?? throw new ArgumentNullException(nameof(update));
        Protection = protection ?? throw new ArgumentNullException(nameof(protection));
        Camera = camera ?? throw new ArgumentNullException(nameof(camera));
    }
}
