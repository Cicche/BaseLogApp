namespace BaseLogApp
{
    public partial class App : Application
    {
        public App()
        {
            InitializeComponent();
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            var window = new Window(new AppShell());
#if WINDOWS
// iPhone 13 approx: 390 x 844 dp; su desktop usa pixel indipendenti
const int width = 390;
const int height = 844;
window.Width = width;
window.Height = height;
#endif
            return window;
        }
    }
}