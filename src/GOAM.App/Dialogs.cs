using Goam.Core.Game;
using Goam.Core.Localization;

namespace Goam.App;

/// <summary>Small modal dialogs, built in code to keep the app free of designer files.</summary>
internal static class Dialogs
{
    private static Form CreateDialog(string title, int width, int height)
    {
        var f = new Form
        {
            Text = title,
            ClientSize = new Size(width, height),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MaximizeBox = false,
            MinimizeBox = false,
            ShowInTaskbar = false,
        };
        Theme.Style(f);
        return f;
    }

    /// <summary>Single-line text prompt. Returns null on cancel.</summary>
    public static string? Prompt(IWin32Window owner, Localizer t, string title, string message, string initial = "")
    {
        using Form f = CreateDialog(title, 460, 190);
        var lbl = Theme.Label(message);
        lbl.MaximumSize = new Size(420, 0);
        lbl.Location = new Point(20, 16);
        var txt = new TextBox
        {
            Text = initial,
            Location = new Point(20, 110),
            Width = 420,
            BackColor = Theme.PanelAlt,
            ForeColor = Theme.Text,
            BorderStyle = BorderStyle.FixedSingle,
        };
        var ok = Theme.Button("OK", Theme.Green, 100, 32);
        var cancel = Theme.Button(t["btn_cancel"], Theme.Neutral, 100, 32);
        ok.Location = new Point(240, 146);
        cancel.Location = new Point(340, 146);
        ok.DialogResult = DialogResult.OK;
        cancel.DialogResult = DialogResult.Cancel;
        f.Controls.AddRange(new Control[] { lbl, txt, ok, cancel });
        f.AcceptButton = ok;
        f.CancelButton = cancel;
        f.Shown += (_, _) =>
        {
            txt.Focus();
            txt.SelectAll();
        };

        return f.ShowDialog(owner) == DialogResult.OK ? txt.Text : null;
    }

