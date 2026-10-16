# YouTube end-to-end through the real UI. The tools folder starts EMPTY, so the app has to download
# yt-dlp.exe itself and verify it against the release's SHA2-256SUMS. Needs internet access.
param(
    [string]$Exe,
    [string]$Url = 'https://www.youtube.com/watch?v=jNQXAC9IVRw'   # "Me at the zoo", 19 s; only used as a test input
)
. (Join-Path $PSScriptRoot 'Common.ps1')
if (-not $Exe) { $Exe = $script:DefaultExe }

$sb = New-Sandbox 'youtube'
# ffmpeg must not be needed
$env:PATH = ($env:PATH -split ';' | Where-Object { $_ -notmatch 'ffmpeg' }) -join ';'

$proc = Start-Process -FilePath $Exe -PassThru
try {
    $win = Get-MainWindow $proc.Id
    Start-Sleep -Seconds 1
    $edit = $win.FindFirst([Windows.Automation.TreeScope]::Descendants,
        (New-Object Windows.Automation.PropertyCondition($script:AE::ControlTypeProperty, [Windows.Automation.ControlType]::Edit)))
    $edit.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue($Url)

    $dl = Find-Button $win 'Download'
    Check 'download button found' ($null -ne $dl)
    $sw = [Diagnostics.Stopwatch]::StartNew()
    Invoke-El $dl | Out-Null

    $script:errorText = $null
    Wait-For {
        if (@(Get-ChildItem $sb.Songs -Filter *.wav -ErrorAction SilentlyContinue).Count -ge 1) { return $true }
        $dlg = Find-ChildWindow $win '.'
        if ($dlg) {
            $script:errorText = ($dlg.FindAll([Windows.Automation.TreeScope]::Descendants, [Windows.Automation.Condition]::TrueCondition) |
                Where-Object { $_.Current.Name } | ForEach-Object { $_.Current.Name }) -join ' | '
            return $true
        }
        return $false
    } 180000 | Out-Null
    if ($script:errorText) { "      dialog shown: $script:errorText" }
    $wav = Get-ChildItem $sb.Songs -Filter *.wav -ErrorAction SilentlyContinue | Select-Object -First 1
    Check 'a converted .wav appeared in the songs folder' ($null -ne $wav)
    "      elapsed: $([math]::Round($sw.Elapsed.TotalSeconds, 1)) s"

    $exePath = Join-Path $sb.Tools 'yt-dlp.exe'
    Check 'yt-dlp.exe was installed by the app' (Test-Path $exePath)
    if (Test-Path $exePath) {
        $actual = (Get-FileHash $exePath -Algorithm SHA256).Hash.ToLower()
        $recorded = (Get-Content -Raw (Join-Path $sb.Tools 'yt-dlp.exe.sha256')).Trim()
        $resp = Invoke-WebRequest -UseBasicParsing 'https://github.com/yt-dlp/yt-dlp/releases/latest/download/SHA2-256SUMS'
        $sums = if ($resp.Content -is [byte[]]) { [Text.Encoding]::ASCII.GetString($resp.Content) } else { [string]$resp.Content }
        $official = ($sums -split "`n" | Where-Object { $_ -match '\syt-dlp\.exe\s*$' } | ForEach-Object { ($_ -split '\s+')[0] } | Select-Object -First 1).ToLower()
        Check 'installed exe hash == official SHA2-256SUMS' ($actual -eq $official) "$actual vs $official"
        Check 'hash recorded by the app matches' ($recorded -eq $actual)
    }

    if ($wav) {
        $b = [IO.File]::ReadAllBytes($wav.FullName)
        $ch = [BitConverter]::ToInt16($b, 22); $rate = [BitConverter]::ToInt32($b, 24); $bits = [BitConverter]::ToInt16($b, 34)
        $secs = ($b.Length - 44) / ($rate * $ch * $bits / 8)
        Check 'output is 22050 Hz / mono / 16-bit' (($ch -eq 1) -and ($rate -eq 22050) -and ($bits -eq 16)) "$rate Hz $ch ch $bits bit"
        Check 'duration is about 19 s' (($secs -gt 17) -and ($secs -lt 21)) "$secs s"
        Check 'file name is sanitised' ($wav.BaseName -match '^[\p{L}\p{N}_]+$') $wav.BaseName
    }
    Check 'voice_input.wav written to the (fake) game folder' (Wait-For { Test-Path (Join-Path $sb.Csgo 'voice_input.wav') } 10000)
    Check 'private downloads folder is empty' (-not (Test-Path (Join-Path $sb.Data 'downloads')) -or @(Get-ChildItem (Join-Path $sb.Data 'downloads') -ErrorAction SilentlyContinue).Count -eq 0)
}
finally {
    Stop-App $proc
}
Finish-Tests
