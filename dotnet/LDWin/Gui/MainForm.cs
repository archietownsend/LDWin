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
    private readonly INpcapDetector _npcap;

    private readonly ComboBox _adapterCombo;
    private readonly Button _getButton;
    private readonly Button _saveButton;
    private readonly Button _copyButton;
    private readonly CheckBox _keepListeningCheck;
    private readonly TextBox _resultsBox;
    private readonly Label _statusLabel;

    private CancellationTokenSource? _captureCts;
    private string? _lastReport;

    // Path to the settings file for persisting the last selected adapter.
    private static readonly string SettingsDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LDWin");
    private static readonly string SettingsFile =
        Path.Combine(SettingsDir, "settings.txt");

    public MainForm()
    {
        _npcap = new NpcapDetector();
        var decoders = new ILinkDecoder[] { new LldpDecoder(), new CdpDecoder() };
        _capture = new CaptureService(decoders);

        // ---- Window chrome ----
        Text = "LDWin - Link Discovery for Windows";
        ClientSize = new Size(560, 540);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        MinimizeBox = true;
        StartPosition = FormStartPosition.CenterScreen;

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

        var blogLink = CreateLinkLabel(
            "chall32.blogspot.com",
            "http://chall32.blogspot.com",
            new Point(220, 490));

        Controls.Add(connectionLabel);
        Controls.Add(_adapterCombo);
        Controls.Add(_getButton);
        Controls.Add(_saveButton);
        Controls.Add(_copyButton);
        Controls.Add(_keepListeningCheck);
        Controls.Add(_statusLabel);
        Controls.Add(_resultsBox);
        Controls.Add(cdpLink);
        Controls.Add(lldpLink);
        Controls.Add(blogLink);

        Load += OnFormLoad;
    }

    // -------------------------------------------------------------------------
    // Form load
    // -------------------------------------------------------------------------

    private void OnFormLoad(object? sender, EventArgs e)
    {
        if (!_npcap.IsInstalled())
        {
            var result = MessageBox.Show(
                "Npcap is required to capture CDP/LLDP packets but does not appear to be installed." +
                Environment.NewLine + Environment.NewLine +
                "Would you like to open the Npcap download page now?",
                "Npcap not found",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (result == DialogResult.Yes)
            {
                OpenUrl(_npcap.DownloadUrl);
            }
        }
        else
        {
            // Feature 5: show Npcap version in title bar when available.
            var version = _npcap.GetInstalledVersion();
            if (!string.IsNullOrWhiteSpace(version))
            {
                Text = $"LDWin - Npcap {version}";
            }
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

            // Feature 3: restore previously selected adapter.
            var saved = LoadSavedAdapterDescription();
            bool restored = false;
            if (!string.IsNullOrEmpty(saved))
            {
                for (int i = 0; i < _adapterCombo.Items.Count; i++)
                {
                    if (_adapterCombo.Items[i] is AdapterInfo ai &&
                        string.Equals(ai.Description, saved, StringComparison.OrdinalIgnoreCase))
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
        // Feature 3: persist whenever the selection changes.
        if (_adapterCombo.SelectedItem is AdapterInfo ai)
        {
            SaveAdapterDescription(ai.Description);
        }
    }

    // -------------------------------------------------------------------------
    // Settings persistence (Feature 3)
    // -------------------------------------------------------------------------

    private static string? LoadSavedAdapterDescription()
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

    private static void SaveAdapterDescription(string description)
    {
        try
        {
            Directory.CreateDirectory(SettingsDir);
            File.WriteAllText(SettingsFile, description);
        }
        catch
        {
            // Settings failures must never crash the app.
        }
    }

    // -------------------------------------------------------------------------
    // Get / Stop button (Features 1, 2, 4)
    // -------------------------------------------------------------------------

    private void OnGetOrStopClick(object? sender, EventArgs e)
    {
        if (_getButton.Text == "Stop")
        {
            // Feature 4: cancel the running capture/retry loop.
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

        // Feature 3: persist the adapter at capture start.
        SaveAdapterDescription(adapter.Description);

        // Switch button to "Stop" and disable Save/Copy while running.
        _getButton.Text = "Stop";
        _saveButton.Enabled = false;
        _copyButton.Enabled = false;
        _adapterCombo.Enabled = false;
        _keepListeningCheck.Enabled = false;

        bool keepListening = _keepListeningCheck.Checked;
        int attempt = 0;

        _captureCts?.Dispose();
        _captureCts = new CancellationTokenSource();
        var token = _captureCts.Token;

        try
        {
            LinkData? linkData = null;

            do
            {
                attempt++;
                _statusLabel.Text = keepListening && attempt > 1
                    ? $"Listening for CDP/LLDP announcements (attempt {attempt}, up to 60s)..."
                    : "Listening for CDP/LLDP announcements (up to 60s)...";

                linkData = await Task.Run(
                    () => _capture.Capture(adapter, TimeSpan.FromSeconds(60), token),
                    token);

                if (linkData is null && keepListening && !token.IsCancellationRequested)
                {
                    _statusLabel.Text = $"No response yet (attempt {attempt}), retrying...";
                    // Brief yield so the UI repaints the status before the next attempt.
                    await Task.Delay(100, token);
                }
            }
            while (linkData is null && keepListening && !token.IsCancellationRequested);

            if (linkData is null)
            {
                _statusLabel.Text =
                    "No link data received - the switch may not send CDP/LLDP, or it may take longer. Try again.";
            }
            else
            {
                // Feature 2: build full display text (report + raw TLVs).
                _lastReport = BuildFullReport(linkData);
                _resultsBox.Text = _lastReport;
                _saveButton.Enabled = true;
                _copyButton.Enabled = true;
                _statusLabel.Text = $"Received {linkData.Protocol} link data.";
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
            _getButton.Text = "Get Link Data";
            _adapterCombo.Enabled = true;
            _keepListeningCheck.Enabled = true;
        }
    }

    // -------------------------------------------------------------------------
    // Feature 2: raw TLV section
    // -------------------------------------------------------------------------

    private static string BuildFullReport(LinkData linkData)
    {
        var report = linkData.ToReport();

        if (linkData.RawTlvs.Count == 0)
        {
            return report;
        }

        var sb = new StringBuilder(report);
        sb.AppendLine();
        sb.AppendLine("-- Raw TLVs --");
        foreach (var tlv in linkData.RawTlvs)
        {
            sb.AppendLine(tlv);
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
    // Feature 1: Copy button
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

        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
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
