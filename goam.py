import os
import sys
import re
import wave
import threading
import locale
import io
import subprocess
import winreg
from pathlib import Path
from tkinter import messagebox, filedialog, simpledialog
import customtkinter as ctk
import miniaudio
import numpy as np
import yt_dlp
from PIL import Image
import pystray

# Try to import pygame for song preview playback
try:
    import pygame  # pyright: ignore[reportMissingImports]

    pygame.mixer.init()
    PYGAME_AVAILABLE = True
except Exception:
    PYGAME_AVAILABLE = False


# =========================
# BASE DIRECTORIES
# Determine where the app is running from (frozen .exe or dev script)
# and set the AppData folder used for config and other persistent files.
# When frozen with PyInstaller --onefile, _MEIPASS points to the temporary
# extraction folder where bundled data files (like goam.ico) are unpacked.
# =========================
if getattr(sys, "frozen", False):
    base_dir = Path(sys.executable).parent
    _bundle_dir = Path(sys._MEIPASS)  # type: ignore[attr-defined]
else:
    base_dir = Path(os.path.abspath(__file__)).parent
    _bundle_dir = base_dir

appdata_dir = Path(os.environ.get("APPDATA", base_dir)) / "GOAM"
appdata_dir.mkdir(parents=True, exist_ok=True)

print("[GOAM] Audio engine: miniaudio (no ffmpeg)")

# =========================
# ICON
# Load the tray/window icon from the bundle (_MEIPASS when frozen, script dir
# otherwise). Falls back to a plain green square if the file is not found.
# =========================
ICON_PATH = _bundle_dir / "goam.ico"
if not ICON_PATH.exists():
    ICON_PATH = base_dir / "goam.ico"


def get_tray_image() -> Image.Image:
    """Return the icon image for the system tray. Uses goam.ico if present, else generates one."""
    if ICON_PATH.exists():
        return Image.open(str(ICON_PATH)).resize((64, 64))
    return Image.new("RGBA", (64, 64), (26, 92, 26, 255))


# =========================
# LOCALIZATION STRINGS
# All user-visible text in both Spanish and English.
# Keys are referenced via T() throughout the code.
# =========================
STRINGS = {
    "es": {
        "title": "GOAM - Global Offensive Audio Mixer",
        "subtitle": "Global Offensive Audio Mixer",
        "goam_active": "● GOAM ACTIVO",
        "goam_stopped": "● GOAM DETENIDO",
        "track_label": "Track: —",
        "track_label_fmt": "Track: {} — {}",
        "btn_stop_goam": "⏹  Detener GOAM",
        "btn_start_goam": "▶  Iniciar GOAM",
        "volume_label": "Volumen: {}%  ({:+.1f} dB)",
        "yt_label": "Link de YouTube:",
        "yt_placeholder": "https://youtube.com/watch?v=...",
        "btn_download": "Descargar y Convertir",
        "btn_import": "Importar archivo(s) local(es)",
        "btn_cancel": "Cancelar",
        "status_ready": "Listo para descargar...",
        "status_analyzing": "Analizando link...",
        "status_downloading": "Descargando... {}",
        "status_converting": "Convirtiendo a formato CS:GO Legacy...",
        "status_importing": "Importando {}/{} - {}",
        "status_processing": "Procesando {}/{} - {}",
        "status_cancelling": "Cancelando operacion...",
        "status_converted": "Convertido: {}",
        "status_success": "Exito! {}",
        "songs_title": "Canciones procesadas",
        "no_songs": "Aun no hay canciones procesadas",
        "btn_select": "Seleccionar",
        "btn_active": "Activo",
        "btn_rename": "Renombrar",
        "btn_delete": "Eliminar",
        "btn_options": "Opciones",
        "btn_play_key": "Tecla ▶: [{}]",
        "dlg_press_key": "Presiona una tecla...",
        "opt_title": "Opciones",
        "opt_relay_key": "Tecla Relay: [{}]",
        "opt_csgo_dir": "Carpeta CS:GO: {}",
        "opt_songs_dir": "Carpeta Songs: {}",
        "opt_reload": "Recargar canciones desde la carpeta Songs",
        "opt_reset": "Eliminar toda la configuracion guardada",
        "opt_startup": "Iniciar con Windows",
        "opt_start_min": "Iniciar minimizado",
        "opt_start_min_win": "Iniciar minimizado solo con Windows",
        "opt_tray": "Minimizar al tray",
        "btn_change_relay": "Cambiar tecla Relay",
        "btn_change_csgo": "Cambiar directorio",
        "btn_change_songs": "Cambiar carpeta",
        "btn_reload": "Recargar",
        "btn_reset_cfg": "Resetear config",
        "btn_close": "Cerrar",
        "dlg_play_key_title": "Tecla para Reproducir",
        "dlg_relay_title": "Tecla Relay",
        "dlg_relay_msg": "Tecla actual: {}\n\nEscribe la nueva tecla (ej: =, kp_ins, mouse4):",
        "dlg_csgo_title": "Directorio CS:GO",
        "dlg_songs_title": "Carpeta de canciones",
        "dlg_rename_title": "Renombrar",
        "dlg_rename_msg": "Nuevo nombre (sin .wav):",
        "dlg_reset_title": "Resetear configuracion",
        "dlg_reset_msg": "Estas seguro? Se borrara toda la configuracion guardada.\nEl programa se cerrara.",
        "dlg_delete_title": "Eliminar",
        "dlg_delete_msg": "Eliminar la cancion?\n\n{}",
        "dlg_playlist_title": "Playlist detectada",
        "dlg_playlist_msg": "Se detecto una playlist",
        "btn_single_video": "Solo este Video",
        "btn_full_playlist": "Toda la Playlist",
        "dlg_initial_songs": "Queres usar una carpeta personalizada para las canciones?\n(Si elegiis No, se usara la carpeta 'Songs' predeterminada)",
        "dlg_initial_csgo": "Configura el directorio de CS:GO",
        "dlg_initial_title": "Configuracion inicial",
        "dlg_songs_custom": "Selecciona la carpeta donde guardar tus canciones",
        "dlg_songs_retry": "No seleccionaste ninguna carpeta.\nQueres intentarlo de nuevo?\n(Si elegis No, se usara la carpeta 'Songs' predeterminada)",
        "dlg_browse": "Examinar...",
        "dlg_csgo_browse": "Buscar csgo.exe",
        "dlg_songs_browse": "Seleccionar carpeta de canciones",
        "dlg_path_label": "Ruta:",
        "btn_confirm": "Confirmar",
        "ok_relay_key": "Tecla Relay actualizada: {}",
        "ok_csgo_dir": "Directorio CS:GO actualizado correctamente.",
        "ok_songs_dir": "Carpeta de canciones actualizada correctamente.",
        "ok_reload": "Lista recargada ({} canciones)",
        "ok_reset": "Configuracion reseteada. Cerrando...",
        "ok_play_key": "Tecla Play actualizada: {}",
        "err_invalid_link": "Ingresa un enlace valido de YouTube",
        "err_invalid_csgo": "La ruta no es valida o no contiene csgo.exe",
        "err_invalid_songs": "La carpeta seleccionada no es valida.",
        "err_invalid_csgo2": "La ruta no contiene csgo.exe.\nPodes configurarla desde Opciones.",
        "err_delete": "No se pudo eliminar:\n{}",
        "err_rename": "No se pudo renombrar:\n{}",
        "err_critical": "Error critico",
        "err_unsupported": "Formato no compatible: {}\nFormatos soportados: MP3, WAV, FLAC, OGG",
        "warn_invalid_link": "Link invalido",
        "btn_launch_csgo": "Iniciar CS:GO",
        "cfg_play_hint": "Presiona {} para reproducir  |  li = lista  |  np = cancion actual",
        "cfg_tracks_header": "Lista de canciones GOAM:",
        "cfg_now_playing": "Reproduciendo: {}",
        "cfg_track_selected": "Sonido seleccionado:",
        "tray_show": "Mostrar",
        "tray_quit": "Salir",
        "tray_tooltip": "GOAM",
    },
    "en": {
        "title": "GOAM - Global Offensive Audio Mixer",
        "subtitle": "Global Offensive Audio Mixer",
        "goam_active": "● GOAM ACTIVE",
        "goam_stopped": "● GOAM STOPPED",
        "track_label": "Track: —",
        "track_label_fmt": "Track: {} — {}",
        "btn_stop_goam": "⏹  Stop GOAM",
        "btn_start_goam": "▶  Start GOAM",
        "volume_label": "Volume: {}%  ({:+.1f} dB)",
        "yt_label": "YouTube Link:",
        "yt_placeholder": "https://youtube.com/watch?v=...",
        "btn_download": "Download & Convert",
        "btn_import": "Import local file(s)",
        "btn_cancel": "Cancel",
        "status_ready": "Ready to download...",
        "status_analyzing": "Analyzing link...",
        "status_downloading": "Downloading... {}",
        "status_converting": "Converting to CS:GO Legacy format...",
        "status_importing": "Importing {}/{} - {}",
        "status_processing": "Processing {}/{} - {}",
        "status_cancelling": "Cancelling operation...",
        "status_converted": "Converted: {}",
        "status_success": "Success! {}",
        "songs_title": "Processed songs",
        "no_songs": "No songs processed yet",
        "btn_select": "Select",
        "btn_active": "Active",
        "btn_rename": "Rename",
        "btn_delete": "Delete",
        "btn_options": "Options",
        "btn_play_key": "Key ▶: [{}]",
        "dlg_press_key": "Press a key...",
        "opt_title": "Options",
        "opt_relay_key": "Relay Key: [{}]",
        "opt_csgo_dir": "CS:GO Folder: {}",
        "opt_songs_dir": "Songs Folder: {}",
        "opt_reload": "Reload songs from Songs folder",
        "opt_reset": "Delete all saved configuration",
        "opt_startup": "Start with Windows",
        "opt_start_min": "Start minimized",
        "opt_start_min_win": "Start minimized only with Windows",
        "opt_tray": "Minimize to tray",
        "btn_change_relay": "Change Relay key",
        "btn_change_csgo": "Change directory",
        "btn_change_songs": "Change folder",
        "btn_reload": "Reload",
        "btn_reset_cfg": "Reset config",
        "btn_close": "Close",
        "dlg_play_key_title": "Play Key",
        "dlg_relay_title": "Relay Key",
        "dlg_relay_msg": "Current key: {}\n\nType the new key (e.g.: =, kp_ins, mouse4):",
        "dlg_csgo_title": "CS:GO Directory",
        "dlg_songs_title": "Songs folder",
        "dlg_rename_title": "Rename",
        "dlg_rename_msg": "New name (without .wav):",
        "dlg_reset_title": "Reset configuration",
        "dlg_reset_msg": "Are you sure? All saved configuration will be deleted.\nThe program will close.",
        "dlg_delete_title": "Delete",
        "dlg_delete_msg": "Delete this song?\n\n{}",
        "dlg_playlist_title": "Playlist detected",
        "dlg_playlist_msg": "A playlist was detected",
        "btn_single_video": "Single Video",
        "btn_full_playlist": "Full Playlist",
        "dlg_initial_songs": "Would you like to use a custom folder for your songs?\n(If you choose No, the default 'Songs' folder will be used)",
        "dlg_initial_csgo": "Configure your CS:GO directory",
        "dlg_initial_title": "Initial setup",
        "dlg_songs_custom": "Select the folder where you want to save your songs",
        "dlg_songs_retry": "No folder was selected.\nWould you like to try again?\n(If you choose No, the default 'Songs' folder will be used)",
        "dlg_browse": "Browse...",
        "dlg_csgo_browse": "Find csgo.exe",
        "dlg_songs_browse": "Select songs folder",
        "dlg_path_label": "Path:",
        "btn_confirm": "Confirm",
        "ok_relay_key": "Relay key updated: {}",
        "ok_csgo_dir": "CS:GO directory updated successfully.",
        "ok_songs_dir": "Songs folder updated successfully.",
        "ok_reload": "List reloaded ({} songs)",
        "ok_reset": "Configuration reset. Closing...",
        "ok_play_key": "Play key updated: {}",
        "err_invalid_link": "Enter a valid YouTube link",
        "err_invalid_csgo": "Path is invalid or does not contain csgo.exe",
        "err_invalid_songs": "The selected folder is not valid.",
        "err_invalid_csgo2": "Path does not contain csgo.exe.\nYou can configure it from Options.",
        "err_delete": "Could not delete:\n{}",
        "err_rename": "Could not rename:\n{}",
        "err_critical": "Critical error",
        "err_unsupported": "Unsupported format: {}\nSupported formats: MP3, WAV, FLAC, OGG",
        "warn_invalid_link": "Invalid link",
        "btn_launch_csgo": "Launch CS:GO",
        "cfg_play_hint": "Press {} to play  |  li = song list  |  np = current song",
        "cfg_tracks_header": "GOAM song list:",
        "cfg_now_playing": "Now playing: {}",
        "cfg_track_selected": "Sound selected:",
        "tray_show": "Show",
        "tray_quit": "Quit",
        "tray_tooltip": "GOAM",
    },
}


