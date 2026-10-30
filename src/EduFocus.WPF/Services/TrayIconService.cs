using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using EduFocus.Ports.Outbound.Services;
using EduFocus.WPF.ViewModels;

namespace EduFocus.WPF.Services;

/// <summary>
/// System tray presence. The icon and tooltip mirror the persisted protection
/// state, and the menu reports the camera supervisor state. Exit opens the
/// PIN-gated exit dialog (UC12) instead of closing the app directly, even when
/// protection is OFF.
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private readonly ProtectionStatusViewModel _protection;
    private readonly CameraStatusViewModel _camera;
    private readonly IAppLogger _logger;
    private readonly Func<ExitWindow> _exitWindowFactory;

    private NotifyIcon? _icon;
    private ToolStripMenuItem? _statusItem;
    private ToolStripMenuItem? _cameraItem;

    public TrayIconService(
        ProtectionStatusViewModel protection,
        CameraStatusViewModel camera,
        IAppLogger logger,
        Func<ExitWindow> exitWindowFactory)
    {
        _protection = protection ?? throw new ArgumentNullException(nameof(protection));
        _camera = camera ?? throw new ArgumentNullException(nameof(camera));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _exitWindowFactory = exitWindowFactory ?? throw new ArgumentNullException(nameof(exitWindowFactory));
    }

    public void Start()
    {
        if (_icon is not null)
            return;

        try
        {
            _statusItem = new ToolStripMenuItem { Enabled = false };
            _cameraItem = new ToolStripMenuItem { Enabled = false };

            var menu = new ContextMenuStrip();
            menu.Items.Add(_statusItem);
            menu.Items.Add(_cameraItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Mở EduFocus", null, (_, _) => ShowMainWindow());
            menu.Items.Add("Thoát EduFocus...", null, (_, _) => ShowExitDialog());

            _icon = new NotifyIcon
            {
                ContextMenuStrip = menu,
                Visible = true
            };
            _icon.DoubleClick += (_, _) => ShowMainWindow();

            _protection.TrayStateChanged += OnTrayStateChanged;
            _camera.PropertyChanged += OnCameraPropertyChanged;
            UpdateIcon();
            UpdateCameraItem();
        }
        catch (Exception ex)
        {
            // A tray icon is best effort: missing shell support must not block startup.
            _logger.Error(LogCategories.UI, "Could not create the tray icon.", ex);
        }
    }

    public void NotifyHiddenToTray(bool protectionEnabled)
    {
        if (_icon is null)
            return;

        try
        {
            _icon.ShowBalloonTip(
                4000,
                "EduFocus vẫn đang chạy",
                protectionEnabled
                    ? "Ứng dụng vẫn đang bảo vệ. Chọn \"Thoát EduFocus\" trong tray và nhập PIN để thoát."
                    : "Ứng dụng vẫn chạy nền. Chọn \"Thoát EduFocus\" trong tray nếu muốn đóng ứng dụng.",
                ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            _logger.Error(LogCategories.UI, "Could not show the tray notification.", ex);
        }
    }

    public void Dispose()
    {
        _protection.TrayStateChanged -= OnTrayStateChanged;
        _camera.PropertyChanged -= OnCameraPropertyChanged;

        if (_icon is null)
            return;

        _icon.Visible = false;
        _icon.ContextMenuStrip?.Dispose();
        _icon.Dispose();
        _icon = null;
        _statusItem = null;
        _cameraItem = null;
    }

    private void OnTrayStateChanged(object? sender, EventArgs e) => UpdateIcon();

    private void OnCameraPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CameraStatusViewModel.TrayStatusText))
            UpdateCameraItem();
    }

    private void UpdateCameraItem()
    {
        if (_cameraItem is not null)
            _cameraItem.Text = _camera.TrayStatusText;
    }

    private void UpdateIcon()
    {
        if (_icon is null)
            return;

        _icon.Icon = _protection.IsEnabled ? SystemIcons.Shield : SystemIcons.Warning;
        _icon.Text = _protection.TrayTooltip;

        if (_statusItem is not null)
        {
            _statusItem.Text = _protection.IsEnabled
                ? "Trạng thái: Đang bảo vệ"
                : "Trạng thái: Chưa bảo vệ";
        }
    }

    private void ShowExitDialog()
    {
        try
        {
            var window = _exitWindowFactory();
            if (window.ShowDialog() == true)
                System.Windows.Application.Current?.Shutdown();
        }
        catch (Exception ex)
        {
            _logger.Error(LogCategories.UI, "Could not open the exit dialog.", ex);
        }
    }

    private static void ShowMainWindow()
    {
        if (System.Windows.Application.Current?.MainWindow is not { } window)
            return;

        window.Show();

        if (window.WindowState == System.Windows.WindowState.Minimized)
            window.WindowState = System.Windows.WindowState.Normal;

        window.Activate();
    }
}
