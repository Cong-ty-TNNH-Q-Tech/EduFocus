using System.Windows;
using System.Windows.Controls;

namespace EduFocus.WPF;

internal sealed class ExitPinDialog : Window
{
    private readonly PasswordBox _pin = new() { MaxLength = 8, FontSize = 18, Height = 38, Margin = new Thickness(0, 8, 0, 8) };
    private readonly TextBlock _status = new() { Foreground = System.Windows.Media.Brushes.DarkRed, Margin = new Thickness(0, 0, 0, 8), TextWrapping = TextWrapping.Wrap };
    private readonly Func<string, Task<bool>> _verify;
    private bool _checking;

    public ExitPinDialog(Func<string, Task<bool>> verify)
    {
        _verify = verify ?? throw new ArgumentNullException(nameof(verify));
        Title = "Authorize exit";
        Width = 360;
        Height = 230;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Content = BuildContent();
        Loaded += (_, _) => _pin.Focus();
    }

    private UIElement BuildContent()
    {
        var panel = new StackPanel { Margin = new Thickness(22) };
        panel.Children.Add(new TextBlock { Text = "Enter the parent PIN to exit EduFocus.", TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(_pin);
        panel.Children.Add(_status);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "Cancel", MinWidth = 88, Height = 32, Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
        var confirm = new Button { Content = "Exit", MinWidth = 88, Height = 32, IsDefault = true };
        confirm.Click += VerifyAndExit;
        buttons.Children.Add(cancel);
        buttons.Children.Add(confirm);
        panel.Children.Add(buttons);
        return panel;
    }

    private async void VerifyAndExit(object sender, RoutedEventArgs e)
    {
        if (_checking) return;
        _checking = true;
        try
        {
            var pin = _pin.Password;
            _pin.Clear();
            if (await _verify(pin)) DialogResult = true;
            else { _status.Text = "PIN was not accepted. Check the remaining attempts and try again."; _pin.Focus(); }
        }
        catch (Exception)
        {
            _status.Text = "Authentication is unavailable. EduFocus remains open.";
        }
        finally { _checking = false; }
    }
}
