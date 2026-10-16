using System.Diagnostics;
using System.Text;

namespace Goam.Core.YouTube;

/// <summary>
/// Runs an external program without a shell: arguments go through ArgumentList (never string
/// concatenation), output is streamed line by line, and cancellation kills the whole process tree.
/// </summary>
internal static class ProcessRunner
{
    public static async Task<int> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        Action<string>? onOutputLine,
        Action<string>? onErrorLine,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        var info = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = Path.GetDirectoryName(executable) ?? Environment.CurrentDirectory,
        };

        foreach (string arg in arguments)
        {
            info.ArgumentList.Add(arg);
        }

        if (environment is not null)
        {
            foreach ((string key, string value) in environment)
            {
                info.Environment[key] = value;
            }
        }

        using var process = new Process { StartInfo = info, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                onOutputLine?.Invoke(e.Data);
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                onErrorLine?.Invoke(e.Data);
            }
        };

        cancellationToken.ThrowIfCancellationRequested();
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using CancellationTokenRegistration registration = cancellationToken.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // already gone
            }
        });

        await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return process.ExitCode;
    }
}
