using Microsoft.Win32;

namespace Goam.Core.Platform;

/// <summary>"Start with Windows" behind an interface, so tests and sandboxed runs never touch the Run key.</summary>
public interface IStartupRegistration
{
    bool IsEnabled();
    void SetEnabled(bool enabled, string executablePath);
}

public static class StartupConstants
{
    /// <summary>Command-line flag GOAM is launched with from the Run key.</summary>
    public const string StartupArgument = "--startup";

    public const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string RunValueName = "GOAM";
}

/// <summary>HKCU\Software\Microsoft\Windows\CurrentVersion\Run - same key and value name as GOAM 1.x.</summary>
public sealed class RegistryStartupRegistration : IStartupRegistration
{
    public bool IsEnabled()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(StartupConstants.RunKeyPath);
        return key?.GetValue(StartupConstants.RunValueName) is string;
    }

    public void SetEnabled(bool enabled, string executablePath)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(StartupConstants.RunKeyPath, writable: true);
        if (enabled)
        {
            key.SetValue(
                StartupConstants.RunValueName,
                $"\"{executablePath}\" {StartupConstants.StartupArgument}",
                RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue(StartupConstants.RunValueName, throwOnMissingValue: false);
        }
    }
}

/// <summary>Stores the setting in a text file instead of the registry (sandbox / test mode).</summary>
public sealed class FileStartupRegistration : IStartupRegistration
{
    private readonly string _path;

    public FileStartupRegistration(string path)
    {
        _path = path;
    }

    public bool IsEnabled() => File.Exists(_path);

    public void SetEnabled(bool enabled, string executablePath)
    {
        if (enabled)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, $"\"{executablePath}\" {StartupConstants.StartupArgument}");
        }
        else if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }
}

public static class StartupRegistrationFactory
{
    /// <summary>
    /// Real registry normally; file-backed when the data folder is overridden (GOAM_DATA_DIR) so that
    /// automated runs can never create a real autostart entry.
    /// </summary>
    public static IStartupRegistration Create(AppPaths paths) =>
        paths.IsOverridden
            ? new FileStartupRegistration(Path.Combine(paths.DataDir, "startup-registration.txt"))
            : new RegistryStartupRegistration();
}
