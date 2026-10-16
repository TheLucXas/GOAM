# Shared helpers for the UI Automation smoke tests (Windows PowerShell 5.1 / PowerShell 7, interactive desktop).
# Every test runs GOAM against throw-away folders: GOAM_DATA_DIR / GOAM_TOOLS_DIR / GOAM_STEAM_DIR point
# into a scratch directory, so the real %APPDATA%, Steam folder, game and Run key are never touched.

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$script:AE = [Windows.Automation.AutomationElement]
Add-Type -Namespace '' -Name Native -MemberDefinition '[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool PostMessage(System.IntPtr hWnd, uint msg, System.IntPtr w, System.IntPtr l);'

$script:RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$script:DefaultExe = Join-Path $RepoRoot 'src\GOAM.App\bin\Release\net8.0-windows\GOAM.exe'
$script:ToneWav = Join-Path $RepoRoot 'tests\assets\tone-440hz-mono-22050.wav'
$script:Scratch = if ($env:GOAM_UI_TEST_DIR) { $env:GOAM_UI_TEST_DIR } else { Join-Path ([IO.Path]::GetTempPath()) 'goam-ui-tests' }
$script:Pass = 0
$script:Fail = 0

function Check([string]$Name, [bool]$Ok, [string]$Detail = '') {
    if ($Ok) { $script:Pass++; "PASS  $Name" } else { $script:Fail++; "FAIL  $Name  $Detail" }
}

function Finish-Tests {
    '----'
    "passed: $script:Pass  failed: $script:Fail"
    if ($script:Fail -gt 0) { exit 1 }
}

function New-Sandbox([string]$Name, [hashtable]$Config = @{}, [int]$Songs = 0, [switch]$NoConfig) {
    $run = Join-Path $script:Scratch $Name
    Remove-Item -Recurse -Force $run -ErrorAction SilentlyContinue
    $sb = [pscustomobject]@{
        Run = $run; Data = Join-Path $run 'data'; Songs = Join-Path $run 'songs'; Csgo = Join-Path $run 'csgo'
        Tools = Join-Path $run 'tools'; Steam = Join-Path $run 'steam'
    }
    New-Item -ItemType Directory -Force -Path $sb.Data, $sb.Songs, $sb.Csgo, $sb.Steam | Out-Null
    [IO.File]::WriteAllBytes((Join-Path $sb.Csgo 'csgo.exe'), [byte[]]@())
    for ($i = 1; $i -le $Songs; $i++) { Copy-Item $script:ToneWav (Join-Path $sb.Songs "Song $i.wav") }
    if (-not $NoConfig) {
        $cfg = @{ songsDir = $sb.Songs; csgoDir = $sb.Csgo; language = 'en'; volumePercent = 100; schemaVersion = 1 }
        foreach ($k in $Config.Keys) { $cfg[$k] = $Config[$k] }
        [IO.File]::WriteAllText((Join-Path $sb.Data 'config.json'), ($cfg | ConvertTo-Json), (New-Object Text.UTF8Encoding($false)))
    }
    $env:GOAM_DATA_DIR = $sb.Data
    $env:GOAM_TOOLS_DIR = $sb.Tools
    $env:GOAM_STEAM_DIR = $sb.Steam
    return $sb
}

function Wait-For([scriptblock]$Condition, [int]$Ms = 6000) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.ElapsedMilliseconds -lt $Ms) { if (& $Condition) { return $true }; Start-Sleep -Milliseconds 100 }
    return [bool](& $Condition)
}

function Get-MainWindow([int]$ProcessId, [int]$Ms = 15000) {
    $cond = New-Object Windows.Automation.PropertyCondition($script:AE::ProcessIdProperty, $ProcessId)
    $script:__win = $null
    Wait-For { $script:__win = $script:AE::RootElement.FindFirst([Windows.Automation.TreeScope]::Children, $cond); $null -ne $script:__win } $Ms | Out-Null
    return $script:__win
}

function Get-Buttons($Parent) {
    $cond = New-Object Windows.Automation.PropertyCondition($script:AE::ControlTypeProperty, [Windows.Automation.ControlType]::Button)
    return @($Parent.FindAll([Windows.Automation.TreeScope]::Descendants, $cond))
}

function Find-Button($Parent, [string]$Pattern) {
    return Get-Buttons $Parent | Where-Object { $_.Current.Name -match $Pattern } | Select-Object -First 1
}

# Song-list cells are buttons named "Fila N" / "Row N"; the text is in their Value pattern.
function Get-CellButtons($Parent, [string]$Text) {
    $found = @()
    foreach ($b in (Get-Buttons $Parent)) {
        $vp = $null
        if ($b.Current.Name.Trim() -match '^(Fila|Row) \d+' -and
            $b.TryGetCurrentPattern([Windows.Automation.ValuePattern]::Pattern, [ref]$vp) -and $vp.Current.Value -eq $Text) {
            $found += $b
        }
    }
    return $found
}

function Invoke-El($El) {
    $p = $null
    if ($El.TryGetCurrentPattern([Windows.Automation.InvokePattern]::Pattern, [ref]$p)) { $p.Invoke(); return $true }
    return $false
}

# Modal dialogs block UIA Invoke until closed; BM_CLICK is posted instead.
function Post-Click($El) {
    [Native]::PostMessage([IntPtr]$El.Current.NativeWindowHandle, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
}

function Find-ChildWindow($Parent, [string]$NamePattern) {
    $cond = New-Object Windows.Automation.PropertyCondition($script:AE::ControlTypeProperty, [Windows.Automation.ControlType]::Window)
    foreach ($w in $Parent.FindAll([Windows.Automation.TreeScope]::Children, $cond)) {
        if ($w.Current.Name -match $NamePattern) { return $w }
    }
    return $null
}

function Stop-App($Proc) {
    if ($Proc -and -not $Proc.HasExited) {
        $Proc.CloseMainWindow() | Out-Null
        if (-not $Proc.WaitForExit(6000)) { $Proc.Kill() }
    }
}
