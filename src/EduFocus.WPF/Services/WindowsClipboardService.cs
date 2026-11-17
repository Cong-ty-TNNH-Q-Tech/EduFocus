using System.Runtime.InteropServices;
using System.Windows;

namespace EduFocus.WPF.Services;

public sealed class WindowsClipboardService : IClipboardService
{
    public bool TrySetText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        try
        {
            Clipboard.SetText(text);
            return true;
        }
        catch (ExternalException)
        {
            // Another process holds the clipboard; the parent can copy manually.
            return false;
        }
    }
}
