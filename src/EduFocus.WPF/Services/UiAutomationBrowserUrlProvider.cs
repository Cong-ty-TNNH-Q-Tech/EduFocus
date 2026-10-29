using System.Runtime.Versioning;
using System.Windows.Automation;
using EduFocus.Ports.Outbound.Services;

namespace EduFocus.WPF.Services;

/// <summary>
/// Best-effort address-bar read through UI Automation (UC07, architecture §7.2).
/// Only known browsers are queried, password fields are skipped and only
/// http/https values are returned; any failure yields null so the caller falls
/// back to <c>UrlSource = None</c>.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class UiAutomationBrowserUrlProvider : IBrowserUrlProviderPort
{
    private static readonly HashSet<string> BrowserProcesses =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "chrome", "msedge", "firefox", "brave", "opera", "vivaldi", "iexplore", "librewolf"
        };

    private static readonly string[] AddressBarIds =
    [
        "Address and search bar",
        "addressEditBox",
        "urlbar-input",
        "omnibox"
    ];

    public string? TryGetActiveUrl(ForegroundActivitySnapshot foreground)
    {
        ArgumentNullException.ThrowIfNull(foreground);

        if (foreground.WindowHandle == IntPtr.Zero ||
            !IsBrowser(foreground.ProcessName))
        {
            return null;
        }

        try
        {
            var window = AutomationElement.FromHandle(foreground.WindowHandle);
            if (window is null)
                return null;

            return ReadAddressBar(window) ?? ReadToolbarEdit(window);
        }
        catch (ElementNotAvailableException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static string? ReadAddressBar(AutomationElement window)
    {
        foreach (var id in AddressBarIds)
        {
            var condition = new AndCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit),
                new PropertyCondition(AutomationElement.AutomationIdProperty, id));

            var element = window.FindFirst(TreeScope.Descendants, condition);
            var url = element is null ? null : ReadUrl(element);
            if (url is not null)
                return url;
        }

        return null;
    }

    private static string? ReadToolbarEdit(AutomationElement window)
    {
        var toolbars = window.FindAll(
            TreeScope.Descendants,
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ToolBar));

        foreach (AutomationElement toolbar in toolbars)
        {
            var edits = toolbar.FindAll(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));

            foreach (AutomationElement edit in edits)
            {
                var url = ReadUrl(edit);
                if (url is not null)
                    return url;
            }
        }

        return null;
    }

    private static string? ReadUrl(AutomationElement element)
    {
        try
        {
            if (element.Current.IsPassword)
                return null;

            if (element.GetCurrentPattern(ValuePattern.Pattern) is not ValuePattern value)
                return null;

            var text = value.Current.Value?.Trim();
            if (string.IsNullOrEmpty(text))
                return null;

            return Uri.TryCreate(text, UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
                    ? text
                    : null;
        }
        catch (ElementNotAvailableException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }

    private static bool IsBrowser(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
            return false;

        var name = processName.Trim().Trim('"');
        var separator = name.LastIndexOfAny(['\\', '/']);
        if (separator >= 0)
            name = name[(separator + 1)..];

        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            name = name[..^4];

        return BrowserProcesses.Contains(name);
    }
}
