using Goam.Core;
using Goam.Core.Audio;
using Goam.Core.Config;
using Goam.Core.Diagnostics;
using Goam.Core.Game;
using Goam.Core.Localization;
using Goam.Core.Platform;
using Goam.Core.Songs;
using Goam.Core.YouTube;

namespace Goam.App;

/// <summary>Composition root: builds every service once so forms receive ready-made dependencies.</summary>
internal sealed class AppServices : IDisposable
{
    private readonly Lazy<HttpClient> _http;
    private readonly Lazy<YtDlpManager> _ytDlpManager;
    private readonly Lazy<YtDlpClient> _ytDlp;

    public AppServices(AppPaths paths, bool launchedFromStartup)
    {
        Paths = paths;
        LaunchedFromStartup = launchedFromStartup;
        Log = new FileLog(paths.LogFile);

        Store = new ConfigStore(paths, Log);
        ConfigLoadResult loaded = Store.Load();
        Config = loaded.Config;
        IsFirstRun = loaded.IsFirstRun;
        MigratedFromLegacy = loaded.MigratedFromLegacy;

        Localizer = new Localizer(Config.Language);
        Steam = new SteamLocator();
        Decoder = new AudioDecoder();
        Converter = new AudioConverter(Decoder, Log);
        Importer = new SongImporter(Converter, Log);
        Engine = new GoamEngine(Config, Store, paths, Localizer, Steam, Converter, Log);
        Preview = new PreviewPlayer(Decoder, Log);
        Startup = StartupRegistrationFactory.Create(paths);

        _http = new Lazy<HttpClient>(() => YtDlpManager.CreateHttpClient(DiagnosticReport.AppVersion()));
        _ytDlpManager = new Lazy<YtDlpManager>(() => new YtDlpManager(paths, _http.Value, Log));
        _ytDlp = new Lazy<YtDlpClient>(() => new YtDlpClient(_ytDlpManager.Value, paths, Log));
    }

    public AppPaths Paths { get; }
    public bool LaunchedFromStartup { get; }
    public FileLog Log { get; }
    public ConfigStore Store { get; }
    public AppConfig Config { get; }
    public bool IsFirstRun { get; }
    public bool MigratedFromLegacy { get; }
    public Localizer Localizer { get; }
    public ISteamLocator Steam { get; }
    public IAudioDecoder Decoder { get; }
    public AudioConverter Converter { get; }
    public SongImporter Importer { get; }
    public GoamEngine Engine { get; }
    public PreviewPlayer Preview { get; }
    public IStartupRegistration Startup { get; }

    public YtDlpManager YtDlpManager => _ytDlpManager.Value;
    public YtDlpClient YtDlp => _ytDlp.Value;
    public bool YtDlpCreated => _ytDlp.IsValueCreated;

    public void Dispose()
    {
        Preview.Dispose();
        Engine.Dispose();
        if (_http.IsValueCreated)
        {
            _http.Value.Dispose();
        }
    }
}
