# Measures the C# build variants: size, time to visible window, idle memory, list refresh, MP3 conversion.
# Run from an env.ps1-initialised shell. Output goes to bench\results\cs_*.
param(
    [string[]]$Variants = @('fdd', 'sc', 'sc-r2r', 'sc-r2r-comp'),
    [int]$Runs = 5,
    [switch]$SkipPublish
)
$ErrorActionPreference = 'Continue'
$root = $env:GOAM_PORT_ROOT
$repo = Join-Path $root 'repo'
$bench = Join-Path $root 'bench'
$res = Join-Path $bench 'results'
$py = Join-Path $root 'venv\Scripts\python.exe'
$tools = Join-Path $root 'tools\bench'
$mp3 = Join-Path $bench 'assets\five_min.mp3'
New-Item -ItemType Directory -Force -Path $res | Out-Null

$props = @{
    'fdd'         = @('--self-contained', 'false', '-p:PublishSingleFile=true')
    'sc'          = @('--self-contained', 'true', '-p:PublishSingleFile=true')
    'sc-r2r'      = @('--self-contained', 'true', '-p:PublishSingleFile=true', '-p:PublishReadyToRun=true')
    'sc-r2r-comp' = @('--self-contained', 'true', '-p:PublishSingleFile=true', '-p:PublishReadyToRun=true', '-p:EnableCompressionInSingleFile=true')
}

function New-DataDir([string]$variant, [int]$n) {
    $d = Join-Path $bench "appdata_cs_${variant}_$n"
    Remove-Item -Recurse -Force $d -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force -Path $d | Out-Null
    $cfg = @{ songsDir = (Join-Path $bench "songs$n"); csgoDir = (Join-Path $bench 'fake-csgo'); language = 'en'; volumePercent = 70; schemaVersion = 1 } | ConvertTo-Json
    [IO.File]::WriteAllText((Join-Path $d 'config.json'), $cfg, (New-Object Text.UTF8Encoding($false)))
    return $d
}

foreach ($v in $Variants) {
    $outDir = Join-Path $root "out\publish\$v"
    if (-not $SkipPublish) {
        Remove-Item -Recurse -Force $outDir -ErrorAction SilentlyContinue
        "=== publish $v ==="
        $sw = [Diagnostics.Stopwatch]::StartNew()
        & dotnet publish (Join-Path $repo 'src\GOAM.App\GOAM.App.csproj') -c Release -r win-x64 @($props[$v]) -o $outDir --nologo -v q 2>&1 |
            ForEach-Object { "$_" } | Select-Object -Last 3
        "publish seconds: $([math]::Round($sw.Elapsed.TotalSeconds))"
    }
    $exe = Join-Path $outDir 'GOAM.exe'
    if (-not (Test-Path $exe)) { "missing $exe"; continue }
    $size = (Get-Item $exe).Length
    $extra = (Get-ChildItem $outDir -File | Where-Object { $_.Name -ne 'GOAM.exe' -and $_.Extension -ne '.pdb' } | Measure-Object Length -Sum).Sum
    "size: $([math]::Round($size/1MB,2)) MB (+ other files: $([math]::Round(($extra+0)/1MB,2)) MB)"

    # list + conversion benchmark, in-process
    foreach ($n in 20, 100, 300) {
        $env:GOAM_DATA_DIR = New-DataDir $v $n
        $env:GOAM_TOOLS_DIR = Join-Path $bench 'tools-none'
        $json = Join-Path $res "cs_${v}_list_$n.json"
        Remove-Item $json -ErrorAction SilentlyContinue
        Start-Process -FilePath $exe -ArgumentList '--benchmark', $json, '--bench-mp3', $mp3 -Wait
        if (Test-Path $json) { "list N=$n : " + ((Get-Content -Raw $json) -replace '\s+', ' ') } else { "list N=$n : NO RESULT" }
    }

    # startup + idle memory, external observer (same tool as for Python)
    foreach ($n in 20, 300) {
        $env:GOAM_DATA_DIR = New-DataDir $v $n
        $txt = Join-Path $res "cs_${v}_startup_$n.txt"
        & $py (Join-Path $tools 'win_visible.py') --runs $Runs --label "cs-$v-$n" --title-contains 'GOAM' --settle 5 -- $exe 2>&1 |
            ForEach-Object { "$_" } | Out-File -LiteralPath $txt -Encoding UTF8
        "startup N=$n : " + ((Select-String -Path $txt -Pattern '"visible_ms_median"|"working_set_mb_median"|"private_mb_median"|"visible_ms_min"|"visible_ms_max"' | ForEach-Object { $_.Line.Trim() }) -join ' ')
    }
}
