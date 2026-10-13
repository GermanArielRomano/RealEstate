using RealEstate.Mobile.ViewModels;

namespace RealEstate.Mobile.Views;

public partial class PropertiesListPage : ContentPage
{
    private readonly PropertiesListViewModel _viewModel;

    public PropertiesListPage(PropertiesListViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.InitializeCommand.ExecuteAsync(null);
    }
}
