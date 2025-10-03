using BaseLogApp.Core.ViewModels;
using Microsoft.Maui.Controls;
using System.Diagnostics;

namespace BaseLogApp.Views;

public partial class JumpsPage : ContentPage
{
    private readonly JumpsViewModel _vm;
    public JumpsPage(JumpsViewModel vm)
    {
        InitializeComponent();

        Debug.WriteLine($"Has Content: {Content != null}");

        //Debug.WriteLine($"ItemsSource set: {(ListSalti?.ItemsSource != null)}");

        _vm = vm;
        BindingContext = _vm;
        Dispatcher.Dispatch(async () => await _vm.LoadAsync());
        Debug.WriteLine($"VM type: {vm.GetType().FullName}");

        Debug.WriteLine($"BindingContext type: {BindingContext?.GetType().FullName}");
    }
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.LoadAsync();
       // System.Diagnostics.Debug.WriteLine($"ListSalti null? {ListSalti == null}, ItemsSource null? {ListSalti?.ItemsSource == null}");
    }

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        if (BindingContext is BaseLogApp.Core.ViewModels.JumpsViewModel vm)
            vm.ApplyFilter(e.NewTextValue);
    }

    private async void OnRefreshClicked(object sender, EventArgs e)
    {
        await _vm.LoadAsync();
        Debug.WriteLine($"Items bound: {(_vm.Items?.Count ?? -1)}");
    }
}