    /// <summary>Folder chooser used for the songs folder. Returns null on cancel.</summary>
    public static string? ChooseSongsFolder(IWin32Window owner, Localizer t, string? initial)
    {
        using Form f = CreateDialog(t["dlg_songs_title"], 560, 170);
        var lbl = Theme.Label(t["dlg_songs_custom"], 10.5f, FontStyle.Bold);
        lbl.Location = new Point(20, 16);
        var txt = new TextBox
        {
            Text = initial ?? string.Empty,
            Location = new Point(20, 56),
            Width = 410,
            BackColor = Theme.PanelAlt,
            ForeColor = Theme.Text,
            BorderStyle = BorderStyle.FixedSingle,
        };
        var browse = Theme.Button(t["dlg_browse"], Theme.Blue, 100, 28);
        browse.Location = new Point(440, 54);
        browse.Click += (_, _) =>
        {
            using var dlg = new FolderBrowserDialog { Description = t["dlg_songs_browse"], UseDescriptionForTitle = true };
            if (Directory.Exists(txt.Text))
            {
                dlg.InitialDirectory = txt.Text;
            }

            if (dlg.ShowDialog(f) == DialogResult.OK)
            {
                txt.Text = dlg.SelectedPath;
            }
        };

        var ok = Theme.Button(t["btn_confirm"], Theme.Green, 120, 32);
        var cancel = Theme.Button(t["btn_cancel"], Theme.Red, 120, 32);
        ok.Location = new Point(300, 118);
        cancel.Location = new Point(430, 118);
        cancel.DialogResult = DialogResult.Cancel;
        ok.Click += (_, _) =>
        {
            string value = txt.Text.Trim().Trim('"');
            if (value.Length > 0 && IsUsableFolder(value))
            {
                txt.Text = value;
                f.DialogResult = DialogResult.OK;
            }
            else
            {
                MessageBox.Show(f, t["err_invalid_songs"], "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        };
        f.Controls.AddRange(new Control[] { lbl, txt, browse, ok, cancel });
        f.AcceptButton = ok;
        f.CancelButton = cancel;

        return f.ShowDialog(owner) == DialogResult.OK ? txt.Text : null;
    }

    private static bool IsUsableFolder(string path)
    {
        try
        {
            return Directory.Exists(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>CS:GO folder chooser (must contain csgo.exe). Returns null on cancel.</summary>
    public static string? ChooseCsgoFolder(IWin32Window owner, Localizer t, string? initial, string title)
    {
        using Form f = CreateDialog(t["dlg_csgo_title"], 560, 190);
        var lbl = Theme.Label(title, 10.5f, FontStyle.Bold);
        lbl.Location = new Point(20, 16);
        var txt = new TextBox
        {
            Text = initial ?? string.Empty,
            Location = new Point(20, 56),
            Width = 410,
            BackColor = Theme.PanelAlt,
            ForeColor = Theme.Text,
            BorderStyle = BorderStyle.FixedSingle,
            PlaceholderText = @"...\Counter-Strike Global Offensive",
        };
        var browse = Theme.Button(t["dlg_browse"], Theme.Blue, 100, 28);
        browse.Location = new Point(440, 54);
        browse.Click += (_, _) =>
        {
            using var dlg = new OpenFileDialog
            {
                Title = t["dlg_csgo_browse"],
                Filter = "csgo.exe|csgo.exe|Executable|*.exe",
                CheckFileExists = true,
            };
            if (dlg.ShowDialog(f) == DialogResult.OK)
            {
                txt.Text = Path.GetDirectoryName(dlg.FileName) ?? string.Empty;
            }
        };

        var ok = Theme.Button(t["btn_confirm"], Theme.Green, 120, 32);
        var cancel = Theme.Button(t["btn_cancel"], Theme.Red, 120, 32);
        ok.Location = new Point(300, 138);
        cancel.Location = new Point(430, 138);
        cancel.DialogResult = DialogResult.Cancel;
        ok.Click += (_, _) =>
        {
            string value = txt.Text.Trim().Trim('"');
            if (GameFiles.LooksLikeCsgoDir(value))
            {
                txt.Text = value;
                f.DialogResult = DialogResult.OK;
            }
            else
            {
                MessageBox.Show(f, t["err_invalid_csgo"], "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        };
        f.Controls.AddRange(new Control[] { lbl, txt, browse, ok, cancel });
        f.AcceptButton = ok;
        f.CancelButton = cancel;

        return f.ShowDialog(owner) == DialogResult.OK ? txt.Text : null;
    }

    /// <summary>Waits for one key press and returns its Source name, or null if the dialog was closed.</summary>
    public static string? CaptureKey(IWin32Window owner, Localizer t, string currentKey)
    {
        using Form f = CreateDialog(t["dlg_play_key_title"], 340, 140);
        f.KeyPreview = true;
        f.TopMost = true;
        var lbl = Theme.Label(t["dlg_press_key"], 14f, FontStyle.Bold);
        lbl.Location = new Point(20, 26);
        var current = Theme.Label($"(current: {currentKey})", 9.5f, FontStyle.Regular, Theme.TextMuted);
        current.Location = new Point(20, 72);
        f.Controls.AddRange(new Control[] { lbl, current });

        string? result = null;
        f.KeyDown += (_, e) =>
        {
            string? name = KeyNames.FromKeys(e.KeyData);
            if (name is null)
            {
                return;
            }

            result = name;
            e.Handled = true;
            e.SuppressKeyPress = true;
            f.DialogResult = DialogResult.OK;
        };

        return f.ShowDialog(owner) == DialogResult.OK ? result : null;
    }

    public enum PlaylistChoice
    {
        Cancel,
        SingleVideo,
        FullPlaylist,
    }

    public static PlaylistChoice ChoosePlaylistMode(IWin32Window owner, Localizer t, string playlistName)
    {
        using Form f = CreateDialog(t["dlg_playlist_title"], 560, 190);
        var title = Theme.Label(t["dlg_playlist_msg"], 14f, FontStyle.Bold);
        title.Location = new Point(20, 20);
        var name = Theme.Label(playlistName, 10f);
        name.MaximumSize = new Size(520, 0);
        name.Location = new Point(20, 62);

        var single = Theme.Button(t["btn_single_video"], Theme.Blue, 170, 44);
        var all = Theme.Button(t["btn_full_playlist"], Theme.Green, 170, 44);
        var cancel = Theme.Button(t["btn_cancel"], Theme.Red, 130, 44);
        single.Location = new Point(20, 124);
        all.Location = new Point(200, 124);
        cancel.Location = new Point(410, 124);

        PlaylistChoice choice = PlaylistChoice.Cancel;
        single.Click += (_, _) =>
        {
            choice = PlaylistChoice.SingleVideo;
            f.DialogResult = DialogResult.OK;
        };
        all.Click += (_, _) =>
        {
            choice = PlaylistChoice.FullPlaylist;
            f.DialogResult = DialogResult.OK;
        };
        cancel.DialogResult = DialogResult.Cancel;
        f.CancelButton = cancel;
        f.Controls.AddRange(new Control[] { title, name, single, all, cancel });

        f.ShowDialog(owner);
        return choice;
    }
}