def detect_language() -> str:
    """Detect the system language and return 'es' or 'en'."""
    try:
        lang = locale.getdefaultlocale()[0] or ""
        if lang.lower().startswith("es"):
            return "es"
    except Exception:
        pass
    return "en"


# =========================
# CONFIGURATION
# Reads/writes a simple key="value" plain-text file in AppData.
# All settings are loaded at startup and saved on every change.
# =========================
config_file = appdata_dir / "config"
config_keys = [
    "csgo_dir",
    "goam_play_key",
    "goam_relay_key",
    "volume_percent",
    "songs_dir",
    "language",
    "start_with_windows",
    "start_minimized",
    "start_minimized_windows",
    "minimize_to_tray",
]
config_file.touch(exist_ok=True)


def parse_config_line(line: str):
    """Parse a single 'key=\"value\"' config line. Returns (key, value) or (None, None)."""
    if "=" not in line:
        return None, None
    raw_key, raw_value = line.split("=", 1)
    raw_key = raw_key.strip()
    # Exact match prevents 'start_minimized' from matching 'start_minimized_windows'
    key = next((k for k in config_keys if k == raw_key), None)
    if not key:
        return None, None
    match = re.search(r'"([^"]*)"', raw_value)
    return key, (match.group(1) if match else None)


config = {k: None for k in config_keys}
for line in config_file.read_text().splitlines():
    k, v = parse_config_line(line)
    if k:
        config[k] = v


def save_config():
    """Write all current config values to the config file."""
    lines = [f'{k}="{config[k]}"' for k in config_keys if config.get(k) is not None]
    config_file.write_text("\n".join(lines))


# Apply defaults for any missing config values
DEFAULT_VOLUME = 70
if not config.get("volume_percent"):
    config["volume_percent"] = str(DEFAULT_VOLUME)
if not config.get("goam_play_key"):
    config["goam_play_key"] = "x"
if not config.get("goam_relay_key"):
    config["goam_relay_key"] = "="
if not config.get("language"):
    config["language"] = detect_language()
if config.get("start_with_windows") is None:
    config["start_with_windows"] = "0"
if config.get("start_minimized") is None:
    config["start_minimized"] = "0"
if config.get("start_minimized_windows") is None:
    config["start_minimized_windows"] = "0"
if config.get("minimize_to_tray") is None:
    config["minimize_to_tray"] = "0"

# Active language, validated against available STRINGS
current_lang = config["language"] if config["language"] in STRINGS else "en"


def T(key: str, *args) -> str:
    """Return the localized string for the given key, formatted with optional args."""
    s = STRINGS[current_lang].get(key, STRINGS["en"].get(key, key))
    return s.format(*args) if args else s


# =========================
# SONGS DIRECTORY
# Resolved from config, or defaults to a 'Songs' subfolder next to the script/exe.
# =========================
is_first_run = config.get("songs_dir") is None

if config.get("songs_dir"):
    songs_dir = Path(config["songs_dir"])
    if not songs_dir.exists():
        songs_dir.mkdir(parents=True, exist_ok=True)
else:
    songs_dir = base_dir / "Songs"
    songs_dir.mkdir(parents=True, exist_ok=True)

# =========================
# CS:GO DIRECTORY
# Path to the CS:GO installation, used to write cfg files and voice_input.wav.
# =========================
csgo_dir = Path(config["csgo_dir"]) if config.get("csgo_dir") else None
if csgo_dir and not csgo_dir.is_dir():
    csgo_dir = None


def get_csgo_paths():
    """Return the four CS:GO cfg paths, or (None, None, None, None) if csgo_dir is not set."""
    if not csgo_dir:
        return None, None, None, None
    cfg_dir = csgo_dir / "csgo" / "cfg"
    cfg_dir.mkdir(parents=True, exist_ok=True)
    return (
        cfg_dir / "goam.cfg",
        cfg_dir / "goam_tracks.cfg",
        cfg_dir,
        cfg_dir / "current_track.cfg",
    )


cfg_file_path, cfg_tracks_path, csgo_cfg_dir, current_track_cfg_path = get_csgo_paths()

# =========================
# GLOBAL STATE
# Runtime variables shared across functions.
# =========================
volume_percent = int(config["volume_percent"])
selected_index = 1  # 1-based index of the currently active song
goam_running = True  # Whether GOAM is actively watching for relay changes
program_running = True  # Set to False on close to stop background threads
last_mtime = None  # Last known mtime of the relay cfg file
wav_files = []  # Sorted list of Path objects for songs in songs_dir
is_downloading = False  # True while a download/import is in progress
cancel_download = False  # Set to True to signal cancellation of current operation
tray_icon = None  # pystray Icon instance, or None if not created

# Preview playback state
preview_playing = False  # True while a song preview is playing
preview_stem = None  # Stem name of the song currently being previewed
preview_stop_evt = threading.Event()  # Set this event to stop the preview early
preview_thread = None  # Reference to the active preview thread

# =========================
# WINDOWS STARTUP (REGISTRY)
# Adds or removes GOAM from the Windows Run key so it auto-launches at login.
# The --startup argument tells GOAM it was opened by Windows, not the user.
# =========================
REGISTRY_KEY = r"Software\Microsoft\Windows\CurrentVersion\Run"
REGISTRY_NAME = "GOAM"

# True only when launched via the Windows startup registry entry
LAUNCHED_FROM_STARTUP = "--startup" in sys.argv


def set_startup(enabled: bool):
    """Add or remove GOAM from the Windows startup registry key."""
    try:
        exe_path = str(Path(sys.executable))
        key = winreg.OpenKey(
            winreg.HKEY_CURRENT_USER, REGISTRY_KEY, 0, winreg.KEY_SET_VALUE
        )
        if enabled:
            winreg.SetValueEx(
                key, REGISTRY_NAME, 0, winreg.REG_SZ, f'"{exe_path}" --startup'
            )
        else:
            try:
                winreg.DeleteValue(key, REGISTRY_NAME)
            except FileNotFoundError:
                pass
        winreg.CloseKey(key)
    except Exception as e:
        print(f"[GOAM] Startup registry error: {e}")


# =========================
# WAV FILE MANAGEMENT
# List songs and write the three CS:GO cfg files that GOAM uses.
# =========================


def load_wav_files() -> list:
    """Return a sorted list of Path objects for all .wav files in songs_dir."""
    return sorted(
        [f for f in songs_dir.iterdir() if f.suffix.lower() == ".wav"],
        key=lambda x: x.stem.lower(),
    )


def update_tracks_cfg():
    """Refresh wav_files and rewrite goam_tracks.cfg with the current numbered song list."""
    global wav_files
    wav_files = load_wav_files()
    if cfg_tracks_path:
        header = T("cfg_tracks_header")
        lines = [f'echo "{header}"'] + [
            f'echo "{i} - {w.stem}"' for i, w in enumerate(wav_files, 1)
        ]
        cfg_tracks_path.write_text("\n".join(lines) + "\n", encoding="utf-8")
    return wav_files


def write_current_track_cfg(stem: str):
    """Write current_track.cfg with the localized 'now playing' message for the given song."""
    if not current_track_cfg_path:
        return
    try:
        msg = T("cfg_now_playing", stem)
        current_track_cfg_path.write_text(f'echo "{msg}"\n', encoding="utf-8")
    except Exception as e:
        print(f"[GOAM] Error writing current_track.cfg: {e}")


