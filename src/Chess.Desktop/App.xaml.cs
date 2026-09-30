using Microsoft.UI.Xaml;

namespace Chess.Desktop;

public partial class App : Application
{
    public static MainWindow? MainWindow { get; private set; }

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, args) =>
        {
            try
            {
                File.WriteAllText(Path.Combine(Path.GetTempPath(), "Chess-crash.txt"), args.Exception.ToString());
            }
            catch
            {
                // Crash logging must never hide the original exception.
            }
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        MainWindow = new MainWindow();
        MainWindow.Activate();
    }
}
