using BusPing.ViewModels;

namespace BusPing.Views;

public partial class RoutesPage : ContentPage
{
    private readonly RoutesViewModel _viewModel;

    public RoutesPage(RoutesViewModel viewModel)
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
