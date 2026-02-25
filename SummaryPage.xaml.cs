using BaseLogApp.Core.ViewModels;

namespace BaseLogApp.Views;

public partial class SummaryPage : ContentPage
{
    private readonly JumpsViewModel _vm;

    public SummaryPage(JumpsViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = _vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.LoadAsync();
    }
}
