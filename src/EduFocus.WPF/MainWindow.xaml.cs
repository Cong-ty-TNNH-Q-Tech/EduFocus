using System.Windows;
using EduFocus.WPF.ViewModels;

namespace EduFocus.WPF;

public partial class MainWindow : Window
{
    private readonly Func<ParentModeWindow> _parentModeWindowFactory;

    public MainWindow(
        UpdateViewModel updateViewModel,
        Func<ParentModeWindow> parentModeWindowFactory)
    {
        InitializeComponent();
        DataContext = updateViewModel;
        _parentModeWindowFactory = parentModeWindowFactory;
    }

    private void ParentModeButton_Click(object sender, RoutedEventArgs e)
    {
        var window = _parentModeWindowFactory();
        window.Owner = this;
        window.ShowDialog();
    }
}
