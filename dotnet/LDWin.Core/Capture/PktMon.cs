using System.Diagnostics;
using System.Text.RegularExpressions;

namespace LDWin.Capture;

/// <summary>
/// Drives Windows Packet Monitor (pktmon.exe, in-box since Windows 10 2004).
/// pktmon has no supported managed API, so the capture is configured through
/// its command line; the packets themselves are read live from its ETW
/// provider by <see cref="CaptureService"/>.
/// </summary>
public static class PktMon
{
    /// <summary>The Microsoft-Windows-PktMon ETW provider.</summary>
    public static readonly Guid ProviderGuid = new("4d4f80d9-c8bd-4d73-bb5b-19c90402c5ac");

    private const int CommandTimeoutMs = 30_000;

    // A component line from "pktmon list": an Id, then a MAC address. Matching
    // on these rather than the headings keeps parsing independent of the
    // display language.
    private static readonly Regex ComponentLine = new(
        @"^\s*(?<id>\d+)\s+(?<mac>(?:[0-9A-Fa-f]{2}[-:]){5}[0-9A-Fa-f]{2})\b",
        RegexOptions.Multiline | RegexOptions.CultureInvariant);

    public static string ExecutablePath =>
        Path.Combine(Environment.SystemDirectory, "pktmon.exe");

    public static bool IsAvailable()
    {
        try
        {
            return OperatingSystem.IsWindows() && File.Exists(ExecutablePath);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Runs pktmon with the given arguments and returns its exit code and combined output.</summary>
    public static (int ExitCode, string Output) Run(params string[] arguments)
    {
        var psi = new ProcessStartInfo(ExecutablePath)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetTempPath(),
        };
        foreach (var argument in arguments)
        {
            psi.ArgumentList.Add(argument);
        }

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Could not start pktmon.exe.");

        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();

        if (!process.WaitForExit(CommandTimeoutMs))
        {
            try { process.Kill(); } catch { /* already exited */ }
            throw new TimeoutException($"pktmon {string.Join(' ', arguments)} did not finish in time.");
        }

        process.WaitForExit(); // flush the redirected streams
        return (process.ExitCode, (stdout.Result + stderr.Result).Trim());
    }

    /// <summary>
    /// Returns the pktmon component IDs whose MAC address matches
    /// <paramref name="macAddress"/> in the output of "pktmon list". More
    /// than one can match (e.g. a NIC bound to a Hyper-V external switch).
    /// </summary>
    public static IReadOnlyList<string> ParseComponentIds(string listOutput, string macAddress)
    {
        var wanted = NormalizeMac(macAddress);
        var ids = new List<string>();
        if (wanted.Length != 12)
        {
            return ids;
        }

        foreach (Match match in ComponentLine.Matches(listOutput))
        {
            if (NormalizeMac(match.Groups["mac"].Value) == wanted)
            {
                var id = match.Groups["id"].Value;
                if (!ids.Contains(id))
                {
                    ids.Add(id);
                }
            }
        }

        return ids;
    }

    private static string NormalizeMac(string mac) =>
        new string(mac.Where(Uri.IsHexDigit).ToArray()).ToUpperInvariant();
}
