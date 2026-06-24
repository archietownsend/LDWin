using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
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
    private readonly TextBox _resultsBox;
    private readonly Label _statusLabel;

    private CancellationTokenSource? _captureCts;
    private string? _lastReport;

    public MainForm()
    {
        _npcap = new NpcapDetector();
        var decoders = new ILinkDecoder[] { new LldpDecoder(), new CdpDecoder() };
        _capture = new CaptureService(decoders);

        // ---- Window chrome ----
        Text = "LDWin - Link Discovery for Windows";
        ClientSize = new Size(560, 520);
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

        // ---- Action buttons ----
        _getButton = new Button
        {
            Text = "Get Link Data",
            Location = new Point(12, 72),
            Size = new Size(160, 30)
        };
        _getButton.Click += OnGetLinkDataClick;

        _saveButton = new Button
        {
            Text = "Save Link Data",
            Location = new Point(180, 72),
            Size = new Size(160, 30),
            Enabled = false
        };
        _saveButton.Click += OnSaveLinkDataClick;

        // ---- Status line ----
        _statusLabel = new Label
        {
            Text = string.Empty,
            Location = new Point(12, 112),
            Size = new Size(536, 20),
            AutoEllipsis = true,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };

        // ---- Results area ----
        _resultsBox = new TextBox
        {
            Location = new Point(12, 138),
            Size = new Size(536, 320),
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
            new Point(12, 470));

        var lldpLink = CreateLinkLabel(
            "About LLDP",
            "https://en.wikipedia.org/wiki/Link_Layer_Discovery_Protocol",
            new Point(110, 470));

        var blogLink = CreateLinkLabel(
            "chall32.blogspot.com",
            "http://chall32.blogspot.com",
            new Point(220, 470));

        Controls.Add(connectionLabel);
        Controls.Add(_adapterCombo);
        Controls.Add(_getButton);
        Controls.Add(_saveButton);
        Controls.Add(_statusLabel);
        Controls.Add(_resultsBox);
        Controls.Add(cdpLink);
        Controls.Add(lldpLink);
        Controls.Add(blogLink);

        Load += OnFormLoad;
    }

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

        LoadAdapters();
    }

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

            if (_adapterCombo.Items.Count > 0)
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

    private async void OnGetLinkDataClick(object? sender, EventArgs e)
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

        _getButton.Enabled = false;
        _saveButton.Enabled = false;
        _statusLabel.Text = "Listening for CDP/LLDP announcements (up to 60s)...";

        _captureCts?.Dispose();
        _captureCts = new CancellationTokenSource();
        var token = _captureCts.Token;

        try
        {
            var linkData = await Task.Run(
                () => _capture.Capture(adapter, TimeSpan.FromSeconds(60), token),
                token);

            if (linkData is null)
            {
                _statusLabel.Text =
                    "No link data received - the switch may not send CDP/LLDP, or it may take longer. Try again.";
            }
            else
            {
                _lastReport = linkData.ToReport();
                _resultsBox.Text = _lastReport;
                _saveButton.Enabled = true;
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
            _getButton.Enabled = true;
        }
    }

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
}
