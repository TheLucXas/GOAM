using System.Diagnostics;
using Goam.Core;
using Goam.Core.Audio;
using Goam.Core.Config;
using Goam.Core.Diagnostics;
using Goam.Core.Game;
using Goam.Core.Localization;
using Goam.Core.Songs;
using Goam.Core.YouTube;

namespace Goam.App;

internal sealed class MainForm : Form
{
    private readonly AppServices _s;
    private readonly Localizer _t;
    private readonly GoamEngine _engine;
    private readonly EventWaitHandle _showEvent;

    // header
    private readonly Label _lblTitle = Theme.Label("GOAM", 26f, FontStyle.Bold);
    private readonly Button _btnLang;
    private readonly Button _btnOptions;
    private readonly Button _btnPlayKey;
    private readonly Label _lblSubtitle = Theme.Label(string.Empty, 10.5f);

    // control bar
    private readonly Label _lblGoam = Theme.Label(string.Empty, 9.5f, FontStyle.Bold, Theme.GreenText);
    private readonly Label _lblTrack = Theme.Label(string.Empty, 9f, FontStyle.Regular, Theme.TextMuted);
    private readonly Button _btnLaunch;
    private readonly Button _btnToggle;

    // volume
    private readonly Label _lblVolume = Theme.Label(string.Empty, 9.5f);
    private readonly TrackBar _volume = new()
    {
        Minimum = Volume.Min,
        Maximum = Volume.Max,
        TickStyle = TickStyle.None,
        SmallChange = 1,
        LargeChange = 10,
        BackColor = Theme.Panel,
        Dock = DockStyle.Top,
    };

    // youtube
    private readonly Label _lblYt = Theme.Label(string.Empty, 9.5f);
    private readonly TextBox _txtUrl = new()
    {
        BackColor = Theme.PanelAlt,
        ForeColor = Theme.Text,
        BorderStyle = BorderStyle.FixedSingle,
        Font = Theme.Ui(11f),
        Dock = DockStyle.Top,
    };
    private readonly Button _btnDownload;
    private readonly Button _btnImport;
    private readonly Button _btnCancel;

    // status + list
    private readonly Label _lblStatus = Theme.Label(string.Empty, 10f);
    private readonly ProgressBar _progress = new() { Style = ProgressBarStyle.Continuous, Height = 6, Dock = DockStyle.Top, Visible = false };
    private readonly Label _lblSongs = Theme.Label(string.Empty, 13f, FontStyle.Bold);
    private readonly SongGrid _grid = new() { Dock = DockStyle.Fill };
    private readonly Label _lblEmpty = Theme.Label(string.Empty, 10f, FontStyle.Regular, Theme.TextMuted);
    private readonly Button _btnReport;

    private readonly string? _screenshotPath;
    private readonly string? _benchmarkPath;
    private readonly string? _benchmarkMp3;
    private readonly System.Windows.Forms.Timer _volumeTimer = new() { Interval = 350 };
    private readonly System.Windows.Forms.Timer _statusTimer = new() { Interval = 2800 };

    private IReadOnlyList<SongEntry> _songs = Array.Empty<SongEntry>();
    private int _selectedIndex = 1;
    private bool _running = true;
    private bool _busy;
    private bool _suppressInitialShow;
    private int _refreshQueued;
    private CancellationTokenSource? _operationCts;
    private NotifyIcon? _tray;
    private Icon? _icon;
    private RegisteredWaitHandle? _showWait;

    public MainForm(
        AppServices services,
        EventWaitHandle showEvent,
        string? screenshotPath = null,
        string? benchmarkPath = null,
        string? benchmarkMp3 = null)
    {
        _s = services;
        _t = services.Localizer;
        _engine = services.Engine;
        _showEvent = showEvent;
        _screenshotPath = screenshotPath;
        _benchmarkPath = benchmarkPath;
        _benchmarkMp3 = benchmarkMp3;

        _btnLang = Theme.Button("EN", Theme.Window, 60, 36, 9.5f);
        _btnLang.FlatAppearance.BorderSize = 1;
        _btnLang.FlatAppearance.BorderColor = Theme.TextMuted;
        _btnLang.Font = Theme.Ui(9.5f, FontStyle.Bold);
        _btnOptions = Theme.Button(string.Empty, Theme.BlueLight, 110, 36);
        _btnPlayKey = Theme.Button(string.Empty, Color.FromArgb(0x2A, 0x4A, 0x2A), 120, 36);
        _btnLaunch = Theme.Button(string.Empty, Theme.Blue, 150, 42, 10f);
        _btnToggle = Theme.Button(string.Empty, Theme.RedDark, 170, 42, 10.5f);
        _btnDownload = Theme.Button(string.Empty, Theme.BlueLight, 240, 42, 10.5f);
        _btnImport = Theme.Button(string.Empty, Color.FromArgb(0x2C, 0x7A, 0x3F), 220, 42, 10.5f);
        _btnCancel = Theme.Button(string.Empty, Theme.Red, 120, 42, 10.5f);
        _btnReport = Theme.Button(string.Empty, Theme.Neutral, 140, 26, 8.5f);

        Theme.Style(this);
        Text = _t["title"];
        ClientSize = new Size(980, 820);
        MinimumSize = new Size(780, 640);
        StartPosition = FormStartPosition.CenterScreen;
        DoubleBuffered = true;
        LoadIcon();

        BuildLayout();

        // Set before the handlers are attached: restoring the saved value is not a user change.
        _volume.Value = Math.Clamp(_s.Config.VolumePercent, Volume.Min, Volume.Max);
        WireEvents();
        ApplyLanguage();
        ConfigureInitialVisibility();
    }

