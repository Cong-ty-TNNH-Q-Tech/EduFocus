using System.Windows;
using EduFocus.WPF.Services;

namespace EduFocus.WPF;

public partial class PrivacyNoticeWindow : Window
{
    public PrivacyNoticeWindow()
    {
        InitializeComponent();

        try
        {
            NoticeViewer.Document = PrivacyNoticeRenderer.CreateDocument();
        }
        catch (Exception exception)
        {
            NoticeViewer.Document = PrivacyNoticeRenderer.CreateErrorDocument(exception);
        }

        Loaded += (_, _) => CloseButton.Focus();
    }

    private void Close_OnClick(object sender, RoutedEventArgs e) => Close();
}
