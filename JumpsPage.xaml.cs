using BaseLog.ViewModels;

namespace BaseLog.Views;

public partial class JumpsPage : ContentPage
{
    public JumpsPage(JumpsPageViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
        _ = vm.LoadAsync();
    }
}
