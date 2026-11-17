using System.Globalization;
using EduFocus.Parent.App.Services;
using EduFocus.Parent.Core.Presentation;

namespace EduFocus.Parent.App.Views;

public partial class ReportsPage : ContentPage, IQueryAttributable
{
    private readonly ReportsViewModel _viewModel;
    private DateOnly? _pendingDate;

    public ReportsPage()
    {
        InitializeComponent();
        _viewModel = ServiceHelper.GetService<ReportsViewModel>();
        BindingContext = _viewModel;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("date", out var value)
            && DateOnly.TryParseExact(
                value?.ToString(),
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var date))
        {
            _pendingDate = date;
            if (IsLoaded)
                _ = LoadPendingAsync();
        }
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _ = LoadPendingAsync();
    }

    private Task LoadPendingAsync()
    {
        var date = _pendingDate;
        _pendingDate = null;
        return _viewModel.LoadAsync(date);
    }

    private void OnDayTapped(object? sender, TappedEventArgs e)
    {
        if (sender is TapGestureRecognizer { BindingContext: ReportDayItem item })
            _viewModel.SelectCommand.Execute(item);
    }
}
