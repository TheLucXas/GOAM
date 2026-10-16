# Phase 0 baseline for the Python version. Run from an env.ps1-initialised shell.
param(
    # Path to goam.py from the v1.x-python-final tag, e.g.:  git show v1.x-python-final:goam.py > goam.py
    [Parameter(Mandatory)][string]$GoamPy,
    [int]$Runs = 5
)
$ErrorActionPreference = 'Continue'
$root   = $env:GOAM_PORT_ROOT
$bench  = Join-Path $root 'bench'
$res    = Join-Path $bench 'results'
$py     = Join-Path $root 'venv\Scripts\python.exe'
$pyw    = Join-Path $root 'venv\Scripts\pythonw.exe'
$tools  = Join-Path $root 'tools\bench'
$mp3    = Join-Path $bench 'assets\five_min.mp3'
New-Item -ItemType Directory -Force -Path $res | Out-Null

function New-PyAppData([int]$n) {
    $ad = Join-Path $bench "appdata_py_$n"
    $dir = Join-Path $ad 'GOAM'
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    $csgo  = 'csgo_dir="' + (Join-Path $bench 'fake-csgo') + '"'
    $songs = 'songs_dir="' + (Join-Path $bench "songs$n") + '"'
    $lines = @($csgo, $songs, 'language="en"')
    Set-Content -LiteralPath (Join-Path $dir 'config') -Value ($lines -join "`n") -Encoding ASCII -NoNewline
    return $ad
}

foreach ($n in 20, 100, 300) {
    $env:APPDATA = New-PyAppData $n
    "=== list bench N=$n ==="
    $out = & $py $tools\bench_list_py.py $GoamPy (Join-Path $bench "songs$n") $mp3 2>&1 | ForEach-Object { "$_" }
    $line = $out | Where-Object { $_ -like 'BENCH_RESULT *' } | Select-Object -Last 1
    if ($line) { ($line -replace '^BENCH_RESULT ', '') | Set-Content -LiteralPath (Join-Path $res "py_list_$n.json") -Encoding UTF8; $line }
    else { $out | Select-Object -Last 15 }
}

foreach ($n in 20, 300) {
    $env:APPDATA = New-PyAppData $n
    "=== startup bench (script, pythonw) N=$n ==="
    & $py $tools\win_visible.py --runs $Runs --label "py-script-$n" --title-contains 'GOAM' --settle 5 -- $pyw $GoamPy 2>&1 |
        ForEach-Object { "$_" } | Out-File -LiteralPath (Join-Path $res "py_startup_script_$n.txt") -Encoding UTF8
    Get-Content (Join-Path $res "py_startup_script_$n.txt") | Select-Object -Last 14
}
