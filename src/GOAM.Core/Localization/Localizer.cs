using System.Globalization;

namespace Goam.Core.Localization;

/// <summary>
/// ES/EN strings. Keys are the same ones GOAM 1.x used so translations are easy to diff.
/// Text starting with <c>cfg_</c> ends up inside CS:GO .cfg files and is kept ASCII on purpose.
/// </summary>
public sealed class Localizer
{
    public const string Spanish = "es";
    public const string English = "en";

    private string _language;

    public Localizer(string? language = null)
    {
        _language = Normalize(language) ?? DetectSystemLanguage();
    }

    public string Language
    {
        get => _language;
        set => _language = Normalize(value) ?? English;
    }

    public static IReadOnlyList<string> SupportedLanguages { get; } = new[] { Spanish, English };

    public static string? Normalize(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return null;
        }

        string l = language.Trim().ToLowerInvariant();
        return l.StartsWith("es", StringComparison.Ordinal) ? Spanish
            : l.StartsWith("en", StringComparison.Ordinal) ? English
            : null;
    }

    public static string DetectSystemLanguage()
    {
        try
        {
            return Normalize(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName) ?? English;
        }
        catch (CultureNotFoundException)
        {
            return English;
        }
    }

    public string Toggle()
    {
        Language = _language == Spanish ? English : Spanish;
        return _language;
    }

    /// <summary>Looks a key up (falling back to English, then to the key itself) and formats it.</summary>
    public string T(string key, params object[] args)
    {
        string text = Lookup(key);
        return args.Length == 0 ? text : string.Format(CultureInfo.InvariantCulture, text, args);
    }

    public string this[string key] => Lookup(key);

    private string Lookup(string key)
    {
        if (Tables.TryGetValue(_language, out var table) && table.TryGetValue(key, out string? s))
        {
            return s;
        }

        if (Tables[English].TryGetValue(key, out s))
        {
            return s;
        }

        return key;
    }

    /// <summary>Keys present in English but missing in another language (used by tests).</summary>
    internal static IEnumerable<string> MissingKeys(string language) =>
        Tables[English].Keys.Where(k => !Tables[language].ContainsKey(k));

    internal static IReadOnlyDictionary<string, string> Table(string language) => Tables[language];

    private static readonly Dictionary<string, Dictionary<string, string>> Tables = new()
    {
        [Spanish] = new Dictionary<string, string>
        {
            ["title"] = "GOAM - Global Offensive Audio Mixer",
            ["subtitle"] = "Global Offensive Audio Mixer",
            ["goam_active"] = "● GOAM ACTIVO",
            ["goam_stopped"] = "● GOAM DETENIDO",
            ["track_label"] = "Track: —",
            ["track_label_fmt"] = "Track: {0} — {1}",
            ["btn_stop_goam"] = "⏹  Detener GOAM",
            ["btn_start_goam"] = "▶  Iniciar GOAM",
            ["volume_label"] = "Volumen: {0}%  ({1:+0.0;-0.0;+0.0} dB)",
            ["yt_label"] = "Link de YouTube:",
            ["yt_placeholder"] = "https://youtube.com/watch?v=...",
            ["btn_download"] = "Descargar y Convertir",
            ["btn_import"] = "Importar archivo(s) local(es)",
            ["btn_cancel"] = "Cancelar",
            ["status_ready"] = "Listo para descargar...",
            ["status_analyzing"] = "Analizando link...",
            ["status_downloading"] = "Descargando... {0}",
            ["status_converting"] = "Convirtiendo a formato CS:GO Legacy...",
            ["status_importing"] = "Importando {0}/{1} - {2}",
            ["status_processing"] = "Procesando {0}/{1} - {2}",
            ["status_cancelling"] = "Cancelando operación...",
            ["status_converted"] = "Convertido: {0}",
            ["status_success"] = "¡Éxito! {0}",
            ["status_ytdlp_installing"] = "Instalando yt-dlp... {0}",
            ["status_ytdlp_updating"] = "Actualizando yt-dlp...",
            ["songs_title"] = "Canciones procesadas",
            ["no_songs"] = "Aún no hay canciones procesadas",
            ["col_song"] = "Canción",
            ["btn_select"] = "Seleccionar",
            ["btn_active"] = "Activo",
            ["btn_rename"] = "Renombrar",
            ["btn_delete"] = "Eliminar",
            ["btn_options"] = "Opciones",
            ["btn_play_key"] = "Tecla ▶: [{0}]",
            ["btn_copy_report"] = "Copiar informe",
            ["dlg_press_key"] = "Presiona una tecla...",
            ["opt_title"] = "Opciones",
            ["opt_relay_key"] = "Tecla Relay: [{0}]",
            ["opt_csgo_dir"] = "Carpeta CS:GO: {0}",
            ["opt_songs_dir"] = "Carpeta Songs: {0}",
            ["opt_reload"] = "Recargar canciones desde la carpeta Songs",
            ["opt_reset"] = "Eliminar toda la configuración guardada",
            ["opt_startup"] = "Iniciar con Windows",
            ["opt_start_min"] = "Iniciar minimizado",
            ["opt_start_min_win"] = "Iniciar minimizado solo con Windows",
            ["opt_tray"] = "Minimizar al tray",
            ["opt_report"] = "Informe de diagnóstico (para pegar en un issue)",
            ["btn_change_relay"] = "Cambiar tecla Relay",
            ["btn_change_csgo"] = "Cambiar directorio",
            ["btn_change_songs"] = "Cambiar carpeta",
            ["btn_reload"] = "Recargar",
            ["btn_reset_cfg"] = "Resetear config",
            ["btn_close"] = "Cerrar",
            ["dlg_play_key_title"] = "Tecla para Reproducir",
            ["dlg_relay_title"] = "Tecla Relay",
            ["dlg_relay_msg"] = "Tecla actual: {0}\n\nEscribe la nueva tecla (ej: =, kp_ins, mouse4):",
            ["dlg_csgo_title"] = "Directorio CS:GO",
            ["dlg_songs_title"] = "Carpeta de canciones",
            ["dlg_rename_title"] = "Renombrar",
            ["dlg_rename_msg"] = "Nuevo nombre (sin .wav):",
            ["dlg_reset_title"] = "Resetear configuración",
            ["dlg_reset_msg"] = "¿Estás seguro? Se borrará toda la configuración guardada.\nEl programa se cerrará.",
            ["dlg_delete_title"] = "Eliminar",
            ["dlg_delete_msg"] = "¿Eliminar la canción?\n\n{0}",
            ["dlg_playlist_title"] = "Playlist detectada",
            ["dlg_playlist_msg"] = "Se detectó una playlist",
            ["btn_single_video"] = "Solo este Video",
            ["btn_full_playlist"] = "Toda la Playlist",
            ["dlg_initial_songs"] = "¿Querés usar una carpeta personalizada para las canciones?\n(Si elegís No, se usará la carpeta 'Songs' predeterminada)",
            ["dlg_initial_csgo"] = "Configura el directorio de CS:GO",
            ["dlg_initial_title"] = "Configuración inicial",
            ["dlg_songs_custom"] = "Selecciona la carpeta donde guardar tus canciones",
            ["dlg_songs_retry"] = "No seleccionaste ninguna carpeta.\n¿Querés intentarlo de nuevo?\n(Si elegís No, se usará la carpeta 'Songs' predeterminada)",
            ["dlg_browse"] = "Examinar...",
            ["dlg_csgo_browse"] = "Buscar csgo.exe",
            ["dlg_songs_browse"] = "Seleccionar carpeta de canciones",
            ["dlg_path_label"] = "Ruta:",
            ["btn_confirm"] = "Confirmar",
            ["ok_relay_key"] = "Tecla Relay actualizada: {0}",
            ["ok_csgo_dir"] = "Directorio CS:GO actualizado correctamente.",
            ["ok_songs_dir"] = "Carpeta de canciones actualizada correctamente.",
            ["ok_reload"] = "Lista recargada ({0} canciones)",
            ["ok_reset"] = "Configuración reseteada. Cerrando...",
            ["ok_play_key"] = "Tecla Play actualizada: {0}",
            ["ok_report_copied"] = "Informe copiado al portapapeles.",
            ["err_invalid_link"] = "Ingresa un enlace válido de YouTube",
            ["err_invalid_csgo"] = "La ruta no es válida o no contiene csgo.exe",
            ["err_invalid_songs"] = "La carpeta seleccionada no es válida.",
            ["err_invalid_csgo2"] = "La ruta no contiene csgo.exe.\nPodés configurarla desde Opciones.",
            ["err_invalid_key"] = "Tecla no válida: {0}",
            ["err_invalid_name"] = "Nombre no válido.",
            ["err_name_exists"] = "Ya existe una canción con ese nombre.",
            ["err_delete"] = "No se pudo eliminar:\n{0}",
            ["err_rename"] = "No se pudo renombrar:\n{0}",
            ["err_critical"] = "Error crítico",
            ["err_unsupported"] = "Formato no compatible: {0}\nFormatos soportados: MP3, WAV, FLAC, OGG",
            ["err_download"] = "No se pudo descargar el audio:\n{0}",
            ["err_ytdlp"] = "No se pudo preparar yt-dlp:\n{0}",
            ["err_no_steam"] = "No se encontró steam.exe",
            ["err_launch"] = "No se pudo iniciar CS:GO:\n{0}",
            ["warn_invalid_link"] = "Link inválido",
            ["btn_launch_csgo"] = "Iniciar CS:GO",
            ["cfg_play_hint"] = "Presiona {0} para reproducir  |  li = lista  |  np = cancion actual",
            ["cfg_tracks_header"] = "Lista de canciones GOAM:",
            ["cfg_now_playing"] = "Reproduciendo: {0}",
            ["cfg_track_selected"] = "Sonido seleccionado:",
            ["tray_show"] = "Mostrar",
            ["tray_quit"] = "Salir",
            ["tray_tooltip"] = "GOAM",
        },
        [English] = new Dictionary<string, string>
        {
            ["title"] = "GOAM - Global Offensive Audio Mixer",
            ["subtitle"] = "Global Offensive Audio Mixer",
            ["goam_active"] = "● GOAM ACTIVE",
            ["goam_stopped"] = "● GOAM STOPPED",
            ["track_label"] = "Track: —",
            ["track_label_fmt"] = "Track: {0} — {1}",
            ["btn_stop_goam"] = "⏹  Stop GOAM",
            ["btn_start_goam"] = "▶  Start GOAM",
            ["volume_label"] = "Volume: {0}%  ({1:+0.0;-0.0;+0.0} dB)",
            ["yt_label"] = "YouTube Link:",
            ["yt_placeholder"] = "https://youtube.com/watch?v=...",
            ["btn_download"] = "Download & Convert",
            ["btn_import"] = "Import local file(s)",
            ["btn_cancel"] = "Cancel",
            ["status_ready"] = "Ready to download...",
            ["status_analyzing"] = "Analyzing link...",
            ["status_downloading"] = "Downloading... {0}",
            ["status_converting"] = "Converting to CS:GO Legacy format...",
            ["status_importing"] = "Importing {0}/{1} - {2}",
            ["status_processing"] = "Processing {0}/{1} - {2}",
            ["status_cancelling"] = "Cancelling operation...",
            ["status_converted"] = "Converted: {0}",
            ["status_success"] = "Success! {0}",
            ["status_ytdlp_installing"] = "Installing yt-dlp... {0}",
            ["status_ytdlp_updating"] = "Updating yt-dlp...",
            ["songs_title"] = "Processed songs",
            ["no_songs"] = "No songs processed yet",
            ["col_song"] = "Song",
            ["btn_select"] = "Select",
            ["btn_active"] = "Active",
            ["btn_rename"] = "Rename",
            ["btn_delete"] = "Delete",
            ["btn_options"] = "Options",
            ["btn_play_key"] = "Key ▶: [{0}]",
            ["btn_copy_report"] = "Copy report",
            ["dlg_press_key"] = "Press a key...",
            ["opt_title"] = "Options",
            ["opt_relay_key"] = "Relay Key: [{0}]",
            ["opt_csgo_dir"] = "CS:GO Folder: {0}",
            ["opt_songs_dir"] = "Songs Folder: {0}",
            ["opt_reload"] = "Reload songs from Songs folder",
            ["opt_reset"] = "Delete all saved configuration",
            ["opt_startup"] = "Start with Windows",
            ["opt_start_min"] = "Start minimized",
            ["opt_start_min_win"] = "Start minimized only with Windows",
            ["opt_tray"] = "Minimize to tray",
            ["opt_report"] = "Diagnostic report (to paste in an issue)",
            ["btn_change_relay"] = "Change Relay key",
            ["btn_change_csgo"] = "Change directory",
            ["btn_change_songs"] = "Change folder",
            ["btn_reload"] = "Reload",
            ["btn_reset_cfg"] = "Reset config",
            ["btn_close"] = "Close",
            ["dlg_play_key_title"] = "Play Key",
            ["dlg_relay_title"] = "Relay Key",
            ["dlg_relay_msg"] = "Current key: {0}\n\nType the new key (e.g.: =, kp_ins, mouse4):",
            ["dlg_csgo_title"] = "CS:GO Directory",
            ["dlg_songs_title"] = "Songs folder",
            ["dlg_rename_title"] = "Rename",
            ["dlg_rename_msg"] = "New name (without .wav):",
            ["dlg_reset_title"] = "Reset configuration",
            ["dlg_reset_msg"] = "Are you sure? All saved configuration will be deleted.\nThe program will close.",
            ["dlg_delete_title"] = "Delete",
            ["dlg_delete_msg"] = "Delete this song?\n\n{0}",
            ["dlg_playlist_title"] = "Playlist detected",
            ["dlg_playlist_msg"] = "A playlist was detected",
            ["btn_single_video"] = "Single Video",
            ["btn_full_playlist"] = "Full Playlist",
            ["dlg_initial_songs"] = "Would you like to use a custom folder for your songs?\n(If you choose No, the default 'Songs' folder will be used)",
            ["dlg_initial_csgo"] = "Configure your CS:GO directory",
            ["dlg_initial_title"] = "Initial setup",
            ["dlg_songs_custom"] = "Select the folder where you want to save your songs",
            ["dlg_songs_retry"] = "No folder was selected.\nWould you like to try again?\n(If you choose No, the default 'Songs' folder will be used)",
            ["dlg_browse"] = "Browse...",
            ["dlg_csgo_browse"] = "Find csgo.exe",
            ["dlg_songs_browse"] = "Select songs folder",
            ["dlg_path_label"] = "Path:",
            ["btn_confirm"] = "Confirm",
            ["ok_relay_key"] = "Relay key updated: {0}",
            ["ok_csgo_dir"] = "CS:GO directory updated successfully.",
            ["ok_songs_dir"] = "Songs folder updated successfully.",
            ["ok_reload"] = "List reloaded ({0} songs)",
            ["ok_reset"] = "Configuration reset. Closing...",
            ["ok_play_key"] = "Play key updated: {0}",
            ["ok_report_copied"] = "Report copied to the clipboard.",
            ["err_invalid_link"] = "Enter a valid YouTube link",
            ["err_invalid_csgo"] = "Path is invalid or does not contain csgo.exe",
            ["err_invalid_songs"] = "The selected folder is not valid.",
            ["err_invalid_csgo2"] = "Path does not contain csgo.exe.\nYou can configure it from Options.",
            ["err_invalid_key"] = "Invalid key: {0}",
            ["err_invalid_name"] = "Invalid name.",
            ["err_name_exists"] = "A song with that name already exists.",
            ["err_delete"] = "Could not delete:\n{0}",
            ["err_rename"] = "Could not rename:\n{0}",
            ["err_critical"] = "Critical error",
            ["err_unsupported"] = "Unsupported format: {0}\nSupported formats: MP3, WAV, FLAC, OGG",
            ["err_download"] = "Could not download the audio:\n{0}",
            ["err_ytdlp"] = "Could not prepare yt-dlp:\n{0}",
            ["err_no_steam"] = "Could not find steam.exe",
            ["err_launch"] = "Could not launch CS:GO:\n{0}",
            ["warn_invalid_link"] = "Invalid link",
            ["btn_launch_csgo"] = "Launch CS:GO",
            ["cfg_play_hint"] = "Press {0} to play  |  li = song list  |  np = current song",
            ["cfg_tracks_header"] = "GOAM song list:",
            ["cfg_now_playing"] = "Now playing: {0}",
            ["cfg_track_selected"] = "Sound selected:",
            ["tray_show"] = "Show",
            ["tray_quit"] = "Quit",
            ["tray_tooltip"] = "GOAM",
        },
    };
}