    // ---- construction --------------------------------------------------------------------------

    private void LoadIcon()
    {
        try
        {
            using Stream? stream = typeof(MainForm).Assembly.GetManifestResourceStream("goam.ico");
            if (stream is not null)
            {
                _icon = new Icon(stream, SystemInformation.IconSize);
                Icon = _icon;
            }
        }
        catch (Exception ex) when (ex is ArgumentException or IOException)
        {
            _s.Log.Warn($"Could not load the window icon: {ex.Message}");
        }
    }

    private static Panel Section(Padding padding) => new()
    {
        BackColor = Theme.Panel,
        Dock = DockStyle.Top,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        Padding = padding,
        Margin = new Padding(0, 6, 0, 6),
    };

    private void BuildLayout()
    {
        SuspendLayout();

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            Padding = new Padding(20, 14, 20, 10),
            BackColor = Theme.Window,
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        // header
        var header = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2 };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var headerButtons = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            Anchor = AnchorStyles.Right,
            WrapContents = false,
        };
        headerButtons.Controls.AddRange(new Control[] { _btnPlayKey, _btnOptions, _btnLang });
        _lblTitle.Anchor = AnchorStyles.Left;
        header.Controls.Add(_lblTitle, 0, 0);
        header.Controls.Add(headerButtons, 1, 0);
        root.Controls.Add(header);

        _lblSubtitle.Anchor = AnchorStyles.None;
        _lblSubtitle.Margin = new Padding(0, 0, 0, 6);
        root.Controls.Add(_lblSubtitle);

        // control bar
        var bar = Section(new Padding(14, 10, 14, 10));
        var barLayout = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2 };
        barLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        barLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var left = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        left.Controls.AddRange(new Control[] { _lblGoam, _lblTrack });
        var right = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        right.Controls.AddRange(new Control[] { _btnLaunch, _btnToggle });
        barLayout.Controls.Add(left, 0, 0);
        barLayout.Controls.Add(right, 1, 0);
        bar.Controls.Add(barLayout);
        root.Controls.Add(bar);

        // volume
        var vol = Section(new Padding(14, 10, 14, 6));
        vol.Controls.Add(_volume);
        _lblVolume.Dock = DockStyle.Top;
        vol.Controls.Add(_lblVolume);
        root.Controls.Add(vol);

        // youtube
        var yt = Section(new Padding(14, 10, 14, 10));
        var ytButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0, 10, 0, 0),
        };
        ytButtons.Controls.AddRange(new Control[] { _btnDownload, _btnImport, _btnCancel });
        yt.Controls.Add(ytButtons);
        yt.Controls.Add(_txtUrl);
        _lblYt.Dock = DockStyle.Top;
        _lblYt.Padding = new Padding(0, 0, 0, 6);
        yt.Controls.Add(_lblYt);
        root.Controls.Add(yt);

        // status
        var status = new Panel { Dock = DockStyle.Top, AutoSize = true, Margin = new Padding(0, 4, 0, 0) };
        _lblStatus.Dock = DockStyle.Top;
        _lblStatus.TextAlign = ContentAlignment.MiddleCenter;
        status.Controls.Add(_progress);
        status.Controls.Add(_lblStatus);
        root.Controls.Add(status);

        // songs
        _lblSongs.Margin = new Padding(8, 10, 0, 4);
        root.Controls.Add(_lblSongs);

        var gridHost = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Panel, Margin = new Padding(0) };
        gridHost.Controls.Add(_grid);
        _lblEmpty.AutoSize = false;
        _lblEmpty.Dock = DockStyle.Top;
        _lblEmpty.Height = 60;
        _lblEmpty.TextAlign = ContentAlignment.MiddleCenter;
        _lblEmpty.BackColor = Theme.Panel;
        gridHost.Controls.Add(_lblEmpty);
        _lblEmpty.BringToFront();
        root.Controls.Add(gridHost);

        // footer
        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            Margin = new Padding(0, 6, 0, 0),
        };
        footer.Controls.Add(_btnReport);
        root.Controls.Add(footer);

        int n = root.Controls.Count;
        for (int i = 0; i < n; i++)
        {
            root.RowStyles.Add(i == n - 2 ? new RowStyle(SizeType.Percent, 100) : new RowStyle(SizeType.AutoSize));
        }

        root.RowCount = n;
        Controls.Add(root);
        ResumeLayout(performLayout: true);

        _txtUrl.Margin = new Padding(0);
        _btnCancel.Enabled = false;
    }

    private void WireEvents()
    {
        _btnLang.Click += async (_, _) => await ToggleLanguageAsync();
        _btnOptions.Click += (_, _) => ShowOptions();
        _btnPlayKey.Click += async (_, _) => await CapturePlayKeyAsync();
        _btnToggle.Click += async (_, _) => await ToggleGoamAsync();
        _btnLaunch.Click += (_, _) => LaunchCsgo();
        _btnDownload.Click += async (_, _) => await OnDownloadClickAsync();
        _btnImport.Click += async (_, _) => await OnImportClickAsync();
        _btnCancel.Click += (_, _) => CancelOperation();
        _btnReport.Click += async (_, _) => await CopyReportAsync();
        _txtUrl.KeyDown += async (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                await OnDownloadClickAsync();
            }
        };

        _volume.ValueChanged += (_, _) =>
        {
            UpdateVolumeLabel();
            _volumeTimer.Stop();
            _volumeTimer.Start();
        };
        _volumeTimer.Tick += async (_, _) =>
        {
            _volumeTimer.Stop();
            int value = _volume.Value;
            await Task.Run(() =>
            {
                _engine.SetVolume(value);
                _engine.ReapplyVolume();
            });
        };
        _statusTimer.Tick += (_, _) =>
        {
            _statusTimer.Stop();
            if (!_busy)
            {
                _lblStatus.Text = _t["status_ready"];
            }
        };

        _grid.CellValueNeeded += OnCellValueNeeded;
        _grid.CellFormatting += OnCellFormatting;
        _grid.CellClick += async (_, e) => await OnGridCellClickAsync(e.RowIndex, e.ColumnIndex);
        _grid.CellDoubleClick += async (_, e) =>
        {
            if (e.ColumnIndex == SongGrid.NameColumn)
            {
                await OnGridCellClickAsync(e.RowIndex, SongGrid.SelectColumn);
            }
        };
        _grid.KeyDown += async (_, e) => await OnGridKeyDownAsync(e);

        _engine.StateChanged += (_, _) => QueueRefresh();
        _s.Preview.StateChanged += (_, _) => QueueRefresh();

        HandleCreated += (_, _) => BeginInvoke(new Action(() => _ = StartupAsync()));
        FormClosing += OnFormClosing;
        Resize += OnResizeForTray;
    }

    // ---- start-up ------------------------------------------------------------------------------

    private void ConfigureInitialVisibility()
    {
        bool minimize = _s.Config.StartMinimized
                        || (_s.Config.StartMinimizedWindows && _s.LaunchedFromStartup);
        if (!minimize)
        {
            return;
        }

        if (_s.Config.MinimizeToTray)
        {
            _suppressInitialShow = true;
            EnsureTray();
        }
        else
        {
            WindowState = FormWindowState.Minimized;
        }
    }

    protected override void SetVisibleCore(bool value)
    {
        if (_suppressInitialShow)
        {
            // Start hidden in the tray: make sure the handle exists (timers, BeginInvoke) but never show.
            if (!IsHandleCreated)
            {
                CreateHandle();
            }

            value = false;
        }

        base.SetVisibleCore(value);
    }

    private async Task StartupAsync()
    {
        _showWait = ThreadPool.RegisterWaitForSingleObject(
            _showEvent,
            (_, _) =>
            {
                try
                {
                    BeginInvoke(new Action(RestoreFromTray));
                }
                catch (InvalidOperationException)
                {
                    // closing
                }
            },
            null,
            Timeout.Infinite,
            executeOnlyOnce: false);

        try
        {
            await Task.Run(() =>
            {
                SongImporter.CleanDownloads(_s.Paths.DownloadsDir, _s.Log);
                _engine.Initialize();
            });
            RefreshUi();

            if (_s.IsFirstRun)
            {
                await FirstRunSongsFolderAsync();
            }

            if (_s.Config.CsgoDir is null || !Directory.Exists(_s.Config.CsgoDir))
            {
                await RequestCsgoDirAsync();
            }

            BackgroundYtDlpCheck();

            if (_screenshotPath is not null)
            {
                await Task.Delay(900);
                SaveScreenshot(_screenshotPath);
                Close();
            }
            else if (_benchmarkPath is not null)
            {
                await Task.Delay(600);
                await RunBenchmarkAsync(_benchmarkPath, _benchmarkMp3);
                Close();
            }
        }
        catch (Exception ex)
        {
            _s.Log.Error("Start-up failed", ex);
            MessageBox.Show(this, ex.Message, _t["err_critical"], MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// Self-measurement used for docs/PERFORMANCE.md: how long the song list takes to build from empty and to
    /// refresh after a selection change (including paint), and how long a 5-minute MP3 takes to convert.
    /// </summary>
    private async Task RunBenchmarkAsync(string outputPath, string? mp3Path)
    {
        static double Median(List<double> values)
        {
            var sorted = values.OrderBy(v => v).ToList();
            return sorted[sorted.Count / 2];
        }

        var result = new Dictionary<string, object?> { ["songs"] = _engine.Songs.Count };
        int n = Math.Max(1, _engine.Songs.Count);

        // cold build: empty grid -> N rows, painted
        _grid.RowCount = 0;
        _grid.Refresh();
        var sw = Stopwatch.StartNew();
        RefreshUi();
        _grid.Refresh();
        result["list_cold_build_ms"] = Math.Round(sw.Elapsed.TotalMilliseconds, 1);

        // refresh after a selection change (the engine call itself is not part of the UI time)
        var times = new List<double>();
        for (int k = 1; k <= 8; k++)
        {
            int index = (k * 7) % n + 1;
            await Task.Run(() => _engine.SelectSong(index));
            await Task.Delay(30);
            sw.Restart();
            RefreshUi();
            _grid.Refresh();
            times.Add(sw.Elapsed.TotalMilliseconds);
        }

        result["list_refresh_ms_median"] = Math.Round(Median(times), 1);
        result["list_refresh_ms_max"] = Math.Round(times.Max(), 1);

        if (mp3Path is not null && File.Exists(mp3Path))
        {
            string outWav = Path.Combine(_s.Paths.DataDir, "bench-convert.wav");
            var conv = new List<double>();
            for (int i = 0; i < 3; i++)
            {
                double ms = await Task.Run(() =>
                {
                    var t0 = Stopwatch.StartNew();
                    _s.Converter.ConvertToCsgoWav(mp3Path, outWav);
                    return t0.Elapsed.TotalMilliseconds;
                });
                conv.Add(ms);
            }

            File.Delete(outWav);
            result["convert_5min_mp3_ms_median"] = Math.Round(Median(conv), 1);
            result["convert_5min_mp3_ms_first"] = Math.Round(conv[0], 1);
        }

        using var self = Process.GetCurrentProcess();
        result["working_set_mb"] = Math.Round(self.WorkingSet64 / 1048576.0, 1);
        result["private_mb"] = Math.Round(self.PrivateMemorySize64 / 1048576.0, 1);

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        File.WriteAllText(
            outputPath,
            System.Text.Json.JsonSerializer.Serialize(
                result, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }

    private void SaveScreenshot(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        // Render the form's own controls only (never the screen), so nothing else on the desktop can leak in.
        using var bmp = new Bitmap(ClientSize.Width, ClientSize.Height);
        foreach (Control child in Controls)
        {
            child.DrawToBitmap(bmp, new Rectangle(child.Location, child.Size));
        }

        bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }

    private async Task FirstRunSongsFolderAsync()
    {
        bool custom = MessageBox.Show(
            this, _t["dlg_initial_songs"], _t["dlg_initial_title"],
            MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;

        string? chosen = null;
        while (custom)
        {
            chosen = Dialogs.ChooseSongsFolder(this, _t, null);
            if (chosen is not null)
            {
                break;
            }

            custom = MessageBox.Show(
                this, _t["dlg_songs_retry"], _t["dlg_initial_title"],
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
        }

        string target = chosen ?? _s.Paths.DefaultSongsDir;
        await Task.Run(() => _engine.SetSongsDir(target));
    }

    private async Task RequestCsgoDirAsync()
    {
        string? suggestion = _s.Steam.FindCsgoDir();
        string? path = Dialogs.ChooseCsgoFolder(this, _t, suggestion, _t["dlg_initial_csgo"]);
        if (path is null)
        {
            SetStatus(_t["err_invalid_csgo2"].Replace('\n', ' '));
            return;
        }

        await Task.Run(() => _engine.SetCsgoDir(path));
    }

    private void BackgroundYtDlpCheck()
    {
        if (!File.Exists(_s.Paths.YtDlpExe)
            || !YtDlpManager.IsCheckDue(_s.Config.YtDlpLastCheckUtc, DateTime.UtcNow))
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                bool updated = await _s.YtDlpManager.UpdateIfNewerAsync(null, CancellationToken.None);
                _s.Log.Info(updated ? "yt-dlp was updated." : "yt-dlp is up to date.");
                _s.Config.YtDlpLastCheckUtc = DateTime.UtcNow;
                _engine.SaveSettings();
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException
                                           or TaskCanceledException)
            {
                _s.Log.Warn($"Periodic yt-dlp check failed: {ex.Message}");
            }
        });
    }

    // ---- refresh -------------------------------------------------------------------------------

    private void QueueRefresh()
    {
        if (Interlocked.Exchange(ref _refreshQueued, 1) == 1)
        {
            return;
        }

        try
        {
            if (IsDisposed || !IsHandleCreated)
            {
                Interlocked.Exchange(ref _refreshQueued, 0);
                return;
            }

            BeginInvoke(new Action(() =>
            {
                Interlocked.Exchange(ref _refreshQueued, 0);
                RefreshUi();
            }));
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            Interlocked.Exchange(ref _refreshQueued, 0);
        }
    }

    private void RefreshUi()
    {
        _songs = _engine.Songs;
        _selectedIndex = _engine.SelectedIndex;
        _running = _engine.IsRunning;

        if (_grid.RowCount != _songs.Count)
        {
            _grid.RowCount = _songs.Count;
        }

        _grid.Invalidate();
        _lblEmpty.Visible = _songs.Count == 0;
        UpdateGoamTexts();
    }

    private void UpdateGoamTexts()
    {
        _lblGoam.Text = _running ? _t["goam_active"] : _t["goam_stopped"];
        _lblGoam.ForeColor = _running ? Theme.GreenText : Theme.RedText;

        _btnToggle.Text = _running ? _t["btn_stop_goam"] : _t["btn_start_goam"];
        Color back = _running ? Theme.RedDark : Theme.GreenDark;
        _btnToggle.BackColor = back;
        _btnToggle.FlatAppearance.MouseOverBackColor = ControlPaint.Light(back, 0.15f);

        SongEntry? current = _selectedIndex >= 1 && _selectedIndex <= _songs.Count ? _songs[_selectedIndex - 1] : null;
        _lblTrack.Text = current is null
            ? _t["track_label"]
            : _t.T("track_label_fmt", _selectedIndex, current.Stem);
    }

    private void UpdateVolumeLabel()
    {
        int percent = _volume.Value;
        _lblVolume.Text = _t.T("volume_label", percent, Volume.GetDbAdjust(percent));
    }

    private void ApplyLanguage()
    {
        Text = _t["title"];
        _lblSubtitle.Text = _t["subtitle"];
        _btnLang.Text = _t.Language.ToUpperInvariant();
        _btnOptions.Text = _t["btn_options"];
        _btnPlayKey.Text = _t.T("btn_play_key", _s.Config.PlayKey);
        _btnLaunch.Text = _t["btn_launch_csgo"];
        _lblYt.Text = _t["yt_label"];
        _txtUrl.PlaceholderText = _t["yt_placeholder"];
        _btnDownload.Text = _t["btn_download"];
        _btnImport.Text = _t["btn_import"];
        _btnCancel.Text = _t["btn_cancel"];
        _lblSongs.Text = _t["songs_title"];
        _lblEmpty.Text = _t["no_songs"];
        _btnReport.Text = _t["btn_copy_report"];
        if (!_busy)
        {
            _lblStatus.Text = _t["status_ready"];
        }

        if (_tray is not null)
        {
            _tray.Text = _t["tray_tooltip"];
            BuildTrayMenu();
        }

        UpdateVolumeLabel();
        UpdateGoamTexts();
        _grid.Invalidate();
    }

    private void SetStatus(string text)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => SetStatus(text)));
            return;
        }

        _lblStatus.Text = text;
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        _btnDownload.Enabled = !busy;
        _btnImport.Enabled = !busy;
        _btnCancel.Enabled = busy;
        if (!busy)
        {
            _progress.Visible = false;
            _progress.Value = 0;
        }
    }

    private void SetProgress(double? fraction)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => SetProgress(fraction)));
            return;
        }

        if (fraction is null)
        {
            _progress.Style = ProgressBarStyle.Marquee;
            _progress.Visible = true;
            return;
        }

        _progress.Style = ProgressBarStyle.Continuous;
        _progress.Maximum = 1000;
        _progress.Value = Math.Clamp((int)(fraction.Value * 1000), 0, 1000);
        _progress.Visible = true;
    }

    // ---- grid ----------------------------------------------------------------------------------

    private void OnCellValueNeeded(object? sender, DataGridViewCellValueEventArgs e)
    {
        IReadOnlyList<SongEntry> songs = _songs;
        if (e.RowIndex < 0 || e.RowIndex >= songs.Count)
        {
            return;
        }

        SongEntry song = songs[e.RowIndex];
        bool active = song.Index == _selectedIndex;
        e.Value = e.ColumnIndex switch
        {
            SongGrid.PreviewColumn => _s.Preview.CurrentStem == song.Stem ? "⏹" : "▶",
            SongGrid.NumberColumn => song.Index.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".",
            SongGrid.NameColumn => song.Stem,
            SongGrid.SelectColumn => active ? _t["btn_active"] : _t["btn_select"],
            SongGrid.RenameColumn => _t["btn_rename"],
            SongGrid.DeleteColumn => _t["btn_delete"],
            _ => null,
        };
    }

    private void OnCellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        IReadOnlyList<SongEntry> songs = _songs;
        if (e.RowIndex < 0 || e.RowIndex >= songs.Count)
        {
            return;
        }

        SongEntry song = songs[e.RowIndex];
        bool active = song.Index == _selectedIndex;
        DataGridViewCellStyle style = e.CellStyle!;

        void Paint(Color back, Color? fore = null)
        {
            style.BackColor = back;
            style.SelectionBackColor = back;
            style.ForeColor = fore ?? Theme.Text;
            style.SelectionForeColor = fore ?? Theme.Text;
        }

        switch (e.ColumnIndex)
        {
            case SongGrid.PreviewColumn:
                bool previewing = _s.Preview.CurrentStem == song.Stem;
                Paint(previewing ? Color.FromArgb(0x8B, 0, 0) : active ? Theme.GreenDark : Theme.BlueLight);
                break;
            case SongGrid.SelectColumn:
                Paint(active ? Theme.GreenDark : Theme.Green, active ? Theme.TextMuted : Theme.Text);
                break;
            case SongGrid.RenameColumn:
                Paint(Theme.BlueLight);
                break;
            case SongGrid.DeleteColumn:
                Paint(Theme.Red);
                break;
            case SongGrid.NameColumn:
                Paint(active ? Theme.GreenRow : Theme.Panel, active ? Theme.GreenText : Theme.Text);
                if (active)
                {
                    style.Font = Theme.Ui(10f, FontStyle.Bold);
                }

                break;
            default:
                Paint(active ? Theme.GreenRow : Theme.Panel, active ? Theme.GreenText : Theme.TextMuted);
                break;
        }
    }

    private async Task OnGridCellClickAsync(int row, int column)
    {
        IReadOnlyList<SongEntry> songs = _songs;
        if (row < 0 || row >= songs.Count)
        {
            return;
        }

        SongEntry song = songs[row];
        switch (column)
        {
            case SongGrid.PreviewColumn:
                int volume = _volume.Value;
                await Task.Run(() => _s.Preview.Toggle(song, volume));
                break;
            case SongGrid.SelectColumn:
                await Task.Run(() => _engine.SelectSong(song.Index));
                break;
            case SongGrid.RenameColumn:
                await RenameAsync(song);
                break;
            case SongGrid.DeleteColumn:
                await DeleteAsync(song);
                break;
        }
    }

    private async Task OnGridKeyDownAsync(KeyEventArgs e)
    {
        if (_grid.CurrentRow is null)
        {
            return;
        }

        int row = _grid.CurrentRow.Index;
        switch (e.KeyCode)
        {
            case Keys.Enter:
                e.SuppressKeyPress = true;
                await OnGridCellClickAsync(row, SongGrid.SelectColumn);
                break;
            case Keys.Space:
                e.SuppressKeyPress = true;
                await OnGridCellClickAsync(row, SongGrid.PreviewColumn);
                break;
            case Keys.F2:
                e.SuppressKeyPress = true;
                await OnGridCellClickAsync(row, SongGrid.RenameColumn);
                break;
            case Keys.Delete:
                e.SuppressKeyPress = true;
                await OnGridCellClickAsync(row, SongGrid.DeleteColumn);
                break;
        }
    }

    private async Task RenameAsync(SongEntry song)
    {
        string? input = Dialogs.Prompt(this, _t, _t["dlg_rename_title"], _t["dlg_rename_msg"], song.Stem);
        if (string.IsNullOrWhiteSpace(input) || input.Trim() == song.Stem)
        {
            return;
        }

        try
        {
            await Task.Run(() => _engine.Rename(song, input));
        }
        catch (SongNameException ex)
        {
            string key = ex.Message == "exists" ? "err_name_exists" : "err_invalid_name";
            MessageBox.Show(this, _t[key], "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _s.Log.Error("Rename failed", ex);
            MessageBox.Show(this, _t.T("err_rename", ex.Message), "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task DeleteAsync(SongEntry song)
    {
        if (MessageBox.Show(
                this, _t.T("dlg_delete_msg", song.FileName), _t["dlg_delete_title"],
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        try
        {
            await Task.Run(() => _engine.Delete(song));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _s.Log.Error("Delete failed", ex);
            MessageBox.Show(this, _t.T("err_delete", ex.Message), "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // ---- header / control bar actions -----------------------------------------------------------

    private async Task ToggleLanguageAsync()
    {
        string next = _t.Language == Localizer.Spanish ? Localizer.English : Localizer.Spanish;
        await Task.Run(() => _engine.SetLanguage(next));
        ApplyLanguage();
    }

    private async Task CapturePlayKeyAsync()
    {
        string? key = Dialogs.CaptureKey(this, _t, _s.Config.PlayKey);
        if (key is null)
        {
            return;
        }

        if (!GameKeys.IsValid(key))
        {
            MessageBox.Show(this, _t.T("err_invalid_key", key), "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        await Task.Run(() => _engine.SetPlayKey(key));
        _btnPlayKey.Text = _t.T("btn_play_key", _s.Config.PlayKey);
    }

    private async Task ToggleGoamAsync()
    {
        bool target = !_engine.IsRunning;
        await Task.Run(() => _engine.SetRunning(target));
    }

    private void LaunchCsgo()
    {
        string? steam = _engine.FindSteamExe();
        if (steam is null)
        {
            MessageBox.Show(this, _t["err_no_steam"], "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        try
        {
            var info = new ProcessStartInfo(steam) { UseShellExecute = false };
            info.ArgumentList.Add("-applaunch");
            info.ArgumentList.Add(SteamLocator.CsgoAppId);
            info.ArgumentList.Add("+exec");
            info.ArgumentList.Add("goam.cfg");
            Process.Start(info)?.Dispose();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            _s.Log.Error("Could not launch CS:GO", ex);
            MessageBox.Show(this, _t.T("err_launch", ex.Message), "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ShowOptions()
    {
        using var options = new OptionsForm(_s, CopyReportAsync, OnTrayOptionChanged);
        options.ShowDialog(this);
        _btnPlayKey.Text = _t.T("btn_play_key", _s.Config.PlayKey);
        RefreshUi();
    }

    // ---- import / download ---------------------------------------------------------------------

    private async Task OnImportClickAsync()
    {
        if (_busy)
        {
            return;
        }

        using var dialog = new OpenFileDialog
        {
            Title = _t["dlg_songs_title"],
            Multiselect = true,
            CheckFileExists = true,
            Filter = "Compatible audio|*.mp3;*.wav;*.flac;*.ogg|MP3|*.mp3|WAV|*.wav|FLAC|*.flac|OGG|*.ogg",
        };
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.FileNames.Length == 0)
        {
            return;
        }

        string[] files = dialog.FileNames;
        using var cts = new CancellationTokenSource();
        _operationCts = cts;
        SetBusy(true);
        SetProgress(0);
        var failures = new List<string>();
        string? lastStem = null;

        try
        {
            string songsDir = _engine.SongsDir;
            for (int i = 0; i < files.Length && !cts.IsCancellationRequested; i++)
            {
                string file = files[i];
                string title = Path.GetFileNameWithoutExtension(file);
                SetStatus(_t.T("status_importing", i + 1, files.Length, Shorten(title, 50)));
                SetProgress((double)i / files.Length);

                try
                {
                    string stem = await Task.Run(
                        () => _s.Importer.Import(file, title, songsDir, deleteSource: false, cts.Token), cts.Token);
                    lastStem = stem;
                    await Task.Run(() => _engine.AddAndSelect(stem));
                }
                catch (UnsupportedAudioFormatException ex)
                {
                    _s.Log.Warn(ex.Message);
                    failures.Add(Path.GetExtension(file).ToUpperInvariant());
                }
            }
        }
        catch (OperationCanceledException)
        {
            // user cancelled
        }
        finally
        {
            _operationCts = null;
            SetBusy(false);
        }

        if (failures.Count > 0)
        {
            string ext = string.Join(", ", failures.Distinct());
            MessageBox.Show(
                this, _t.T("err_unsupported", ext.Length == 0 ? "?" : ext), "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        ShowResult(lastStem);
    }

    private async Task OnDownloadClickAsync()
    {
        if (_busy)
        {
            return;
        }

        string text = _txtUrl.Text.Trim();
        if (!YouTubeUrl.TryNormalize(text, out string url))
        {
            MessageBox.Show(
                this, _t["err_invalid_link"], _t["warn_invalid_link"], MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        using var cts = new CancellationTokenSource();
        _operationCts = cts;
        CancellationToken ct = cts.Token;
        SetBusy(true);
        SetStatus(_t["status_analyzing"]);
        SetProgress(null);

        var installProgress = new Progress<double>(f => SetStatus(_t.T("status_ytdlp_installing", f.ToString("P0"))));
        string? lastStem = null;

        try
        {
            ProbeResult probe = await _s.YtDlp.ProbeAsync(url, fullPlaylist: false, installProgress, ct);

            IReadOnlyList<PlaylistEntry> targets;
            string singleUrl = YouTubeUrl.Clean(url);
            if (probe.IsPlaylist)
            {
                Dialogs.PlaylistChoice choice =
                    Dialogs.ChoosePlaylistMode(this, _t, probe.Title ?? "Playlist");
                if (choice == Dialogs.PlaylistChoice.Cancel)
                {
                    return;
                }

                if (choice == Dialogs.PlaylistChoice.FullPlaylist)
                {
                    ProbeResult full = await _s.YtDlp.ProbeAsync(url, fullPlaylist: true, installProgress, ct);
                    targets = full.Entries;
                }
                else
                {
                    targets = new[] { new PlaylistEntry(singleUrl, string.Empty) };
                }
            }
            else
            {
                targets = new[] { new PlaylistEntry(singleUrl, string.Empty) };
            }

            for (int i = 0; i < targets.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                PlaylistEntry entry = targets[i];
                if (targets.Count > 1)
                {
                    SetStatus(_t.T("status_processing", i + 1, targets.Count, Shorten(entry.Title, 60)));
                }

                var progress = new Progress<DownloadProgress>(p =>
                {
                    SetProgress(p.Fraction);
                    if (p.Fraction is { } f && targets.Count == 1)
                    {
                        SetStatus(_t.T("status_downloading", f.ToString("P1")));
                    }
                });

                DownloadedAudio audio = await _s.YtDlp.DownloadAsync(entry.Url, progress, installProgress, ct);

                SetStatus(_t["status_converting"]);
                SetProgress(null);
                string songsDir = _engine.SongsDir;
                string stem = await Task.Run(
                    () => _s.Importer.Import(audio.FilePath, audio.Title, songsDir, deleteSource: true, ct), ct);
                lastStem = stem;
                await Task.Run(() => _engine.AddAndSelect(stem), ct);
            }

            _txtUrl.Clear();
        }
        catch (OperationCanceledException)
        {
            // user cancelled
        }
        catch (YtDlpException ex)
        {
            _s.Log.Error($"yt-dlp failed (exit {ex.ExitCode}): {ex.Details}");
            MessageBox.Show(
                this, _t.T("err_download", ex.Details.Length > 0 ? ex.Details : ex.Message), "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        catch (UnsupportedAudioFormatException ex)
        {
            _s.Log.Warn(ex.Message);
            MessageBox.Show(
                this,
                _t.T("err_unsupported", Path.GetExtension(ex.FilePath).ToUpperInvariant() is { Length: > 0 } e ? e : "?"),
                "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidDataException or IOException
                                       or System.ComponentModel.Win32Exception or TaskCanceledException)
        {
            _s.Log.Error("Download failed", ex);
            MessageBox.Show(this, _t.T("err_ytdlp", ex.Message), "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _operationCts = null;
            SongImporter.CleanDownloads(_s.Paths.DownloadsDir, _s.Log);
            SetBusy(false);
        }

        ShowResult(lastStem);
    }

    private void ShowResult(string? stem)
    {
        if (stem is not null)
        {
            _lblStatus.Text = _t.T("status_success", stem + SongNames.Extension);
            _statusTimer.Stop();
            _statusTimer.Start();
        }
        else
        {
            _lblStatus.Text = _t["status_ready"];
        }
    }

    private void CancelOperation()
    {
        _operationCts?.Cancel();
        SetStatus(_t["status_cancelling"]);
    }

    private static string Shorten(string text, int max) => text.Length <= max ? text : text[..max];

    // ---- diagnostics -----------------------------------------------------------------------------

    private async Task CopyReportAsync()
    {
        bool present = File.Exists(_s.Paths.YtDlpExe);
        string? version = null;
        if (present)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                version = await _s.YtDlp.GetVersionAsync(cts.Token);
            }
            catch (Exception ex) when (ex is OperationCanceledException or System.ComponentModel.Win32Exception
                                           or InvalidOperationException)
            {
                _s.Log.Warn($"Could not read the yt-dlp version: {ex.Message}");
            }
        }

        string? csgo = _s.Config.CsgoDir;
        string? relay = _engine.RelayCfgPath;
        var snapshot = new DiagnosticSnapshot(
            Language: _t.Language,
            CsgoDirConfigured: !string.IsNullOrEmpty(csgo),
            CsgoExeFound: GameFiles.LooksLikeCsgoDir(csgo),
            CsgoDir: csgo,
            SongsDirExists: Directory.Exists(_engine.SongsDir),
            SongsDir: _engine.SongsDir,
            SongCount: _engine.Songs.Count,
            SteamFound: _engine.FindSteamExe() is not null,
            RelayCfgExists: relay is not null && File.Exists(relay),
            GoamRunning: _engine.IsRunning,
            YtDlpPresent: present,
            YtDlpVersion: version,
            StartWithWindows: _s.Config.StartWithWindows,
            LogTail: _s.Log.Tail(40));

        string report = DiagnosticReport.Build(snapshot, Redactor.ForCurrentUser(_s.Paths));
        try
        {
            Clipboard.SetText(report);
            MessageBox.Show(this, _t["ok_report_copied"], "OK", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (System.Runtime.InteropServices.ExternalException ex)
        {
            _s.Log.Warn($"Clipboard unavailable: {ex.Message}");
            MessageBox.Show(this, report, "GOAM", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    // ---- tray / lifetime -------------------------------------------------------------------------

    private void EnsureTray()
    {
        if (_tray is not null)
        {
            return;
        }

        _tray = new NotifyIcon
        {
            Icon = _icon ?? SystemIcons.Application,
            Text = _t["tray_tooltip"],
            Visible = false,
        };
        _tray.DoubleClick += (_, _) => RestoreFromTray();
        BuildTrayMenu();
    }

    private void BuildTrayMenu()
    {
        if (_tray is null)
        {
            return;
        }

        var menu = new ContextMenuStrip();
        menu.Items.Add(_t["tray_show"], null, (_, _) => RestoreFromTray());
        menu.Items.Add(_t["tray_quit"], null, (_, _) => Close());
        ContextMenuStrip? old = _tray.ContextMenuStrip;
        _tray.ContextMenuStrip = menu;
        old?.Dispose();
    }

    private void OnResizeForTray(object? sender, EventArgs e)
    {
        if (WindowState == FormWindowState.Minimized && _s.Config.MinimizeToTray)
        {
            EnsureTray();
            _tray!.Visible = true;
            Hide();
        }
    }

    private void OnTrayOptionChanged()
    {
        if (!_s.Config.MinimizeToTray && _tray is not null)
        {
            _tray.Visible = false;
        }
    }

    private void RestoreFromTray()
    {
        _suppressInitialShow = false;
        if (!Visible)
        {
            Show();
        }

        if (WindowState == FormWindowState.Minimized)
        {
            WindowState = FormWindowState.Normal;
        }

        Activate();
        BringToFront();
        if (_tray is not null)
        {
            _tray.Visible = false;
        }
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        _operationCts?.Cancel();
        _showWait?.Unregister(null);
        _volumeTimer.Stop();
        _statusTimer.Stop();

        try
        {
            _s.Preview.Stop();
            _engine.Shutdown(); // removes voice_input.wav and the generated cfgs, like GOAM 1.x
        }
        catch (Exception ex)
        {
            _s.Log.Error("Shutdown error", ex);
        }

        if (_tray is not null)
        {
            _tray.Visible = false;
            _tray.Dispose();
            _tray = null;
        }

        _s.Log.Info("GOAM closed.");
    }
}
