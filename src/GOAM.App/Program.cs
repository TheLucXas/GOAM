using System.Security.Cryptography;
using System.Text;
using Goam.Core;
using Goam.Core.Platform;

namespace Goam.App;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        AppPaths paths = AppPaths.FromEnvironment();

        // One instance per data folder: a second launch just asks the first to come to the front.
        string suffix = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(paths.DataDir.ToLowerInvariant())))[..12];
        using var mutex = new Mutex(initiallyOwned: true, $@"Local\GOAM-{suffix}", out bool isFirstInstance);
        var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, $@"Local\GOAM-show-{suffix}");

        if (!isFirstInstance)
        {
            showEvent.Set();
            showEvent.Dispose();
            return 0;
        }

        ApplicationConfiguration.Initialize();

        bool fromStartup = args.Contains(StartupConstants.StartupArgument, StringComparer.OrdinalIgnoreCase);
        using var services = new AppServices(paths, fromStartup);

        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => ReportCrash(services, e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            services.Log.Error("Unhandled exception", e.ExceptionObject as Exception);

        services.Log.Info($"GOAM {Goam.Core.Diagnostics.DiagnosticReport.AppVersion()} starting"
                          + (fromStartup ? " (launched by Windows)" : string.Empty));
        if (services.MigratedFromLegacy)
        {
            services.Log.Info("Settings were migrated from the GOAM 1.x config file.");
        }

        // "--screenshot <file.png>": save a picture of the main window once it is ready, then exit.
        // Used to produce the README images and to check the UI in automated runs.
        string? screenshot = null;
        int flag = Array.FindIndex(args, a => string.Equals(a, "--screenshot", StringComparison.OrdinalIgnoreCase));
        if (flag >= 0 && flag + 1 < args.Length)
        {
            screenshot = Path.GetFullPath(args[flag + 1]);
        }

        using var form = new MainForm(services, showEvent, screenshot);
        Application.Run(form);

        showEvent.Dispose();
        return 0;
    }

    private static void ReportCrash(AppServices services, Exception ex)
    {
        services.Log.Error("Unhandled UI exception", ex);
        MessageBox.Show(
            $"{services.Localizer["err_critical"]}:\n\n{ex.Message}",
            "GOAM",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }
}