def write_main_cfg():
    """Write goam.cfg: all aliases, the play key bind, the GOAM banner, and per-song aliases."""
    if not cfg_file_path:
        return
    hint_msg = T("cfg_play_hint", config["goam_play_key"])
    cfg_lines = [
        'alias list_tracks "exec goam_tracks.cfg"',
        "alias list list_tracks",
        "alias li list_tracks",
        'alias current_track "exec current_track.cfg"',
        "alias curtrack current_track",
        "alias nowplaying current_track",
        "alias np current_track",
        'alias goam_play "goam_on"',
        'alias goam_on "voice_inputfromfile 1; voice_loopback 1; +voicerecord; alias goam_play goam_off"',
        'alias goam_off "-voicerecord; voice_inputfromfile 0; voice_loopback 0; alias goam_play goam_on"',
        'alias update_cfg "host_writeconfig goam_relay_key"',
        f'bind {config["goam_play_key"]} goam_play',
        "",
        # ASCII art banner shown in the CS:GO console when the cfg loads
        'echo "======================================"',
        'echo "                                     "',
        'echo "  \u2588\u2588\u2588\u2588\u2588\u2588   \u2588\u2588\u2588\u2588\u2588\u2588   \u2588\u2588\u2588\u2588\u2588  \u2588\u2588\u2588    \u2588\u2588\u2588"',
        'echo " \u2588\u2588       \u2588\u2588    \u2588\u2588 \u2588\u2588   \u2588\u2588 \u2588\u2588\u2588\u2588  \u2588\u2588\u2588\u2588"',
        'echo " \u2588\u2588   \u2588\u2588\u2588 \u2588\u2588    \u2588\u2588 \u2588\u2588\u2588\u2588\u2588\u2588\u2588 \u2588\u2588 \u2588\u2588\u2588\u2588 \u2588\u2588"',
        'echo " \u2588\u2588    \u2588\u2588 \u2588\u2588    \u2588\u2588 \u2588\u2588   \u2588\u2588 \u2588\u2588  \u2588\u2588  \u2588\u2588"',
        'echo "  \u2588\u2588\u2588\u2588\u2588\u2588   \u2588\u2588\u2588\u2588\u2588\u2588  \u2588\u2588   \u2588\u2588 \u2588\u2588      \u2588\u2588"',
        'echo "                                     "',
        'echo "======================================"',
        'echo "                                     "',
        f'echo "{hint_msg}"',
        'echo "                                     "',
        'echo "                                     "',
        'echo "                                     "',
        "",
    ]
    selected_prefix = T("cfg_track_selected")
    for i, wav in enumerate(wav_files, 1):
        cfg_lines.append(
            f'alias {i} "bind {config["goam_relay_key"]} {i}; update_cfg; echo {selected_prefix} {wav.stem}"'
        )
    cfg_file_path.write_text("\n".join(cfg_lines) + "\n", encoding="utf-8")


def rebuild_goam():
    """Regenerate all GOAM cfg files and reset the relay file watcher."""
    global last_mtime
    update_tracks_cfg()
    write_main_cfg()
    last_mtime = None


# =========================
# CLEANUP
# Removes GOAM-generated files from the CS:GO directory on exit or when GOAM is stopped.
# =========================


def cleanup_goam():
    """Delete voice_input.wav and all GOAM cfg files from the CS:GO directory."""
    try:
        if csgo_dir:
            (csgo_dir / "voice_input.wav").unlink(missing_ok=True)
        for f in [cfg_file_path, cfg_tracks_path, current_track_cfg_path]:
            if f:
                f.unlink(missing_ok=True)
        print("[GOAM] Files cleaned up.")
    except Exception as e:
        print(f"[GOAM] Cleanup error: {e}")


# =========================
# STEAM USERDATA / RELAY
# Finds the Steam userdata folder that contains CS:GO's local config.
# GOAM reads the relay cfg that CS:GO writes when the player changes songs in-game.
# =========================


def find_userdata() -> Path | None:
    """Find the most-recently-used Steam userdata folder that has a CS:GO app folder (4465480)."""
    base = Path("C:/Program Files (x86)/Steam/userdata")
    if not base.exists():
        return None
    users = [u for u in base.iterdir() if (u / "4465480").exists()]
    return max(users, key=lambda x: x.stat().st_mtime) if users else None


userdata = find_userdata()
relay_cfg_path = (
    (userdata / "4465480" / "local" / "cfg" / "goam_relay_key.cfg")
    if userdata
    else None
)

# =========================
# AUDIO ENGINE (miniaudio)
# Low-level audio helpers: decode, apply volume, and write WAV files.
# Supports MP3, WAV, FLAC, OGG with no ffmpeg dependency.
# =========================
SUPPORTED_EXTENSIONS = {".mp3", ".wav", ".flac", ".ogg"}


def _decode_to_raw(input_path: str) -> bytes:
    """Decode a supported audio file to raw PCM: signed 16-bit, mono, 22050 Hz."""
    decoded = miniaudio.decode_file(
        input_path,
        output_format=miniaudio.SampleFormat.SIGNED16,
        nchannels=1,
        sample_rate=22050,
    )
    return decoded.samples.tobytes()


def _apply_db(raw: bytes, db: float) -> bytes:
    """Apply a dB gain/attenuation to raw int16 PCM samples using numpy."""
    if db == 0.0:
        return raw
    factor = 10.0 ** (db / 20.0)
    arr = np.frombuffer(raw, dtype=np.int16).astype(np.float32)
    arr = np.clip(arr * factor, -32768, 32767).astype(np.int16)
    return arr.tobytes()


def _write_wav(dest, raw: bytes):
    """Write raw int16 PCM data as a WAV file (22050 Hz, mono) to a file path or buffer."""
    with wave.open(dest, "wb") as wf:
        wf.setnchannels(1)
        wf.setsampwidth(2)
        wf.setframerate(22050)
        wf.writeframes(raw)


def convert_to_csgo_wav(input_path: str, output_path: str) -> bool:
    """Convert any supported audio file to a CS:GO-compatible WAV (22050 Hz, mono, 16-bit).
    Returns True on success, False if the format is unsupported or an error occurs."""
    try:
        raw = _decode_to_raw(input_path)
        _write_wav(output_path, raw)
        return True
    except miniaudio.DecodeError:
        return False
    except Exception as e:
        print(f"[GOAM] Audio conversion error: {e}")
        return False


# =========================
# VOLUME
# Maps a 0-200% slider value to a dB adjustment applied when writing audio.
# Values above 100% apply positive gain (up to +8 dB).
# =========================


def get_db_adjust(percent: int) -> float:
    """Convert a volume percentage (0-200) to a dB gain value."""
    if percent <= 0:
        return -100.0
    if percent > 100:
        return (percent - 100) / 100.0 * 8.0
    if percent >= 80:
        return -25.0 - (100 - percent) * (5.0 / 20.0)
    return -30.0 - (80 - percent) * (2.5 / 10.0)


# =========================
# SONG SELECTION
# Decodes the selected song, applies volume adjustment, and writes voice_input.wav
# to the CS:GO directory so the game plays it as microphone input.
# =========================


def copy_wav(index: int):
    """Set the active song by index, apply volume, and write voice_input.wav for CS:GO."""
    global selected_index
    selected_index = index
    if not wav_files or index < 1 or index > len(wav_files):
        print("[GOAM] Invalid song index")
        return
    if not csgo_dir:
        print("[GOAM] CS:GO directory not configured")
        return
    src = wav_files[index - 1]
    target = csgo_dir / "voice_input.wav"
    try:
        raw = _decode_to_raw(str(src))
        db_adjust = get_db_adjust(volume_percent)
        adjusted = _apply_db(raw, db_adjust)
        _write_wav(str(target), adjusted)
        print(f"[GOAM] Playing: {src.stem} → {volume_percent}% ({db_adjust:+.1f} dB)")
        write_current_track_cfg(src.stem)
    except Exception as e:
        print(f"[GOAM] Error writing voice_input.wav: {e}")
        try:
            target.write_bytes(src.read_bytes())
            write_current_track_cfg(src.stem)
        except Exception as e2:
            print(f"[GOAM] Fallback copy failed: {e2}")
    app.after(0, update_song_list)


# =========================
# RELAY LOOP
# Background thread that polls the relay cfg file written by CS:GO.
# When the file changes, reads the selected track index and calls copy_wav.
# Uses a threading.Event for instant shutdown without blocking on sleep.
# =========================
_relay_stop_event = threading.Event()


def get_index_from_relay() -> int:
    """Parse the relay cfg file and return the track index bound to the relay key."""
    if not relay_cfg_path or not relay_cfg_path.exists():
        return selected_index
    try:
        content = relay_cfg_path.read_text(errors="ignore").strip()
        m = re.search(
            rf'bind\s+["\']?{re.escape(config["goam_relay_key"])}["\']?\s+["\']?(\d+)["\']?',
            content,
            re.IGNORECASE,
        )
        if m:
            return int(m.group(1))
    except Exception:
        pass
    return selected_index


def relay_loop():
    """Watch the relay cfg for changes and update the active song when it changes."""
    global last_mtime, program_running
    while not _relay_stop_event.wait(timeout=0.2):
        if not program_running:
            break
        try:
            if goam_running and relay_cfg_path and relay_cfg_path.exists():
                mtime = relay_cfg_path.stat().st_mtime
                if last_mtime is None or mtime != last_mtime:
                    last_mtime = mtime
                    idx = get_index_from_relay()
                    copy_wav(idx)
        except Exception as e:
            if program_running:
                print(f"[GOAM] Relay loop error: {e}")


def get_initial_track() -> int:
    """Read the relay cfg at startup to restore the previously active track index."""
    if not relay_cfg_path or not relay_cfg_path.exists():
        return 1
    try:
        content = relay_cfg_path.read_text(errors="ignore").strip()
        m = re.search(
            rf'bind\s+["\']?{re.escape(config["goam_relay_key"])}["\']?\s+["\']?(\d+)["\']?',
            content,
            re.IGNORECASE,
        )
        if m:
            idx = int(m.group(1))
            wvs = load_wav_files()
            if 1 <= idx <= len(wvs):
                print(f"[GOAM] Restoring track from relay: {idx}")
                return idx
    except Exception:
        pass
    return 1


# =========================
# GUI HELPERS
# Small utility functions used across the GUI layer.
# =========================


