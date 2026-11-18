namespace EduFocus.Parent.App.Views;

public partial class WelcomePage : ContentPage
{
    public WelcomePage()
    {
        InitializeComponent();
    }

    private async void OnPairClicked(object sender, EventArgs e) =>
        await Shell.Current.GoToAsync("//pairing");

    private async void OnPrivacyTapped(object sender, TappedEventArgs e) =>
        await Shell.Current.GoToAsync("privacy");
}
