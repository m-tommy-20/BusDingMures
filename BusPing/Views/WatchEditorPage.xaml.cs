using BusPing.ViewModels;

namespace BusPing.Views;

public partial class WatchEditorPage : ContentPage
{
    private readonly WatchEditorViewModel _viewModel;

    public WatchEditorPage(WatchEditorViewModel viewModel)
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