def center_window(window):
    """Center a tkinter/CTk window on the screen."""
    window.update_idletasks()
    x = (window.winfo_screenwidth() // 2) - (window.winfo_width() // 2)
    y = (window.winfo_screenheight() // 2) - (window.winfo_height() // 2)
    window.geometry(f"+{x}+{y}")


def is_valid_youtube_url(url: str) -> bool:
    """Return True if the given string looks like a valid YouTube URL."""
    if not url:
        return False
    return bool(
        re.compile(
            r"^(https?://)?(www\.)?(youtube\.com|youtu\.be|music\.youtube\.com)/.+"
        ).match(url.strip())
    )


def clean_youtube_url(url: str) -> str:
    """Strip a YouTube URL down to just the video ID portion."""
    match = re.search(r"(?:v=|youtu\.be/)([A-Za-z0-9_-]{11})", url)
    return f"https://www.youtube.com/watch?v={match.group(1)}" if match else url


def clean_temp_files():
    """Delete leftover temporary download files from the songs directory."""
    temp_ext = (".webm", ".mp4", ".m4a", ".aac", ".part", ".ytdl", ".tmp")
    for f in os.listdir(songs_dir):
        if f.lower().endswith(temp_ext):
            try:
                os.remove(os.path.join(songs_dir, f))
            except Exception:
                pass


# =========================
# SYSTEM TRAY
# Creates and manages the pystray icon shown in the Windows notification area.
# =========================


def create_tray():
    """Create the system tray icon and run it in a background daemon thread."""
    global tray_icon
    # Use callables for menu labels so they reflect the current language
    # every time the context menu is opened, instead of being frozen at
    # creation time.
    menu = pystray.Menu(
        pystray.MenuItem(lambda item: T("tray_show"), on_tray_show, default=True),
        pystray.MenuItem(lambda item: T("tray_quit"), on_tray_quit),
    )
    tray_icon = pystray.Icon("GOAM", get_tray_image(), T("tray_tooltip"), menu)
    threading.Thread(target=tray_icon.run, daemon=True).start()


def on_tray_show(icon=None, item=None):
    """Tray callback: restore the main window.
    Must NOT call tray_icon.stop() from inside the pystray callback thread —
    doing so deadlocks. Instead we schedule _show_window on the tk main loop,
    which runs stop() safely from a separate thread after the callback returns.
    """
    app.after(0, _show_window)


def on_tray_quit(icon=None, item=None):
    """Tray callback: quit the application."""
    app.after(0, on_close)


def _destroy_tray():
    """Stop and remove the system tray icon from a non-tray thread."""
    global tray_icon
    if tray_icon is not None:
        icon = tray_icon
        tray_icon = None  # clear reference first so no re-entrancy
        threading.Thread(target=icon.stop, daemon=True).start()


def _show_window():
    """Restore and focus the main window, then destroy the tray icon."""
    app.deiconify()
    app.lift()
    app.focus_force()
    _destroy_tray()


def minimize_window():
    """Minimize to tray or taskbar, depending on config."""
    if config.get("minimize_to_tray") == "1":
        app.withdraw()
        if tray_icon is None:
            create_tray()
    else:
        app.iconify()


# =========================
# INTERFACE RESET
# Re-enables download/import buttons and resets the status label
# after any download, import, or cancellation completes.
# =========================


def reset_interface(success_msg: str = None):
    """Restore the UI to idle state after a download or import operation."""
    global is_downloading
    is_downloading = False
    download_button.configure(state="normal")
    import_button.configure(state="normal")
    cancel_button.configure(state="disabled")
    if success_msg:
        url_entry.delete(0, "end")
        status_label.configure(text=success_msg)
        app.after(2800, lambda: status_label.configure(text=T("status_ready")))
    else:
        status_label.configure(text=T("status_ready"))
    progress_label.configure(text="")


# =========================
# SONG PREVIEW
# Plays the first PREVIEW_SECONDS seconds of a WAV file using miniaudio.
# Clicking the same song again stops it; clicking a different one switches.
# Volume is applied to match the main GOAM volume setting.
# =========================
PREVIEW_SECONDS = 5


def _stop_preview_engine():
    """Signal the preview thread to stop via the stop event (miniaudio handles its own cleanup)."""
    pass  # stopping is handled by setting preview_stop_evt in preview_song()


def preview_song(wav_path: Path, btn_ref):
    """Preview a song for up to PREVIEW_SECONDS seconds. Clicking again stops it."""
    global preview_playing, preview_stem, preview_stop_evt, preview_thread

    stem = wav_path.stem

    # Clicking the currently playing song stops it
    if preview_playing and preview_stem == stem:
        preview_stop_evt.set()
        preview_playing = False
        preview_stem = None
        app.after(0, update_song_list)
        return

    # Capture old thread reference and signal it to stop
    old_thread = preview_thread
    preview_stop_evt.set()

    # Each play session gets its own event so its finally block can check
    # whether it is still the active session before touching global state.
    my_evt = threading.Event()
    preview_stop_evt = my_evt
    preview_playing = True
    preview_stem = stem
    app.after(0, update_song_list)

    def _play():
        global preview_playing, preview_stem
        # Wait for the old thread to fully exit (including its pygame.stop)
        # before we touch pygame — runs here in background, UI stays responsive.
        if old_thread and old_thread.is_alive():
            old_thread.join(timeout=1.0)
        try:
            if PYGAME_AVAILABLE:
                if volume_percent <= 100:
                    pygame_vol = volume_percent / 100.0
                    pygame.mixer.music.load(str(wav_path))
                    pygame.mixer.music.set_volume(pygame_vol)
                    pygame.mixer.music.play()
                else:
                    # Above 100%: apply gain with miniaudio+numpy into a memory buffer
                    db_adjust = get_db_adjust(volume_percent)
                    raw = _decode_to_raw(str(wav_path))
                    adjusted = _apply_db(raw, db_adjust)
                    buf = io.BytesIO()
                    _write_wav(buf, adjusted)
                    buf.seek(0)
                    pygame.mixer.music.load(buf)
                    pygame.mixer.music.set_volume(1.0)
                    pygame.mixer.music.play()
                my_evt.wait(timeout=PREVIEW_SECONDS)
                pygame.mixer.music.stop()
            else:
                import winsound

                winsound.PlaySound(
                    str(wav_path), winsound.SND_FILENAME | winsound.SND_ASYNC
                )
                my_evt.wait(timeout=PREVIEW_SECONDS)
                winsound.PlaySound(None, winsound.SND_PURGE)
        except Exception as e:
            print(f"[GOAM] Preview error: {e}")
        finally:
            # Only reset global state if no newer preview has started since us.
            # If another song was clicked, preview_stop_evt already points to a
            # different event — leave it alone so the new session shows correctly.
            if preview_stop_evt is my_evt:
                preview_playing = False
                preview_stem = None
                app.after(0, update_song_list)

    preview_thread = threading.Thread(target=_play, daemon=True)
    preview_thread.start()


# =========================
# SONG LIST (widget cache)
# Avoids flickering by reusing widgets instead of destroying and recreating them.
# Rows are built once per song and stored in _song_row_cache.
# Updates only change widget properties (text, color, command).
# =========================
_song_row_cache = {}  # Maps song filename -> dict of its widget references


def _build_song_row(song: str, i: int) -> dict:
    """Create all widgets for a song row, pack them, cache and return the widget dict."""
    frame = ctk.CTkFrame(song_list_frame, fg_color="transparent", corner_radius=8)
    frame.pack(fill="x", padx=10, pady=3)

    select_btn = ctk.CTkButton(frame, width=110, height=28, font=ctk.CTkFont(size=12))
    select_btn.pack(side="right", padx=4)

    rename_btn = ctk.CTkButton(
        frame,
        width=85,
        height=28,
        font=ctk.CTkFont(size=12),
        command=lambda s=song: rename_song(s),
    )
    rename_btn.pack(side="right", padx=4)

    delete_btn = ctk.CTkButton(
        frame,
        width=75,
        height=28,
        fg_color="#C42B2B",
        hover_color="#A02020",
        font=ctk.CTkFont(size=12),
        command=lambda s=song: delete_song(s),
    )
    delete_btn.pack(side="right", padx=4)

    song_path = songs_dir / song
    prev_btn = ctk.CTkButton(frame, width=34, height=28, font=ctk.CTkFont(size=13))
    prev_btn.configure(command=lambda p=song_path, b=prev_btn: preview_song(p, b))
    prev_btn.pack(side="left", padx=(10, 4), pady=6)

    label = ctk.CTkLabel(frame, anchor="w", font=ctk.CTkFont(size=13))
    label.pack(side="left", padx=(0, 10), pady=6)

    _song_row_cache[song] = {
        "frame": frame,
        "prev_btn": prev_btn,
        "label": label,
        "select_btn": select_btn,
        "rename_btn": rename_btn,
        "delete_btn": delete_btn,
    }
    return _song_row_cache[song]


def _update_song_row(song: str, i: int, widgets: dict):
    """Refresh the visual state of an existing song row without recreating any widgets."""
    is_active = i == selected_index
    song_path = songs_dir / song
    is_previewing = preview_playing and preview_stem == song_path.stem

    widgets["frame"].configure(fg_color="#1a3a1a" if is_active else "transparent")

    sel_color = "#1a5c1a" if is_active else "#2C5F2E"
    widgets["select_btn"].configure(
        text=T("btn_active") if is_active else T("btn_select"),
        fg_color=sel_color,
        hover_color="#1e7a20",
        state="disabled" if is_active else "normal",
        command=lambda idx=i: copy_wav(idx),
    )
    widgets["rename_btn"].configure(text=T("btn_rename"))
    widgets["delete_btn"].configure(text=T("btn_delete"))

    prev_icon = "⏹" if is_previewing else "▶"
    prev_color = "#8B0000" if is_previewing else ("#1a5c1a" if is_active else "#2a4a7a")
    prev_hover = "#6B0000" if is_previewing else ("#1e7a20" if is_active else "#1e5090")
    widgets["prev_btn"].configure(
        text=prev_icon,
        fg_color=prev_color,
        hover_color=prev_hover,
        command=lambda p=song_path, b=widgets["prev_btn"]: preview_song(p, b),
    )

    label_color = "#4ade80" if is_active else "white"
    label_weight = "bold" if is_active else "normal"
    widgets["label"].configure(
        text=f"{i}. {song.replace('.wav', '')}",
        text_color=label_color,
        font=ctk.CTkFont(size=13, weight=label_weight),
    )


def update_song_list():
    """Refresh the song list UI: add new rows, remove stale ones, update all visual states."""
    global wav_files
    wav_files = load_wav_files()

    songs = sorted(
        [f for f in os.listdir(songs_dir) if f.lower().endswith(".wav")],
        key=lambda x: x.lower(),
    )

    # Remove cached rows for songs that no longer exist on disk
    removed = [s for s in _song_row_cache if s not in songs]
    for s in removed:
        try:
            _song_row_cache[s]["frame"].destroy()
        except Exception:
            pass
        del _song_row_cache[s]

    if not songs:
        for w in song_list_frame.winfo_children():
            w.destroy()
        _song_row_cache.clear()
        ctk.CTkLabel(song_list_frame, text=T("no_songs"), text_color="gray").pack(
            pady=20
        )
        update_track_label()
        return

    # Create rows for any songs not yet in the cache
    for i, song in enumerate(songs, 1):
        if song not in _song_row_cache:
            _build_song_row(song, i)

    # Re-pack all rows in correct sorted order without destroying them
    for song in songs:
        _song_row_cache[song]["frame"].pack_forget()
    for song in songs:
        _song_row_cache[song]["frame"].pack(fill="x", padx=10, pady=3)

    # Update visual state of every row
    for i, song in enumerate(songs, 1):
        _update_song_row(song, i, _song_row_cache[song])

    update_track_label()


def update_track_label():
    """Update the 'Track: N — name' label in the main control bar."""
    if wav_files and 1 <= selected_index <= len(wav_files):
        name = wav_files[selected_index - 1].stem
        track_label.configure(text=T("track_label_fmt", selected_index, name))
    else:
        track_label.configure(text=T("track_label"))


# =========================
# SONG ACTIONS
# Delete and rename songs from the songs directory.
# =========================


def delete_song(filename: str):
    """Confirm with the user, then permanently delete the song from songs_dir."""
    if messagebox.askyesno(T("dlg_delete_title"), T("dlg_delete_msg", filename)):
        try:
            os.remove(os.path.join(songs_dir, filename))
            update_tracks_cfg()
            write_main_cfg()
            update_song_list()
        except Exception as e:
            messagebox.showerror("Error", T("err_delete", e))


def rename_song(current_filename: str):
    """Prompt for a new name and rename the song file on disk."""
    new_name = simpledialog.askstring(
        T("dlg_rename_title"),
        T("dlg_rename_msg"),
        initialvalue=current_filename.replace(".wav", ""),
    )
    if not new_name or not new_name.strip():
        return
    new_filename = new_name.strip() + ".wav"
    if new_filename == current_filename:
        return
    try:
        os.rename(
            os.path.join(songs_dir, current_filename),
            os.path.join(songs_dir, new_filename),
        )
        update_tracks_cfg()
        write_main_cfg()
        update_song_list()
    except Exception as e:
        messagebox.showerror("Error", T("err_rename", e))


# =========================
# AUDIO PROCESSING
# Converts a supported audio file to a CS:GO WAV and copies it to songs_dir.
# is_temp=True  → source is a temporary yt-dlp download; delete after conversion.
# is_temp=False → source is a user's local file; never delete it, only copy/convert.
# After importing, automatically selects the new song in the list.
# =========================


def process_audio(input_path: str, original_title: str, is_temp: bool = False):
    """Convert and import an audio file into GOAM's songs directory."""
    try:
        status_label.configure(text=T("status_converting"))
        app.update_idletasks()

        safe_title = "".join(c if c.isalnum() else "_" for c in original_title).strip(
            "_"
        )[:100]
        output_path = os.path.join(songs_dir, f"{safe_title}.wav")

        ok = convert_to_csgo_wav(input_path, output_path)

        if not ok:
            ext = Path(input_path).suffix.lower()
            if is_temp and os.path.exists(input_path) and input_path != output_path:
                try:
                    os.remove(input_path)
                except Exception:
                    pass
            messagebox.showerror("Error", T("err_unsupported", ext.upper() or "?"))
            reset_interface()
            return

        # Only delete the source file if it was a temporary download
        if is_temp and os.path.exists(input_path) and input_path != output_path:
            try:
                os.remove(input_path)
            except Exception:
                pass

        update_tracks_cfg()
        write_main_cfg()

        # Auto-select the newly added song (find its position in the sorted list)
        new_wav = safe_title + ".wav"
        songs_sorted = sorted(
            [f for f in os.listdir(songs_dir) if f.lower().endswith(".wav")],
            key=lambda x: x.lower(),
        )
        if new_wav in songs_sorted:
            new_idx = songs_sorted.index(new_wav) + 1
            app.after(0, lambda idx=new_idx: copy_wav(idx))

        app.after(0, update_song_list)
        reset_interface(T("status_success", new_wav))

    except Exception as e:
        if "cancelled" not in str(e).lower() and "cancelada" not in str(e).lower():
            messagebox.showerror("Error", str(e))
        reset_interface()


# =========================
# YOUTUBE DOWNLOAD
# Downloads audio from YouTube using yt-dlp and imports it into GOAM.
# Prefers formats miniaudio can decode directly (mp3, ogg, flac) to avoid needing ffmpeg.
# =========================


def download_progress_hook(d: dict):
    """yt-dlp progress callback: updates the status label and handles cancellation."""
    global cancel_download
    if cancel_download and d["status"] == "downloading":
        raise Exception("Cancelled")
    if d["status"] == "downloading":
        try:
            percent = d.get("_percent_str", "0%").strip()
            status_label.configure(text=T("status_downloading", percent))
            app.update_idletasks()
        except Exception:
            pass


def detect_playlist(url: str) -> tuple:
    """Quickly check if a URL is a playlist. Returns (is_playlist, title)."""
    try:
        with yt_dlp.YoutubeDL(
            {
                "quiet": True,
                "no_warnings": True,
                "extract_flat": True,
                "playlistend": 3,
                "skip_download": True,
            }
        ) as ydl:
            info = ydl.extract_info(url, download=False)
            if info and isinstance(info, dict) and len(info.get("entries", [])) > 1:
                return True, info.get("title", "Playlist")
        return False, None
    except Exception as e:
        print(f"[GOAM] Playlist detection error: {e}")
        return False, None


def run_youtube_download(url: str, download_playlist: bool):
    """Run the yt-dlp download for one video or a full playlist, then import each song."""
    global is_downloading, cancel_download
    cancel_download = False
    try:
        download_button.configure(state="disabled")
        import_button.configure(state="disabled")
        cancel_button.configure(state="normal")

        ydl_format = (
            "bestaudio[ext=mp3]/bestaudio[ext=ogg]/bestaudio[ext=flac]/bestaudio"
        )

        if not download_playlist:
            url = clean_youtube_url(url)
            ydl_opts = {
                "format": ydl_format,
                "outtmpl": os.path.join(songs_dir, "%(title)s.%(ext)s"),
                "quiet": False,
                "progress_hooks": [download_progress_hook],
                "noplaylist": True,
                "postprocessors": [],  # no ffmpeg postprocessing
            }
            with yt_dlp.YoutubeDL(ydl_opts) as ydl:
                info = ydl.extract_info(url, download=True)
                downloaded_file = ydl.prepare_filename(info)
            process_audio(downloaded_file, info.get("title", "song"), is_temp=True)
        else:
            with yt_dlp.YoutubeDL(
                {"quiet": True, "extract_flat": True, "skip_download": True}
            ) as ydl:
                info = ydl.extract_info(url, download=False)
            entries = info.get("entries", []) if info else []
            for i, entry in enumerate(entries, 1):
                if cancel_download:
                    break
                video_url = (
                    entry.get("url") or f"https://youtube.com/watch?v={entry.get('id')}"
                )
                title = entry.get("title", f"Song_{i}")
                status_label.configure(
                    text=T("status_processing", i, len(entries), title[:60])
                )
                app.update_idletasks()
                with yt_dlp.YoutubeDL(
                    {
                        "format": ydl_format,
                        "outtmpl": os.path.join(songs_dir, "%(title)s.%(ext)s"),
                        "quiet": False,
                        "progress_hooks": [download_progress_hook],
                        "noplaylist": True,
                        "postprocessors": [],
                    }
                ) as ydl:
                    song_info = ydl.extract_info(video_url, download=True)
                    downloaded_file = ydl.prepare_filename(song_info)
                process_audio(
                    downloaded_file, song_info.get("title", title), is_temp=True
                )
    except Exception as e:
        if "cancelled" not in str(e).lower() and "cancelada" not in str(e).lower():
            messagebox.showerror("Error", str(e))
    finally:
        reset_interface()


def show_playlist_dialog(playlist_name: str, url: str):
    """Show a dialog asking whether to download a single video or the full playlist."""
    dialog = ctk.CTkToplevel(app)
    dialog.title(T("dlg_playlist_title"))
    dialog.geometry("540x260")
    dialog.resizable(False, False)
    dialog.grab_set()
    center_window(dialog)

    ctk.CTkLabel(
        dialog, text=T("dlg_playlist_msg"), font=ctk.CTkFont(size=18, weight="bold")
    ).pack(pady=(25, 10))
    ctk.CTkLabel(
        dialog, text=playlist_name, font=ctk.CTkFont(size=13), wraplength=500
    ).pack(pady=(0, 25))

    bf = ctk.CTkFrame(dialog, fg_color="transparent")
    bf.pack(pady=10)

    def choose(download_all: bool):
        dialog.destroy()
        threading.Thread(
            target=run_youtube_download, args=(url, download_all), daemon=True
        ).start()

    def cancel_dialog():
        dialog.destroy()
        global is_downloading
        is_downloading = False
        reset_interface()

    ctk.CTkButton(
        bf,
        text=T("btn_single_video"),
        width=170,
        height=48,
        command=lambda: choose(False),
    ).grid(row=0, column=0, padx=10)
    ctk.CTkButton(
        bf,
        text=T("btn_full_playlist"),
        width=170,
        height=48,
        command=lambda: choose(True),
    ).grid(row=0, column=1, padx=10)
    ctk.CTkButton(
        bf,
        text=T("btn_cancel"),
        fg_color="#C42B2B",
        width=130,
        height=48,
        command=cancel_dialog,
    ).grid(row=0, column=2, padx=10)


# =========================
# LOCAL FILE IMPORT
# Lets the user pick local audio files and copies/converts them into songs_dir.
# The original files are never deleted or moved.
# =========================


def import_local_files():
    """Open a file picker and import selected audio files into GOAM without deleting originals."""
    global is_downloading
    if is_downloading:
        return
    files = filedialog.askopenfilenames(
        title=T("dlg_songs_title"),
        filetypes=[
            ("Compatible audio", "*.mp3 *.wav *.flac *.ogg"),
            ("MP3", "*.mp3"),
            ("WAV", "*.wav"),
            ("FLAC", "*.flac"),
            ("OGG", "*.ogg"),
        ],
    )
    if not files:
        return
    is_downloading = True
    download_button.configure(state="disabled")
    import_button.configure(state="disabled")
    cancel_button.configure(state="normal")

    def process_list():
        for i, file_path in enumerate(files, 1):
            if cancel_download:
                break
            title = os.path.splitext(os.path.basename(file_path))[0]
            status_label.configure(
                text=T("status_importing", i, len(files), title[:50])
            )
            app.update_idletasks()
            process_audio(file_path, title, is_temp=False)  # Never delete local files
        reset_interface()

    threading.Thread(target=process_list, daemon=True).start()


# =========================
# MAIN BUTTON CALLBACKS
# =========================


def on_download_click():
    """Validate the YouTube URL and kick off the download thread."""
    global is_downloading
    if is_downloading:
        return
    url = url_entry.get().strip()
    if not is_valid_youtube_url(url):
        messagebox.showwarning(T("warn_invalid_link"), T("err_invalid_link"))
        return
    is_downloading = True
    status_label.configure(text=T("status_analyzing"))
    is_playlist, playlist_name = detect_playlist(url)
    if is_playlist:
        show_playlist_dialog(playlist_name, url)
    else:
        threading.Thread(
            target=run_youtube_download, args=(url, False), daemon=True
        ).start()


def cancel_operation():
    """Signal the current download or import to stop."""
    global cancel_download
    cancel_download = True
    status_label.configure(text=T("status_cancelling"))


# =========================
# GOAM TOGGLE
# Starts GOAM (writes cfgs, monitors relay) or stops it (cleans up generated files).
# =========================


def toggle_goam():
    """Toggle GOAM between active and stopped states."""
    global goam_running
    goam_running = not goam_running
    if goam_running:
        rebuild_goam()
        toggle_btn.configure(
            text=T("btn_stop_goam"), fg_color="#5c1a1a", hover_color="#521414"
        )
        goam_status_label.configure(text=T("goam_active"), text_color="#4ade80")
    else:
        cleanup_goam()
        toggle_btn.configure(
            text=T("btn_start_goam"), fg_color="#1a5c1a", hover_color="#145214"
        )
        goam_status_label.configure(text=T("goam_stopped"), text_color="#f87171")


def on_volume_change(val):
    """Handle volume slider movement: update state, config, and label."""
    global volume_percent
    volume_percent = int(float(val))
    config["volume_percent"] = str(volume_percent)
    save_config()
    db = get_db_adjust(volume_percent)
    vol_label.configure(text=T("volume_label", volume_percent, db))


# =========================
# LANGUAGE TOGGLE
# Switches between Spanish and English and re-renders all GUI text.
# Also regenerates CS:GO cfgs so their messages use the new language.
# =========================


def toggle_language():
    """Switch the active language between 'es' and 'en' and refresh all UI text."""
    global current_lang
    current_lang = "en" if current_lang == "es" else "es"
    config["language"] = current_lang
    save_config()
    apply_language()


def apply_language():
    """Update every static text widget in the main window to match the current language."""
    app.title(T("title"))
    subtitle_label.configure(text=T("subtitle"))
    goam_status_label.configure(
        text=T("goam_active") if goam_running else T("goam_stopped")
    )
    update_track_label()
    toggle_btn.configure(
        text=T("btn_stop_goam") if goam_running else T("btn_start_goam"),
        fg_color="#5c1a1a" if goam_running else "#1a5c1a",
        hover_color="#521414" if goam_running else "#145214",
    )
    db = get_db_adjust(volume_percent)
    vol_label.configure(text=T("volume_label", volume_percent, db))
    yt_label_widget.configure(text=T("yt_label"))
    url_entry.configure(placeholder_text=T("yt_placeholder"))
    download_button.configure(text=T("btn_download"))
    import_button.configure(text=T("btn_import"))
    cancel_button.configure(text=T("btn_cancel"))
    status_label.configure(text=T("status_ready"))
    songs_title_label.configure(text=T("songs_title"))
    options_btn.configure(text=T("btn_options"))
    launch_csgo_btn.configure(text=T("btn_launch_csgo"))
    play_key_btn.configure(text=T("btn_play_key", config["goam_play_key"]))
    lang_btn.configure(text="ES" if current_lang == "es" else "EN")
    if tray_icon is not None:
        try:
            tray_icon.title = T("tray_tooltip")
        except Exception:
            pass
    if goam_running:
        rebuild_goam()
    update_song_list()


# =========================
# CS:GO DIRECTORY DIALOG
# Custom file dialog for selecting the CS:GO installation folder.
# Can be set as mandatory (no close button) for first-run setup.
# =========================


def _dialog_csgo(parent=None, mandatory: bool = False) -> Path | None:
    """Show a dialog to select the CS:GO folder. Returns Path on confirm, None on cancel."""
    result = {"path": None, "confirmed": False}

    win = ctk.CTkToplevel(parent or app)
    win.title(T("dlg_csgo_title"))
    win.geometry("540x200")
    win.resizable(False, False)
    win.grab_set()
    center_window(win)

    if mandatory:
        win.protocol("WM_DELETE_WINDOW", lambda: None)

    ctk.CTkLabel(
        win, text=T("dlg_initial_csgo"), font=ctk.CTkFont(size=14, weight="bold")
    ).pack(pady=(18, 8), padx=20)

    row = ctk.CTkFrame(win, fg_color="transparent")
    row.pack(fill="x", padx=20, pady=(0, 5))

    entry = ctk.CTkEntry(
        row, height=36, placeholder_text="C:\\...\\Counter-Strike Global Offensive"
    )
    entry.pack(side="left", fill="x", expand=True, padx=(0, 8))

    def browse():
        exe = filedialog.askopenfilename(
            parent=win,
            title=T("dlg_csgo_browse"),
            filetypes=[("csgo.exe", "csgo.exe"), ("Executable", "*.exe")],
        )
        if exe:
            entry.delete(0, "end")
            entry.insert(0, str(Path(exe).parent))

    ctk.CTkButton(row, text=T("dlg_browse"), width=110, height=36, command=browse).pack(
        side="right"
    )

    btn_row = ctk.CTkFrame(win, fg_color="transparent")
    btn_row.pack(pady=12)

    def confirm():
        val = entry.get().strip().strip('"')
        possible = Path(val)
        if possible.is_dir() and (possible / "csgo.exe").exists():
            result["path"] = possible
            result["confirmed"] = True
            win.destroy()
        else:
            messagebox.showerror("Error", T("err_invalid_csgo"), parent=win)

    def cancel():
        win.destroy()

    ctk.CTkButton(
        btn_row, text=T("btn_confirm"), width=120, height=34, command=confirm
    ).pack(side="left", padx=8)
    if not mandatory:
        ctk.CTkButton(
            btn_row,
            text=T("btn_cancel"),
            width=120,
            height=34,
            fg_color="#C42B2B",
            hover_color="#A02020",
            command=cancel,
        ).pack(side="left", padx=8)

    win.wait_window()
    return result["path"] if result["confirmed"] else None


# =========================
# SONGS DIRECTORY DIALOG
# Custom dialog for selecting the songs folder.
# =========================


def _dialog_songs(parent=None) -> Path | None:
    """Show a dialog to select the songs folder. Returns Path on confirm, None on cancel."""
    result = {"path": None, "confirmed": False}

    win = ctk.CTkToplevel(parent or app)
    win.title(T("dlg_songs_title"))
    win.geometry("540x200")
    win.resizable(False, False)
    win.grab_set()
    center_window(win)

    ctk.CTkLabel(
        win, text=T("dlg_songs_custom"), font=ctk.CTkFont(size=14, weight="bold")
    ).pack(pady=(18, 8), padx=20)

    row = ctk.CTkFrame(win, fg_color="transparent")
    row.pack(fill="x", padx=20, pady=(0, 5))

    entry = ctk.CTkEntry(row, height=36, placeholder_text="C:\\...")
    entry.pack(side="left", fill="x", expand=True, padx=(0, 8))

    def browse():
        folder = filedialog.askdirectory(parent=win, title=T("dlg_songs_browse"))
        if folder:
            entry.delete(0, "end")
            entry.insert(0, folder)

    ctk.CTkButton(row, text=T("dlg_browse"), width=110, height=36, command=browse).pack(
        side="right"
    )

    btn_row = ctk.CTkFrame(win, fg_color="transparent")
    btn_row.pack(pady=12)

    def confirm():
        val = entry.get().strip().strip('"')
        possible = Path(val)
        if possible.is_dir():
            result["path"] = possible
            result["confirmed"] = True
            win.destroy()
        else:
            messagebox.showerror("Error", T("err_invalid_songs"), parent=win)

    def cancel():
        win.destroy()

    ctk.CTkButton(
        btn_row, text=T("btn_confirm"), width=120, height=34, command=confirm
    ).pack(side="left", padx=8)
    ctk.CTkButton(
        btn_row,
        text=T("btn_cancel"),
        width=120,
        height=34,
        fg_color="#C42B2B",
        hover_color="#A02020",
        command=cancel,
    ).pack(side="left", padx=8)

    win.wait_window()
    return result["path"] if result["confirmed"] else None


# =========================
# OPTIONS WINDOW
# Settings dialog: relay key, CS:GO folder, songs folder,
# startup/minimize behavior, song reload, and full config reset.
# =========================


def open_options():
    """Open the Options settings dialog."""
    win = ctk.CTkToplevel(app)
    win.title(T("opt_title"))
    win.geometry("520x520")
    win.resizable(True, True)
    win.grab_set()
    center_window(win)

    ctk.CTkLabel(
        win, text=T("opt_title"), font=ctk.CTkFont(size=22, weight="bold")
    ).pack(pady=(20, 15))

    frame = ctk.CTkFrame(win)
    frame.pack(padx=20, pady=5, fill="both", expand=True)

    def row_button(label_text: str, btn_text: str, cmd, btn_color=None):
        """Helper: labeled row with a fixed-width button on the right."""
        row = ctk.CTkFrame(frame, fg_color="transparent")
        row.pack(fill="x", padx=15, pady=5)
        kw = {"fg_color": btn_color} if btn_color else {}
        ctk.CTkButton(row, text=btn_text, width=160, height=32, command=cmd, **kw).pack(
            side="right"
        )
        ctk.CTkLabel(row, text=label_text, font=ctk.CTkFont(size=13), anchor="w").pack(
            side="left", fill="x", expand=True
        )

    def row_toggle(label_text: str, config_key: str, callback=None):
        """Helper: labeled row with a checkbox on the right."""
        row = ctk.CTkFrame(frame, fg_color="transparent")
        row.pack(fill="x", padx=15, pady=5)
        ctk.CTkLabel(row, text=label_text, font=ctk.CTkFont(size=13), anchor="w").pack(
            side="left", fill="x", expand=True
        )
        var = ctk.BooleanVar(value=config.get(config_key) == "1")

        def on_toggle():
            config[config_key] = "1" if var.get() else "0"
            save_config()
            if callback:
                callback(var.get())

        ctk.CTkCheckBox(row, text="", variable=var, command=on_toggle, width=32).pack(
            side="right"
        )

    # ── Relay Key ─────────────────────────────────────────
    def change_relay_key():
        new_key = simpledialog.askstring(
            T("dlg_relay_title"),
            T("dlg_relay_msg", config["goam_relay_key"]),
            parent=win,
        )
        if new_key and new_key.strip():
            config["goam_relay_key"] = new_key.strip().lower()
            save_config()
            rebuild_goam()
            messagebox.showinfo(
                "OK", T("ok_relay_key", config["goam_relay_key"]), parent=win
            )

    row_button(
        T("opt_relay_key", config["goam_relay_key"]),
        T("btn_change_relay"),
        change_relay_key,
    )

    # ── CS:GO Folder ──────────────────────────────────────
    def change_csgo_dir():
        global csgo_dir, cfg_file_path, cfg_tracks_path, csgo_cfg_dir, current_track_cfg_path
        path = _dialog_csgo(parent=win)
        if path:
            csgo_dir = path
            config["csgo_dir"] = str(path)
            save_config()
            cfg_file_path, cfg_tracks_path, csgo_cfg_dir, current_track_cfg_path = (
                get_csgo_paths()
            )
            rebuild_goam()
            messagebox.showinfo("OK", T("ok_csgo_dir"), parent=win)

    dir_full = str(csgo_dir) if csgo_dir else "—"
    row_button(T("opt_csgo_dir", dir_full), T("btn_change_csgo"), change_csgo_dir)

    # ── Songs Folder ──────────────────────────────────────
    def change_songs_dir():
        global songs_dir
        path = _dialog_songs(parent=win)
        if path:
            songs_dir = path
            config["songs_dir"] = str(path)
            save_config()
            rebuild_goam()
            update_song_list()
            messagebox.showinfo("OK", T("ok_songs_dir"), parent=win)

    row_button(
        T("opt_songs_dir", str(songs_dir)), T("btn_change_songs"), change_songs_dir
    )

    ctk.CTkFrame(frame, height=1, fg_color="#333").pack(fill="x", padx=15, pady=8)

    # ── Startup toggles ───────────────────────────────────
    row_toggle(
        T("opt_startup"), "start_with_windows", callback=lambda v: set_startup(v)
    )

    # Start minimized options are mutually exclusive
    row_min = ctk.CTkFrame(frame, fg_color="transparent")
    row_min.pack(fill="x", padx=15, pady=5)
    ctk.CTkLabel(
        row_min, text=T("opt_start_min"), font=ctk.CTkFont(size=13), anchor="w"
    ).pack(side="left", fill="x", expand=True)
    var_min = ctk.BooleanVar(value=config.get("start_minimized") == "1")

    row_win = ctk.CTkFrame(frame, fg_color="transparent")
    row_win.pack(fill="x", padx=15, pady=5)
    ctk.CTkLabel(
        row_win, text=T("opt_start_min_win"), font=ctk.CTkFont(size=13), anchor="w"
    ).pack(side="left", fill="x", expand=True)
    var_win = ctk.BooleanVar(value=config.get("start_minimized_windows") == "1")

    def on_toggle_min():
        if var_min.get():
            var_win.set(False)
            config["start_minimized_windows"] = "0"
        config["start_minimized"] = "1" if var_min.get() else "0"
        save_config()

    def on_toggle_win():
        if var_win.get():
            var_min.set(False)
            config["start_minimized"] = "0"
        config["start_minimized_windows"] = "1" if var_win.get() else "0"
        save_config()

    ctk.CTkCheckBox(
        row_min, text="", variable=var_min, command=on_toggle_min, width=32
    ).pack(side="right")
    ctk.CTkCheckBox(
        row_win, text="", variable=var_win, command=on_toggle_win, width=32
    ).pack(side="right")

    row_toggle(
        T("opt_tray"),
        "minimize_to_tray",
        callback=lambda v: (
            _destroy_tray() if not v else None
        ),  # destroy tray when option is disabled
    )

    ctk.CTkFrame(frame, height=1, fg_color="#333").pack(fill="x", padx=15, pady=8)

    # ── Reload / Reset ────────────────────────────────────
    def reload_songs():
        update_tracks_cfg()
        write_main_cfg()
        update_song_list()
        messagebox.showinfo("OK", T("ok_reload", len(wav_files)), parent=win)

    row_button(T("opt_reload"), T("btn_reload"), reload_songs)

    def reset_config():
        if messagebox.askyesno(T("dlg_reset_title"), T("dlg_reset_msg"), parent=win):
            config_file.unlink(missing_ok=True)
            config_file.touch()
            messagebox.showinfo("OK", T("ok_reset"), parent=win)
            win.destroy()
            app.after(500, lambda: sys.exit(0))

    row_button(T("opt_reset"), T("btn_reset_cfg"), reset_config, btn_color="#C42B2B")

    ctk.CTkButton(
        win, text=T("btn_close"), width=120, height=36, command=win.destroy
    ).pack(pady=15)


# =========================
# PLAY KEY CAPTURE
# A small dialog that listens for a physical keypress instead of typed input.
# Maps tkinter keysym names to CS:GO-compatible key name strings.
# =========================


def open_key_capture():
    """Open a dialog that detects a keypress and sets it as the new play key."""
    dlg = ctk.CTkToplevel(app)
    dlg.title(T("dlg_play_key_title"))
    dlg.geometry("300x130")
    dlg.resizable(False, False)
    dlg.grab_set()
    dlg.attributes("-topmost", True)
    center_window(dlg)

    ctk.CTkLabel(
        dlg, text=T("dlg_press_key"), font=ctk.CTkFont(size=16, weight="bold")
    ).pack(expand=True)
    ctk.CTkLabel(
        dlg,
        text=f"(current: {config['goam_play_key']})",
        font=ctk.CTkFont(size=12),
        text_color="gray",
    ).pack(pady=(0, 12))

    # Map tkinter keysym → CS:GO key name
    KEY_MAP = {
        "space": "space",
        "Return": "enter",
        "Tab": "tab",
        "BackSpace": "backspace",
        "Escape": "escape",
        "F1": "f1",
        "F2": "f2",
        "F3": "f3",
        "F4": "f4",
        "F5": "f5",
        "F6": "f6",
        "F7": "f7",
        "F8": "f8",
        "F9": "f9",
        "F10": "f10",
        "F11": "f11",
        "F12": "f12",
        "KP_End": "kp_end",
        "KP_Down": "kp_downarrow",
        "KP_Next": "kp_pgdn",
        "KP_Left": "kp_leftarrow",
        "KP_Begin": "kp_5",
        "KP_Right": "kp_rightarrow",
        "KP_Home": "kp_home",
        "KP_Up": "kp_uparrow",
        "KP_Prior": "kp_pgup",
        "KP_Insert": "kp_ins",
        "KP_Delete": "kp_del",
        "KP_Add": "kp_plus",
        "KP_Subtract": "kp_minus",
        "KP_Multiply": "kp_multiply",
        "KP_Divide": "kp_slash",
        "KP_Enter": "kp_enter",
        "Insert": "ins",
        "Delete": "del",
        "Home": "home",
        "End": "end",
        "Prior": "pgup",
        "Next": "pgdn",
        "Up": "uparrow",
        "Down": "downarrow",
        "Left": "leftarrow",
        "Right": "rightarrow",
    }

    def on_key(event):
        sym = event.keysym
        # Ignore standalone modifier keys
        if sym in (
            "Shift_L",
            "Shift_R",
            "Control_L",
            "Control_R",
            "Alt_L",
            "Alt_R",
            "Super_L",
            "Super_R",
            "Caps_Lock",
        ):
            return
        key = KEY_MAP.get(sym, sym.lower())
        config["goam_play_key"] = key
        save_config()
        rebuild_goam()
        play_key_btn.configure(text=T("btn_play_key", key))
        dlg.destroy()

    dlg.bind("<KeyPress>", on_key)
    dlg.focus_force()


# =========================
# LAUNCH CS:GO
# Finds steam.exe via the Windows registry and launches CS:GO with +exec goam.cfg.
# =========================
CSGO_STEAM_APPID = "4465480"


def find_steam_exe() -> str | None:
    """Locate steam.exe from the Steam install path stored in the Windows registry."""
    try:
        key = winreg.OpenKey(
            winreg.HKEY_LOCAL_MACHINE,
            r"SOFTWARE\WOW6432Node\Valve\Steam",
            0,
            winreg.KEY_READ,
        )
        path, _ = winreg.QueryValueEx(key, "InstallPath")
        winreg.CloseKey(key)
        steam = Path(path) / "steam.exe"
        if steam.exists():
            return str(steam)
    except Exception:
        pass
    fallback = Path("C:/Program Files (x86)/Steam/steam.exe")
    return str(fallback) if fallback.exists() else None


def launch_csgo():
    """Launch CS:GO via steam.exe -applaunch with +exec goam.cfg as a launch option."""
    steam_exe = find_steam_exe()
    if not steam_exe:
        messagebox.showerror("Error", "Could not find steam.exe")
        return
    args = [steam_exe, "-applaunch", CSGO_STEAM_APPID, "+exec", "goam.cfg"]
    try:
        subprocess.Popen(args)
    except Exception as e:
        messagebox.showerror("Error", f"Could not launch CS:GO:\n{e}")


# =========================
# GUI CONSTRUCTION
# Build all main window widgets. Widget variables are module-level so callbacks can reference them.
# =========================
ctk.set_appearance_mode("dark")
ctk.set_default_color_theme("blue")

app = ctk.CTk()
app.title(T("title"))
app.geometry("980x820")
app.resizable(True, True)

if ICON_PATH.exists():
    try:
        app.iconbitmap(str(ICON_PATH))
    except Exception:
        pass

center_window(app)

# Intercept the native minimize button to optionally send to tray
app.bind(
    "<Unmap>",
    lambda e: (
        minimize_window()
        if e.widget is app and config.get("minimize_to_tray") == "1"
        else None
    ),
)

# ── Header ────────────────────────────────────────────
header = ctk.CTkFrame(app, fg_color="transparent")
header.pack(pady=(20, 5), padx=20, fill="x")

ctk.CTkLabel(header, text="GOAM", font=ctk.CTkFont(size=42, weight="bold")).pack(
    side="left"
)

header_right = ctk.CTkFrame(header, fg_color="transparent")
header_right.pack(side="right")

lang_btn = ctk.CTkButton(
    header_right,
    text="ES" if current_lang == "es" else "EN",
    width=60,
    height=38,
    font=ctk.CTkFont(size=13, weight="bold"),
    fg_color="transparent",
    border_width=1,
    command=toggle_language,
)
lang_btn.pack(side="right", padx=(5, 0))

options_btn = ctk.CTkButton(
    header_right,
    text=T("btn_options"),
    width=120,
    height=38,
    font=ctk.CTkFont(size=14),
    command=open_options,
)
options_btn.pack(side="right", padx=5)

# Play key button: shows the current key and opens the key capture dialog on click
play_key_btn = ctk.CTkButton(
    header_right,
    text=T("btn_play_key", config["goam_play_key"]),
    width=110,
    height=38,
    font=ctk.CTkFont(size=13),
    fg_color="#2a4a2a",
    hover_color="#1e3a1e",
    command=open_key_capture,
)
play_key_btn.pack(side="right", padx=(0, 5))

subtitle_label = ctk.CTkLabel(app, text=T("subtitle"), font=ctk.CTkFont(size=15))
subtitle_label.pack(pady=(0, 10))

# ── GOAM Control Bar ──────────────────────────────────
control_bar = ctk.CTkFrame(app, corner_radius=10)
control_bar.pack(pady=5, padx=20, fill="x")

left_ctrl = ctk.CTkFrame(control_bar, fg_color="transparent")
left_ctrl.pack(side="left", padx=15, pady=10, fill="x", expand=True)

goam_status_label = ctk.CTkLabel(
    left_ctrl,
    text=T("goam_active"),
    font=ctk.CTkFont(size=13, weight="bold"),
    text_color="#4ade80",
)
goam_status_label.pack(anchor="w")

track_label = ctk.CTkLabel(
    left_ctrl, text=T("track_label"), font=ctk.CTkFont(size=12), text_color="gray"
)
track_label.pack(anchor="w", pady=(2, 0))

right_ctrl = ctk.CTkFrame(control_bar, fg_color="transparent")
right_ctrl.pack(side="right", padx=15, pady=10)

toggle_btn = ctk.CTkButton(
    right_ctrl,
    text=T("btn_stop_goam"),
    width=180,
    height=44,
    font=ctk.CTkFont(size=15, weight="bold"),
    fg_color="#5c1a1a",
    hover_color="#521414",
    command=toggle_goam,
)
toggle_btn.pack(side="right", padx=(10, 0))

launch_csgo_btn = ctk.CTkButton(
    right_ctrl,
    text=T("btn_launch_csgo"),
    width=160,
    height=44,
    font=ctk.CTkFont(size=14, weight="bold"),
    fg_color="#1a3a6e",
    hover_color="#142d58",
    command=launch_csgo,
)
launch_csgo_btn.pack(side="right", padx=(0, 8))

# ── Volume Slider ─────────────────────────────────────
vol_frame = ctk.CTkFrame(app, corner_radius=10)
vol_frame.pack(pady=5, padx=20, fill="x")

vol_inner = ctk.CTkFrame(vol_frame, fg_color="transparent")
vol_inner.pack(padx=15, pady=10, fill="x")

db_init = get_db_adjust(volume_percent)
vol_label = ctk.CTkLabel(
    vol_inner,
    text=T("volume_label", volume_percent, db_init),
    font=ctk.CTkFont(size=13),
)
vol_label.pack(anchor="w", pady=(0, 5))

vol_slider = ctk.CTkSlider(
    vol_inner, from_=0, to=200, number_of_steps=200, command=on_volume_change
)
vol_slider.set(volume_percent)
vol_slider.pack(fill="x")

# ── YouTube Download Section ──────────────────────────
frame_superior = ctk.CTkFrame(app)
frame_superior.pack(pady=10, padx=20, fill="x")

yt_label_widget = ctk.CTkLabel(
    frame_superior, text=T("yt_label"), font=ctk.CTkFont(size=13)
)
yt_label_widget.pack(anchor="w", padx=10, pady=(10, 5))

url_entry = ctk.CTkEntry(
    frame_superior, height=40, placeholder_text=T("yt_placeholder")
)
url_entry.pack(padx=10, pady=5, fill="x")

btn_frame = ctk.CTkFrame(frame_superior, fg_color="transparent")
btn_frame.pack(pady=12)

download_button = ctk.CTkButton(
    btn_frame,
    text=T("btn_download"),
    font=ctk.CTkFont(size=15),
    height=46,
    width=260,
    command=on_download_click,
)
download_button.grid(row=0, column=0, padx=8)

import_button = ctk.CTkButton(
    btn_frame,
    text=T("btn_import"),
    font=ctk.CTkFont(size=15),
    height=46,
    width=220,
    fg_color="#2C7A3F",
    command=import_local_files,
)
import_button.grid(row=0, column=1, padx=8)

cancel_button = ctk.CTkButton(
    btn_frame,
    text=T("btn_cancel"),
    fg_color="#C42B2B",
    font=ctk.CTkFont(size=14),
    height=46,
    width=130,
    command=cancel_operation,
    state="disabled",
)
cancel_button.grid(row=0, column=2, padx=8)

status_label = ctk.CTkLabel(app, text=T("status_ready"), font=ctk.CTkFont(size=14))
status_label.pack(pady=(8, 0))

progress_label = ctk.CTkLabel(
    app, text="", font=ctk.CTkFont(size=13), text_color="#4ade80"
)
progress_label.pack()

# ── Song List ─────────────────────────────────────────
songs_title_label = ctk.CTkLabel(
    app, text=T("songs_title"), font=ctk.CTkFont(size=18, weight="bold")
)
songs_title_label.pack(anchor="w", padx=30, pady=(15, 5))

song_scroll = ctk.CTkScrollableFrame(app, height=280)
song_scroll.pack(padx=30, pady=5, fill="both", expand=True)
song_list_frame = song_scroll

# =========================
# CLOSE HANDLER
# Signals all background threads to stop, cleans up CS:GO files, then destroys the window.
# No blocking sleep — threads use events for instant response.
# =========================


def on_close():
    """Gracefully shut down GOAM: stop threads, remove generated files, destroy window."""
    global program_running
    program_running = False
    _relay_stop_event.set()
    _destroy_tray()
    cleanup_goam()
    app.quit()
    app.destroy()


app.protocol("WM_DELETE_WINDOW", on_close)

# =========================
# FIRST RUN SETUP
# On first launch, prompt the user to configure the songs folder and CS:GO directory.
# =========================


def request_csgo_dir():
    """Mandatory CS:GO directory prompt — cannot be dismissed without a valid path."""
    global csgo_dir, cfg_file_path, cfg_tracks_path, csgo_cfg_dir, current_track_cfg_path
    path = _dialog_csgo(mandatory=True)
    if path:
        csgo_dir = path
        config["csgo_dir"] = str(path)
        save_config()
        cfg_file_path, cfg_tracks_path, csgo_cfg_dir, current_track_cfg_path = (
            get_csgo_paths()
        )
        rebuild_goam()
        update_song_list()


def request_songs_dir():
    """First-run songs folder setup: ask if the user wants a custom folder, then prompt for it."""
    global songs_dir
    want_custom = messagebox.askyesno(T("dlg_initial_title"), T("dlg_initial_songs"))

    if want_custom:
        while True:
            path = _dialog_songs()
            if path:
                songs_dir = path
                config["songs_dir"] = str(path)
                break
            else:
                retry = messagebox.askyesno(
                    T("dlg_initial_title"), T("dlg_songs_retry")
                )
                if not retry:
                    config["songs_dir"] = str(songs_dir)
                    break
    else:
        config["songs_dir"] = str(songs_dir)

    save_config()

    if not csgo_dir:
        app.after(200, request_csgo_dir)


# =========================
# STARTUP SEQUENCE
# Runs 50ms after the window appears. Starts the relay thread and loads songs
# in a background thread so the window is immediately visible and responsive.
# =========================


def on_app_ready():
    """Initialize GOAM after the main window is shown."""
    global selected_index

    # Start relay watcher immediately
    threading.Thread(target=relay_loop, daemon=True).start()

    # Heavy init (file scanning, cfg generation) runs in background to keep UI snappy
    def _init():
        clean_temp_files()
        rebuild_goam()
        idx = get_initial_track()
        app.after(0, lambda: _finish_init(idx))

    def _finish_init(idx: int):
        global selected_index
        selected_index = idx
        update_song_list()
        print("[GOAM] Ready")

    threading.Thread(target=_init, daemon=True).start()

    # First-run setup dialogs (shown before minimize logic)
    if is_first_run:
        request_songs_dir()
    elif not csgo_dir:
        request_csgo_dir()

    # Apply start-minimized behavior
    should_minimize = config.get("start_minimized") == "1" or (
        config.get("start_minimized_windows") == "1" and LAUNCHED_FROM_STARTUP
    )

    if should_minimize:
        if config.get("minimize_to_tray") == "1":
            create_tray()
            app.after(150, app.withdraw)
        else:
            app.after(150, app.iconify)
    # Do NOT create the tray icon here when not minimizing.
    # The tray is created on demand when the window is actually minimized.


app.after(50, on_app_ready)
app.mainloop()
