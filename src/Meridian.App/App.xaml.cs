using Microsoft.UI.Xaml;
namespace Meridian;
public partial class App : Application
{
    private Window? _window;
    public App()
    {
        UnhandledException += (_, args) => RecordFailure(args.Exception);
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try { _window = new MainWindow(); _window.Activate(); }
        catch (Exception ex) { RecordFailure(ex); throw; }
    }

    private static void RecordFailure(Exception exception)
    {
        try
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Meridian");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "last-error.log"),
                DateTimeOffset.UtcNow.ToString("O") + Environment.NewLine + exception +
                Environment.NewLine + string.Join(Environment.NewLine,
                    exception.Data.Keys.Cast<object>().Select(key => key + ": " + exception.Data[key])));
        }
        catch { /* Diagnostics must not hide the original failure. */ }
    }
}
