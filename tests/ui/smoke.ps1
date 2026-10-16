# Main-window smoke test: select a song, stop/start GOAM, language toggle, options dialog, cleanup on close.
param([string]$Exe)
. (Join-Path $PSScriptRoot 'Common.ps1')
if (-not $Exe) { $Exe = $script:DefaultExe }

$sb = New-Sandbox 'smoke' -Songs 5
$cfgDir = Join-Path $sb.Csgo 'csgo\cfg'
$proc = Start-Process -FilePath $Exe -PassThru
try {
    $win = Get-MainWindow $proc.Id
    Check 'main window appears' ($null -ne $win)
    if (-not $win) { throw 'no window' }
    Check 'window title' ($win.Current.Name -like 'GOAM*') $win.Current.Name

    Check 'cfg files written at start' (Wait-For { (Test-Path (Join-Path $cfgDir 'goam.cfg')) -and (Test-Path (Join-Path $cfgDir 'goam_tracks.cfg')) })
    Check 'no voice_input.wav before selecting' (-not (Test-Path (Join-Path $sb.Csgo 'voice_input.wav')))

    Start-Sleep -Milliseconds 500
    $sel = @(Get-CellButtons $win 'Select')
    Check 'song rows present (4 Select + 1 Active)' (($sel.Count -eq 4) -and ((Get-CellButtons $win 'Active').Count -eq 1)) "Select buttons: $($sel.Count)"

    # rows are Song 1 (active), then 2..5, so $sel[2] is "Song 4"
    Check 'Select button is invokable' (Invoke-El $sel[2])
    Check 'voice_input.wav written after selecting' (Wait-For { Test-Path (Join-Path $sb.Csgo 'voice_input.wav') })
    Check 'current_track.cfg names the song' (Wait-For { (Test-Path (Join-Path $cfgDir 'current_track.cfg')) -and ((Get-Content -Raw (Join-Path $cfgDir 'current_track.cfg')) -match 'Song 4') })
    Check 'active marker moved to the new row' (Wait-For { @(Get-CellButtons $win 'Active').Count -eq 1 -and @(Get-CellButtons $win 'Select').Count -eq 4 })

    $stopBtn = Find-Button $win 'Stop GOAM'
    Check 'Stop GOAM button found' ($null -ne $stopBtn)
    if ($stopBtn) {
        Invoke-El $stopBtn | Out-Null
        Check 'stopping removes generated files' (Wait-For { -not (Test-Path (Join-Path $sb.Csgo 'voice_input.wav')) -and -not (Test-Path (Join-Path $cfgDir 'goam.cfg')) })
        $script:startBtn = $null
        Wait-For { $script:startBtn = Find-Button $win 'Start GOAM'; $null -ne $script:startBtn } | Out-Null
        Check 'button turns into Start GOAM' ($null -ne $script:startBtn)
        if ($script:startBtn) {
            Invoke-El $script:startBtn | Out-Null
            Check 'starting writes the cfg files again' (Wait-For { Test-Path (Join-Path $cfgDir 'goam.cfg') })
        }
    }

    $lang = Find-Button $win '^EN$'
    Check 'language button found' ($null -ne $lang)
    if ($lang) {
        Invoke-El $lang | Out-Null
        Check 'switching language to Spanish' (Wait-For { $null -ne (Find-Button $win '^Opciones$') })
        Check 'language persisted in config.json' (Wait-For { (Get-Content -Raw (Join-Path $sb.Data 'config.json')) -match '"language":\s*"es"' })
        Check 'cfg messages follow the language' (Wait-For { (Get-Content -Raw (Join-Path $cfgDir 'goam_tracks.cfg')) -match 'Lista de canciones GOAM' })
    }

    $opt = Find-Button $win '^Opciones$'
    Check 'options button found' ($null -ne $opt)
    if ($opt) {
        Post-Click $opt
        $dlg = $null
        Wait-For { $script:__d = Find-ChildWindow $win '^Opciones$'; $null -ne $script:__d } 6000 | Out-Null
        $dlg = $script:__d
        Check 'options dialog opens' ($null -ne $dlg)
        if ($dlg) {
            $close = Find-Button $dlg '^Cerrar$'
            Check 'options dialog has a Close button' ($null -ne $close)
            if ($close) {
                Post-Click $close
                Check 'options dialog closes' (Wait-For { $null -eq (Find-ChildWindow $win '^Opciones$') } 5000)
            }
        }
    }
}
finally {
    Stop-App $proc
}
Check 'closing removes voice/cfg files' (-not (Test-Path (Join-Path $sb.Csgo 'voice_input.wav')) -and -not (Test-Path (Join-Path $cfgDir 'goam.cfg')))
Finish-Tests
