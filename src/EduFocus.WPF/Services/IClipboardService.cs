namespace EduFocus.WPF.Services;

/// <summary>Clipboard access behind a seam so the pairing view model stays testable.</summary>
public interface IClipboardService
{
    bool TrySetText(string text);
}
