using BusPing.ViewModels;

namespace BusPing.Views;

public partial class AlertsPage : ContentPage
{
    private readonly AlertsViewModel _viewModel;

    public AlertsPage(AlertsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.InitializeAsync();
    }
}
