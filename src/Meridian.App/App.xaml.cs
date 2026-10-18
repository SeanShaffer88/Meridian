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
        _ = Services.FailureLog.TryRecord(exception);
    }
}
