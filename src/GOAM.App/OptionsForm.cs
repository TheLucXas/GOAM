using Goam.Core.Config;
using Goam.Core.Localization;

namespace Goam.App;

/// <summary>Settings dialog: relay key, folders, startup behaviour, reload, reset and diagnostic report.</summary>
internal sealed class OptionsForm : Form
{
    private readonly AppServices _s;
    private readonly Localizer _t;
    private readonly Func<Task> _copyReport;
    private readonly Action _trayOptionChanged;

    private readonly Label _relayLabel = Theme.Label(string.Empty, 10f);
    private readonly Label _csgoLabel = Theme.Label(string.Empty, 10f);
    private readonly Label _songsLabel = Theme.Label(string.Empty, 10f);

    public OptionsForm(AppServices services, Func<Task> copyReport, Action trayOptionChanged)
    {
        _s = services;
        _t = services.Localizer;
        _copyReport = copyReport;
        _trayOptionChanged = trayOptionChanged;

        Theme.Style(this);
        Text = _t["opt_title"];
        ClientSize = new Size(640, 560);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;

        var title = Theme.Label(_t["opt_title"], 18f, FontStyle.Bold);
        title.Location = new Point(24, 16);
        Controls.Add(title);

        var list = new TableLayoutPanel
        {
            Location = new Point(24, 64),
            Size = new Size(592, 410),
            ColumnCount = 2,
            BackColor = Theme.Panel,
            Padding = new Padding(12, 8, 12, 8),
        };
        list.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        list.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));

        AddRow(list, _relayLabel, Theme.Button(_t["btn_change_relay"], Theme.Blue, 180, 30), ChangeRelayKeyAsync);
        AddRow(list, _csgoLabel, Theme.Button(_t["btn_change_csgo"], Theme.Blue, 180, 30), ChangeCsgoDirAsync);
        AddRow(list, _songsLabel, Theme.Button(_t["btn_change_songs"], Theme.Blue, 180, 30), ChangeSongsDirAsync);
        AddSeparator(list);
        AddToggle(list, _t["opt_startup"], _s.Startup.IsEnabled(), OnStartupToggled);

        CheckBox? minimized = null;
        CheckBox? minimizedWin = null;
        minimized = AddToggle(list, _t["opt_start_min"], _s.Config.StartMinimized, on =>
        {
            _s.Config.StartMinimized = on;
            if (on)
            {
                _s.Config.StartMinimizedWindows = false;
                minimizedWin!.Checked = false;
            }

            _s.Engine.SaveSettings();
        });
        minimizedWin = AddToggle(list, _t["opt_start_min_win"], _s.Config.StartMinimizedWindows, on =>
        {
            _s.Config.StartMinimizedWindows = on;
            if (on)
            {
                _s.Config.StartMinimized = false;
                minimized.Checked = false;
            }

            _s.Engine.SaveSettings();
        });
        AddToggle(list, _t["opt_tray"], _s.Config.MinimizeToTray, on =>
        {
            _s.Config.MinimizeToTray = on;
            _s.Engine.SaveSettings();
            _trayOptionChanged();
        });
        AddSeparator(list);
        AddRow(list, Theme.Label(_t["opt_reload"], 10f), Theme.Button(_t["btn_reload"], Theme.Blue, 180, 30), ReloadAsync);
        AddRow(list, Theme.Label(_t["opt_report"], 10f), Theme.Button(_t["btn_copy_report"], Theme.Blue, 180, 30), _copyReport);
        AddRow(list, Theme.Label(_t["opt_reset"], 10f), Theme.Button(_t["btn_reset_cfg"], Theme.Red, 180, 30), ResetAsync);
        Controls.Add(list);

        var close = Theme.Button(_t["btn_close"], Theme.Neutral, 120, 36, 10f);
        close.Location = new Point(260, 500);
        close.DialogResult = DialogResult.Cancel;
        CancelButton = close;
        Controls.Add(close);

        RefreshLabels();
    }

    private void RefreshLabels()
    {
        _relayLabel.Text = _t.T("opt_relay_key", _s.Config.RelayKey);
        _csgoLabel.Text = _t.T("opt_csgo_dir", _s.Config.CsgoDir ?? "—");
        _songsLabel.Text = _t.T("opt_songs_dir", _s.Engine.SongsDir);
        foreach (Label l in new[] { _csgoLabel, _songsLabel })
        {
            l.AutoEllipsis = true;
            l.AutoSize = false;
            l.Size = new Size(370, 22);
        }
    }

    private static void AddRow(TableLayoutPanel table, Control left, Button right, Func<Task> action)
    {
        right.Click += async (_, _) => await action();
        left.Anchor = AnchorStyles.Left;
        right.Anchor = AnchorStyles.Right;
        int row = table.RowCount++;
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        table.Controls.Add(left, 0, row);
        table.Controls.Add(right, 1, row);
    }

    private static CheckBox AddToggle(TableLayoutPanel table, string text, bool initial, Action<bool> changed)
    {
        var box = new CheckBox
        {
            Text = text,
            Checked = initial,
            AutoSize = true,
            ForeColor = Theme.Text,
            Font = Theme.Ui(10f),
            Anchor = AnchorStyles.Left,
        };
        box.CheckedChanged += (_, _) => changed(box.Checked);
        int row = table.RowCount++;
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        table.Controls.Add(box, 0, row);
        table.SetColumnSpan(box, 2);
        return box;
    }

    private static void AddSeparator(TableLayoutPanel table)
    {
        var sep = new Panel { Height = 1, BackColor = Color.FromArgb(0x44, 0x44, 0x44), Dock = DockStyle.Fill, Margin = new Padding(4, 8, 4, 8) };
        int row = table.RowCount++;
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 18));
        table.Controls.Add(sep, 0, row);
        table.SetColumnSpan(sep, 2);
    }

    private async Task ChangeRelayKeyAsync()
    {
        string? input = Dialogs.Prompt(
            this, _t, _t["dlg_relay_title"], _t.T("dlg_relay_msg", _s.Config.RelayKey), _s.Config.RelayKey);
        if (string.IsNullOrWhiteSpace(input))
        {
            return;
        }

        string key = GameKeys.Normalize(input);
        if (!GameKeys.IsValid(key))
        {
            MessageBox.Show(this, _t.T("err_invalid_key", input.Trim()), "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        await Task.Run(() => _s.Engine.SetRelayKey(key));
        RefreshLabels();
        MessageBox.Show(this, _t.T("ok_relay_key", key), "OK", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private async Task ChangeCsgoDirAsync()
    {
        string? initial = _s.Config.CsgoDir ?? _s.Steam.FindCsgoDir();
        string? path = Dialogs.ChooseCsgoFolder(this, _t, initial, _t["dlg_initial_csgo"]);
        if (path is null)
        {
            return;
        }

        await Task.Run(() => _s.Engine.SetCsgoDir(path));
        RefreshLabels();
        MessageBox.Show(this, _t["ok_csgo_dir"], "OK", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private async Task ChangeSongsDirAsync()
    {
        string? path = Dialogs.ChooseSongsFolder(this, _t, _s.Engine.SongsDir);
        if (path is null)
        {
            return;
        }

        await Task.Run(() => _s.Engine.SetSongsDir(path));
        RefreshLabels();
        MessageBox.Show(this, _t["ok_songs_dir"], "OK", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void OnStartupToggled(bool enabled)
    {
        try
        {
            _s.Startup.SetEnabled(enabled, Application.ExecutablePath);
            _s.Config.StartWithWindows = enabled;
            _s.Engine.SaveSettings();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            _s.Log.Error("Could not change the Windows startup entry", ex);
            MessageBox.Show(this, ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task ReloadAsync()
    {
        await Task.Run(() => _s.Engine.ReloadSongs());
        MessageBox.Show(
            this, _t.T("ok_reload", _s.Engine.Songs.Count), "OK", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private async Task ResetAsync()
    {
        if (MessageBox.Show(
                this, _t["dlg_reset_msg"], _t["dlg_reset_title"], MessageBoxButtons.YesNo, MessageBoxIcon.Warning)
            != DialogResult.Yes)
        {
            return;
        }

        // Only GOAM's own settings are removed; songs and the game folder are left alone.
        await Task.Run(() =>
        {
            _s.Engine.Shutdown();
            _s.Store.Reset();
        });
        MessageBox.Show(this, _t["ok_reset"], "OK", MessageBoxButtons.OK, MessageBoxIcon.Information);
        DialogResult = DialogResult.Abort;
        Close();
        Application.Exit();
    }
}
