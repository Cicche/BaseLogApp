namespace BaseLogApp
{
    public partial class App : Application
    {
        public App()
        {
            InitializeComponent();
        }

        protected override async void OnStart() // oppure nel costruttore della MainPage con _ = Task.Run...
        {
            #if !WINDOWS
                var target = Path.Combine(FileSystem.AppDataDirectory, "BASELogbook.sqlite");
                if (!File.Exists(target))
                {
                    // Se distribuisci un seed nel pacchetto: assicurati Build Action = MauiAsset in Resources/Raw
                    await using var input = await FileSystem.OpenAppPackageFileAsync("BASELogbook.sqlite");
                    await using var output = File.Create(target);
                    await input.CopyToAsync(output);
                }
            #endif
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