using BaseLogApp.ViewModels;

namespace BaseLogApp;

public partial class JumpsPage : ContentPage
{
    public JumpsPage(JumpsPageViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
        _ = vm.LoadAsync();
    }
}
