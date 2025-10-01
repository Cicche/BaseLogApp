using BaseLogApp.Core.ViewModels;

namespace BaseLogApp.Views;

public partial class JumpsPage : ContentPage
{
    private readonly JumpsViewModel _vm;
    public JumpsPage(JumpsViewModel vm)
    {
      //  InitializeComponent();
        _vm = vm;
        BindingContext = _vm;
    }
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.LoadAsync();
    }

    private async void OnRefreshClicked(object sender, EventArgs e)
    {
        await _vm.LoadAsync();
    }
}