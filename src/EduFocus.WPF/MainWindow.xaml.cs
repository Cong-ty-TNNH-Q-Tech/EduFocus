using System.Windows;
using EduFocus.WPF.ViewModels;

namespace EduFocus.WPF;

public partial class MainWindow : Window
{
    public MainWindow(UpdateViewModel updateViewModel)
    {
        InitializeComponent();
        DataContext = updateViewModel;
    }
}
