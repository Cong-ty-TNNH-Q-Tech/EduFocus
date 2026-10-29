using System.Drawing;
using System.Windows.Forms;
using EduFocus.Ports.Outbound.Services;
using EduFocus.WPF.ViewModels;

namespace EduFocus.WPF.Services;

/// <summary>
/// System tray presence. The icon and tooltip mirror the persisted protection
/// state. Exit is intentionally absent: quitting requires a parent PIN and is
/// handled by the exit flow (issue #18).
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private readonly ProtectionStatusViewModel _protection;
    private readonly IAppLogger _logger;

    private NotifyIcon? _icon;
    private ToolStripMenuItem? _statusItem;

    public TrayIconService(ProtectionStatusViewModel protection, IAppLogger logger)
    {
        _protection = protection ?? throw new ArgumentNullException(nameof(protection));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void Start()
    {
        if (_icon is not null)
            return;

        try
        {
            _statusItem = new ToolStripMenuItem { Enabled = false };

            var menu = new ContextMenuStrip();
            menu.Items.Add(_statusItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Mở EduFocus", null, (_, _) => ShowMainWindow());

            _icon = new NotifyIcon
            {
                ContextMenuStrip = menu,
                Visible = true
            };
            _icon.DoubleClick += (_, _) => ShowMainWindow();

            _protection.TrayStateChanged += OnTrayStateChanged;
            UpdateIcon();
        }
        catch (Exception ex)
        {
            // A tray icon is best effort: missing shell support must not block startup.
            _logger.Error(LogCategories.UI, "Could not create the tray icon.", ex);
        }
    }

    public void Dispose()
    {
        _protection.TrayStateChanged -= OnTrayStateChanged;

        if (_icon is null)
            return;

        _icon.Visible = false;
        _icon.ContextMenuStrip?.Dispose();
        _icon.Dispose();
        _icon = null;
        _statusItem = null;
    }

    private void OnTrayStateChanged(object? sender, EventArgs e) => UpdateIcon();

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
