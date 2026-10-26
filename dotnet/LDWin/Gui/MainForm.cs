using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using LDWin.Capture;
using LDWin.Models;
using LDWin.Protocols;

namespace LDWin.Gui;

/// <summary>
/// The main (and only) window of LDWin: pick a network adapter, listen for the
/// first CDP/LLDP announcement from the directly-connected switch, display the
/// decoded link data and optionally save it to disk.
/// </summary>
public sealed class MainForm : Form
{
    private readonly ICaptureService _capture;

    private readonly ComboBox _adapterCombo;
    private readonly Button _getButton;
    private readonly Button _saveButton;
    private readonly Button _copyButton;
    private readonly CheckBox _keepListeningCheck;
    private readonly TextBox _resultsBox;
    private readonly Label _statusLabel;
    private readonly ProgressBar _listenProgress;
    private readonly System.Windows.Forms.Timer _listenTimer;

    private CancellationTokenSource? _captureCts;
    private Task? _captureWork;
    private string? _lastReport;

    // Listening state shown by the countdown; only touched on the UI thread.
    private readonly Stopwatch _attemptClock = new();
    private readonly Stopwatch _totalClock = new();
    private bool _keepListening;
    private int _attempt;
    private int _foundCount;

    // How long a single listen attempt runs before giving up (or retrying, if
    // "keep listening" is checked).
    private const int ListenSeconds = 60;

    // Path to the settings file for persisting the last selected adapter.
    private static readonly string SettingsDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LDWin");
    private static readonly string SettingsFile =
        Path.Combine(SettingsDir, "settings.txt");

