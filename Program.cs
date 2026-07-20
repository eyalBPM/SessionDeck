using System.Threading;

namespace SessionDeck;

public static class Program
{
    private const string MutexName = "SessionDeck_SingletonMutex";

    [STAThread]
    public static int Main(string[] args)
    {
        // Any command-line argument means CLI mode: forward to the running instance's pipe.
        if (args.Length > 0)
        {
            // Install commands run locally — they must work before the app has ever started.
            if (args[0] is "install-hooks" or "uninstall-hooks")
                return Cli.HookInstaller.Run(args);

            return Cli.CliClient.Run(args);
        }

        using var mutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            // Second UI launch — bring the existing instance to front and exit.
            Cli.CliClient.TrySendActivate();
            return 0;
        }

        Services.StartupService.MigrateLegacyValue();
        Services.StartupService.RefreshPathIfStale();

        var app = new App();
        app.InitializeComponent();
        return app.Run();
    }
}
