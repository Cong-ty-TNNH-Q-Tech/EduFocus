using EduFocus.WPF.Services;

namespace EduFocus.WPF.Tests.TestSupport;

internal sealed class FakeClipboardService : IClipboardService
{
    public bool Result { get; set; } = true;

    public string? LastText { get; private set; }

    public bool TrySetText(string text)
    {
        LastText = text;
        return Result;
    }
}