    public MainForm()
    {
        var decoders = new ILinkDecoder[] { new LldpDecoder(), new CdpDecoder() };
        _capture = new CaptureService(decoders);

        // ---- Window chrome ----
        Text = "LDWin - Link Discovery for Windows";
        ClientSize = new Size(560, 540);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        MinimizeBox = true;
        StartPosition = FormStartPosition.CenterScreen;

        using (var iconStream = typeof(MainForm).Assembly.GetManifestResourceStream("LDWin.network.ico"))
        {
            if (iconStream is not null)
            {
                Icon = new Icon(iconStream);
            }
        }

        // ---- "Network Connection:" label ----
        var connectionLabel = new Label
        {
            Text = "Network Connection:",
            Location = new Point(12, 15),
            AutoSize = true
        };

        // ---- Adapter drop-down ----
        _adapterCombo = new ComboBox
        {
            Location = new Point(12, 38),
            Size = new Size(536, 23),
            DropDownStyle = ComboBoxStyle.DropDownList,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        _adapterCombo.SelectedIndexChanged += OnAdapterSelectionChanged;

        // ---- Action buttons (row 1) ----
        _getButton = new Button
        {
            Text = "Get Link Data",
            Location = new Point(12, 72),
            Size = new Size(160, 30)
        };
        _getButton.Click += OnGetOrStopClick;

        _saveButton = new Button
        {
            Text = "Save Link Data",
            Location = new Point(180, 72),
            Size = new Size(160, 30),
            Enabled = false
        };
        _saveButton.Click += OnSaveLinkDataClick;

        _copyButton = new Button
        {
            Text = "Copy",
            Location = new Point(348, 72),
            Size = new Size(100, 30),
            Enabled = false
        };
        _copyButton.Click += OnCopyClick;

        // ---- "Keep listening" checkbox (row 2) ----
        _keepListeningCheck = new CheckBox
        {
            Text = "Keep listening until found",
            Location = new Point(12, 110),
            AutoSize = true
        };

        _listenProgress = new ProgressBar
        {
            Location = new Point(220, 110),
            Size = new Size(328, 18),
            Maximum = ListenSeconds * 1000,
            Visible = false,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };

        _listenTimer = new System.Windows.Forms.Timer { Interval = 200 };
        _listenTimer.Tick += (_, _) => UpdateListeningStatus();

        // ---- Status line ----
        _statusLabel = new Label
        {
            Text = string.Empty,
            Location = new Point(12, 136),
            Size = new Size(536, 20),
            AutoEllipsis = true,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };

        // ---- Results area ----
        _resultsBox = new TextBox
        {
            Location = new Point(12, 162),
            Size = new Size(536, 316),
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Font = new Font(FontFamily.GenericMonospace, 9f),
            BackColor = SystemColors.Window,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
        };

        // ---- Bottom links ----
        var cdpLink = CreateLinkLabel(
            "About CDP",
            "https://en.wikipedia.org/wiki/Cisco_Discovery_Protocol",
            new Point(12, 490));

        var lldpLink = CreateLinkLabel(
            "About LLDP",
            "https://en.wikipedia.org/wiki/Link_Layer_Discovery_Protocol",
            new Point(110, 490));

        Controls.Add(connectionLabel);
        Controls.Add(_adapterCombo);
        Controls.Add(_getButton);
        Controls.Add(_saveButton);
        Controls.Add(_copyButton);
        Controls.Add(_keepListeningCheck);
        Controls.Add(_listenProgress);
        Controls.Add(_statusLabel);
        Controls.Add(_resultsBox);
        Controls.Add(cdpLink);
        Controls.Add(lldpLink);

        Load += OnFormLoad;
    }

    // -------------------------------------------------------------------------
    // Form load
    // -------------------------------------------------------------------------

    private void OnFormLoad(object? sender, EventArgs e)
    {
        if (!PktMon.IsAvailable())
        {
            MessageBox.Show(
                "LDWin uses Windows Packet Monitor (pktmon.exe) to capture CDP/LLDP, " +
                "but it was not found on this PC." +
                Environment.NewLine + Environment.NewLine +
                "Packet Monitor is built into Windows 10 version 2004 and later, and " +
                "Windows 11. Please update Windows and try again.",
                "Packet Monitor not found",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }

        LoadAdapters();
    }

    // -------------------------------------------------------------------------
    // Adapter helpers
    // -------------------------------------------------------------------------

    private void LoadAdapters()
    {
        try
        {
            _adapterCombo.BeginUpdate();
            _adapterCombo.Items.Clear();

            foreach (var adapter in _capture.GetAdapters())
            {
                _adapterCombo.Items.Add(adapter);
            }

            // Restore the previously selected adapter, keyed by its stable interface
            // ID rather than the (possibly duplicated) description. EndsWith also
            // matches the \Device\NPF_{GUID} names saved by Npcap-based versions.
            var saved = LoadSavedAdapterName();
            bool restored = false;
            if (!string.IsNullOrEmpty(saved))
            {
                for (int i = 0; i < _adapterCombo.Items.Count; i++)
                {
                    if (_adapterCombo.Items[i] is AdapterInfo ai &&
                        saved.EndsWith(ai.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        _adapterCombo.SelectedIndex = i;
                        restored = true;
                        break;
                    }
                }
            }

            if (!restored && _adapterCombo.Items.Count > 0)
            {
                _adapterCombo.SelectedIndex = 0;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Failed to enumerate network adapters:{Environment.NewLine}{Environment.NewLine}{ex.Message}",
                "LDWin",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            _adapterCombo.EndUpdate();
        }
    }

    private void OnAdapterSelectionChanged(object? sender, EventArgs e)
    {
        if (_adapterCombo.SelectedItem is AdapterInfo ai)
        {
            SaveAdapterName(ai.Name);
        }
    }

    // -------------------------------------------------------------------------
    // Settings persistence
    // -------------------------------------------------------------------------

    private static string? LoadSavedAdapterName()
    {
        try
        {
            if (File.Exists(SettingsFile))
            {
                return File.ReadAllText(SettingsFile).Trim();
            }
        }
        catch
        {
            // Settings failures must never crash the app.
        }

        return null;
    }

    private static void SaveAdapterName(string name)
    {
        try
        {
            Directory.CreateDirectory(SettingsDir);
            File.WriteAllText(SettingsFile, name);
        }
        catch
        {
            // Settings failures must never crash the app.
        }
    }

    // -------------------------------------------------------------------------
    // Get / Stop button
    // -------------------------------------------------------------------------

    private void OnGetOrStopClick(object? sender, EventArgs e)
    {
        if (_getButton.Text == "Stop")
        {
            try
            {
                _captureCts?.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Already disposed; nothing to cancel.
            }
        }
        else
        {
            StartCapture();
        }
    }

    private async void StartCapture()
    {
        if (_adapterCombo.SelectedItem is not AdapterInfo adapter)
        {
            MessageBox.Show(
                "Please select a network connection first.",
                "LDWin",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        SaveAdapterName(adapter.Name);

        if (adapter.IsWireless)
        {
            ShowWifiOnlyResult(adapter);
            return;
        }

        // Switch button to "Stop" and disable Save/Copy while running.
        _getButton.Text = "Stop";
        _saveButton.Enabled = false;
        _copyButton.Enabled = false;
        _adapterCombo.Enabled = false;
        _keepListeningCheck.Enabled = false;

        bool keepListening = _keepListeningCheck.Checked;
        _keepListening = keepListening;
        _attempt = 0;
        _foundCount = 0;
        _listenProgress.Style = keepListening ? ProgressBarStyle.Marquee : ProgressBarStyle.Blocks;
        _listenProgress.Value = 0;
        _listenProgress.Visible = true;
        _totalClock.Restart();
        _attemptClock.Restart();
        _listenTimer.Start();

        _captureCts?.Dispose();
        _captureCts = new CancellationTokenSource();
        var token = _captureCts.Token;

        // Progress<T> captures the UI SynchronizationContext at creation, so the
        // handler below always runs on the UI thread even though CaptureAll's
        // callback fires from the background capture thread. This lets results
        // appear the instant each neighbour is decoded, instead of only after
        // the full listen window closes - the window still runs its course so
        // additional switches on the same segment can also be caught.
        var liveNeighbours = new List<LinkData>();
        IProgress<LinkData> progress = new Progress<LinkData>(data =>
        {
            liveNeighbours.Add(data);
            _lastReport = BuildMultiNeighbourReport(liveNeighbours);
            _resultsBox.Text = _lastReport;
            _saveButton.Enabled = true;
            _copyButton.Enabled = true;
            _foundCount = liveNeighbours.Count;
            UpdateListeningStatus();
        });

        try
        {
            IReadOnlyList<LinkData>? neighbours = null;

            do
            {
                _attempt++;
                liveNeighbours.Clear();
                _foundCount = 0;
                _attemptClock.Restart();
                UpdateListeningStatus();

                _captureWork = Task.Run(
                    () => _capture.CaptureAll(adapter, TimeSpan.FromSeconds(ListenSeconds), token, progress.Report));
                neighbours = await _captureWork;

                if (neighbours.Count == 0 && keepListening && !token.IsCancellationRequested)
                {
                    // Brief yield so the UI repaints before the next attempt.
                    await Task.Delay(100, token);
                }
            }
            while (neighbours.Count == 0 && keepListening && !token.IsCancellationRequested);

            if (neighbours.Count == 0)
            {
                _statusLabel.Text =
                    "No link data received - the switch may not send CDP/LLDP, or it may take longer. Try again.";
            }
            else
            {
                _lastReport = BuildMultiNeighbourReport(neighbours);
                _resultsBox.Text = _lastReport;
                _saveButton.Enabled = true;
                _copyButton.Enabled = true;

                string summary = neighbours.Count == 1
                    ? $"Found 1 neighbour ({neighbours[0].Protocol})."
                    : $"Found {neighbours.Count} neighbours.";
                _statusLabel.Text = summary;
            }
        }
        catch (OperationCanceledException)
        {
            _statusLabel.Text = "Capture cancelled.";
        }
        catch (Exception ex)
        {
            _statusLabel.Text = "Capture failed.";
            MessageBox.Show(
                $"An error occurred while capturing link data:{Environment.NewLine}{Environment.NewLine}{ex.Message}",
                "LDWin",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            // Restore UI regardless of outcome.
            _listenTimer.Stop();
            _listenProgress.Visible = false;
            _getButton.Text = "Get Link Data";
            _adapterCombo.Enabled = true;
            _keepListeningCheck.Enabled = true;
        }
    }

    // -------------------------------------------------------------------------
    // Countdown while listening, so a quiet link doesn't look like a hang
    // -------------------------------------------------------------------------

    private void UpdateListeningStatus()
    {
        int remaining = Math.Max(0, ListenSeconds - (int)_attemptClock.Elapsed.TotalSeconds);
        string timeLeft = remaining > 0 ? $"{remaining}s left" : "finishing";

        if (!_keepListening)
        {
            _listenProgress.Value = (int)Math.Min(_listenProgress.Maximum, _attemptClock.ElapsedMilliseconds);
        }

        string found = _foundCount switch
        {
            0 => "",
            1 => "Found 1 neighbour - ",
            _ => $"Found {_foundCount} neighbours - ",
        };

        if (_keepListening)
        {
            var total = _totalClock.Elapsed;
            string soFar = total.TotalMinutes >= 1
                ? $"{(int)total.TotalMinutes}m {total.Seconds}s"
                : $"{total.Seconds}s";
            _statusLabel.Text = _foundCount > 0
                ? $"{found}still listening for more ({timeLeft})..."
                : $"Listening for CDP/LLDP neighbours (attempt {_attempt}, {soFar} so far)...";
        }
        else
        {
            _statusLabel.Text = _foundCount > 0
                ? $"{found}still listening for more ({timeLeft})..."
                : $"Listening for CDP/LLDP neighbours ({timeLeft})...";
        }
    }

    // -------------------------------------------------------------------------
    // Wi-Fi: CDP/LLDP can't be received wirelessly - explain why and show
    // the current AP association instead, rather than running a 60s capture
    // that can never find anything.
    // -------------------------------------------------------------------------

    private void ShowWifiOnlyResult(AdapterInfo adapter)
    {
        var wifi = _capture.GetWifiInfo(adapter, out uint wifiError);

        var sb = new StringBuilder();
        sb.AppendLine("CDP/LLDP cannot be received over Wi-Fi.");
        sb.AppendLine();
        sb.AppendLine("A switch sends CDP/LLDP only to whatever is plugged");
        sb.AppendLine("into its port - the access point, not your laptop -");
        sb.AppendLine("and access points do not relay those frames over the");
        sb.AppendLine("air. Connect by Ethernet to see the switch and port.");
        sb.AppendLine();
        sb.AppendLine("-- Current Wi-Fi connection --");

        if (wifi is not null)
        {
            sb.AppendLine($"SSID: {wifi.Ssid}");
            sb.AppendLine($"Access point (BSSID): {wifi.Bssid}");
            sb.AppendLine($"Signal quality: {wifi.SignalQualityPercent}%");
            sb.AppendLine($"Link speed: {wifi.LinkSpeedMbps} Mbps");
        }
        else
        {
            sb.AppendLine(WifiInfoProvider.DescribeError(wifiError).Replace("\n", Environment.NewLine));
        }

        _lastReport = sb.ToString();
        _resultsBox.Text = _lastReport;
        _saveButton.Enabled = true;
        _copyButton.Enabled = true;
        _statusLabel.Text = wifi is not null
            ? "Wi-Fi selected - showing access point info (CDP/LLDP needs a wired connection)."
            : "Wi-Fi selected - access point info unavailable (see details below).";
    }

    // -------------------------------------------------------------------------
    // Multi-neighbour report (with raw TLV section per neighbour)
    // -------------------------------------------------------------------------

    private static string BuildMultiNeighbourReport(IReadOnlyList<LinkData> neighbours)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < neighbours.Count; i++)
        {
            var linkData = neighbours[i];
            string label = neighbours.Count == 1
                ? $"Neighbour 1 ({linkData.Protocol})"
                : $"Neighbour {i + 1} ({linkData.Protocol}) — {linkData.DeviceId}";

            sb.AppendLine(new string('=', 50));
            sb.AppendLine(label);
            sb.AppendLine(new string('=', 50));
            sb.AppendLine();
            sb.Append(linkData.ToReport());

            if (linkData.RawTlvs.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("-- Raw TLVs --");
                foreach (var tlv in linkData.RawTlvs)
                {
                    sb.AppendLine(tlv);
                }
            }

            if (i < neighbours.Count - 1)
            {
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    // -------------------------------------------------------------------------
    // Save button
    // -------------------------------------------------------------------------

    private void OnSaveLinkDataClick(object? sender, EventArgs e)
    {
        if (string.IsNullOrEmpty(_lastReport))
        {
            return;
        }

        using var dialog = new SaveFileDialog
        {
            FileName = "LinkData.txt",
            Filter = "Text files|*.txt|All files|*.*",
            DefaultExt = "txt",
            AddExtension = true,
            OverwritePrompt = true
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            File.WriteAllText(dialog.FileName, _lastReport);
            _statusLabel.Text = $"Link data saved to {dialog.FileName}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Failed to save the link data:{Environment.NewLine}{Environment.NewLine}{ex.Message}",
                "LDWin",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    // -------------------------------------------------------------------------
    // Copy button
    // -------------------------------------------------------------------------

    private void OnCopyClick(object? sender, EventArgs e)
    {
        if (string.IsNullOrEmpty(_lastReport))
        {
            return;
        }

        try
        {
            Clipboard.SetText(_lastReport);
            _statusLabel.Text = "Report copied to clipboard.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Failed to copy to clipboard:{Environment.NewLine}{Environment.NewLine}{ex.Message}",
                "LDWin",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    // -------------------------------------------------------------------------
    // Form closing / dispose
    // -------------------------------------------------------------------------

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        try
        {
            _captureCts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Already disposed; nothing to cancel.
        }

        // Let the capture stop Packet Monitor before the process exits; otherwise
        // the capture is left running and blocks the next start.
        try
        {
            _captureWork?.Wait(TimeSpan.FromSeconds(10));
        }
        catch (Exception)
        {
            // Cancellation or a capture error - cleanup has already run.
        }

        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _listenTimer.Dispose();
            _captureCts?.Dispose();
        }

        base.Dispose(disposing);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static LinkLabel CreateLinkLabel(string text, string url, Point location)
    {
        var link = new LinkLabel
        {
            Text = text,
            Location = location,
            AutoSize = true,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left
        };
        link.LinkClicked += (_, _) => OpenUrl(url);
        return link;
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Unable to open the link:{Environment.NewLine}{url}{Environment.NewLine}{Environment.NewLine}{ex.Message}",
                "LDWin",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }
}
