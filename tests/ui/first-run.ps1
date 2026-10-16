# First-run flow: no config -> "custom songs folder?" question (answer No) -> CS:GO folder dialog,
# pre-filled from a fake Steam library -> Confirm -> config.json and cfg files appear.
param([string]$Exe)
. (Join-Path $PSScriptRoot 'Common.ps1')
if (-not $Exe) { $Exe = $script:DefaultExe }

$sb = New-Sandbox 'first-run' -NoConfig
$game = Join-Path $sb.Steam 'steamapps\common\Counter-Strike Global Offensive'
New-Item -ItemType Directory -Force -Path $game | Out-Null
[IO.File]::WriteAllBytes((Join-Path $game 'csgo.exe'), [byte[]]@())
$vdf = '"libraryfolders"' + "`n{`n`t`"0`"`n`t{`n`t`t`"path`"`t`t`"" + ($sb.Steam -replace '\\', '\\') + "`"`n`t}`n}`n"
[IO.File]::WriteAllText((Join-Path $sb.Steam 'steamapps\libraryfolders.vdf'), $vdf, (New-Object Text.UTF8Encoding($false)))

$proc = Start-Process -FilePath $Exe -PassThru
try {
    $win = Get-MainWindow $proc.Id
    Check 'main window appears' ($null -ne $win)

    # MessageBox buttons have AutomationId 6 (Yes) / 7 (No) and no Invoke pattern
    $script:q = $null
    Wait-For { $script:q = Find-ChildWindow $win 'Initial setup|Configuraci'; $null -ne $script:q } | Out-Null
    Check 'first-run songs-folder question is shown' ($null -ne $script:q)
    if ($script:q) {
        $no = $script:q.FindFirst([Windows.Automation.TreeScope]::Descendants,
            (New-Object Windows.Automation.PropertyCondition($script:AE::AutomationIdProperty, '7')))
        Check 'answer "No" (default songs folder)' ($null -ne $no)
        if ($no) { Post-Click $no }
    }

    $script:d = $null
    Wait-For { $script:d = Find-ChildWindow $win 'CS:GO'; $null -ne $script:d } | Out-Null
    Check 'CS:GO folder dialog is shown' ($null -ne $script:d)
    if ($script:d) {
        $edit = $script:d.FindFirst([Windows.Automation.TreeScope]::Descendants,
            (New-Object Windows.Automation.PropertyCondition($script:AE::ControlTypeProperty, [Windows.Automation.ControlType]::Edit)))
        $value = $edit.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).Current.Value
        Check 'folder pre-filled from the Steam library' ($value -eq $game) "value='$value'"
        $ok = Find-Button $script:d '^(Confirm|Confirmar)$'
        Check 'Confirm button found' ($null -ne $ok)
        if ($ok) { Invoke-El $ok | Out-Null }
    }

    $cfgPath = Join-Path $sb.Data 'config.json'
    Check 'config.json written with both folders' (Wait-For {
            (Test-Path $cfgPath) -and ((Get-Content -Raw $cfgPath) -match [regex]::Escape(($game -replace '\\', '\\'))) -and ((Get-Content -Raw $cfgPath) -match '"songsDir"')
        })
    Check 'cfg files written into the game folder' (Wait-For { Test-Path (Join-Path $game 'csgo\cfg\goam.cfg') })
    Check 'default songs folder created' (Test-Path (Join-Path $sb.Data 'Songs'))
}
finally {
    Stop-App $proc
}
Finish-Tests
