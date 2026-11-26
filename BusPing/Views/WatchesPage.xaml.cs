using BusPing.ViewModels;

namespace BusPing.Views;

public partial class WatchesPage : ContentPage
{
    private readonly WatchesViewModel _viewModel;

    public WatchesPage(WatchesViewModel viewModel)
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
