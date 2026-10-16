# Single instance + tray: start hidden, a second launch brings the first window forward and exits,
# minimizing hides the window again.
param([string]$Exe)
. (Join-Path $PSScriptRoot 'Common.ps1')
if (-not $Exe) { $Exe = $script:DefaultExe }

Add-Type -Namespace Win -Name Windows -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, System.IntPtr l);
public delegate bool EnumProc(System.IntPtr h, System.IntPtr l);
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(System.IntPtr h, out uint pid);
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool IsWindowVisible(System.IntPtr h);
[System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] public static extern int GetWindowText(System.IntPtr h, System.Text.StringBuilder s, int n);
'@

function Visible-GoamWindows([int]$ProcessId) {
    $found = New-Object System.Collections.ArrayList
    $cb = [Win.Windows+EnumProc] {
        param($h, $l)
        [uint32]$p = 0
        [void][Win.Windows]::GetWindowThreadProcessId($h, [ref]$p)
        if ($p -eq $ProcessId -and [Win.Windows]::IsWindowVisible($h)) {
            $sb2 = New-Object System.Text.StringBuilder 256
            [void][Win.Windows]::GetWindowText($h, $sb2, 256)
            if ($sb2.ToString() -like 'GOAM*') { [void]$found.Add($h) }
        }
        return $true
    }
    [void][Win.Windows]::EnumWindows($cb, [IntPtr]::Zero)
    return @($found)
}

$sb = New-Sandbox 'tray' @{ startMinimized = $true; minimizeToTray = $true }
$first = Start-Process -FilePath $Exe -PassThru
try {
    Start-Sleep -Seconds 3
    Check 'first instance is running' (-not $first.HasExited)
    Check 'started hidden (tray): no visible window' ((Visible-GoamWindows $first.Id).Count -eq 0)

    $second = Start-Process -FilePath $Exe -PassThru
    Check 'second instance exits by itself' ($second.WaitForExit(8000))
    Check 'first window appears after the second launch' (Wait-For { (Visible-GoamWindows $first.Id).Count -ge 1 })

    $win = Get-MainWindow $first.Id
    $win.GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern).SetWindowVisualState([Windows.Automation.WindowVisualState]::Minimized)
    Check 'minimizing hides the window (tray)' (Wait-For { (Visible-GoamWindows $first.Id).Count -eq 0 })

    $third = Start-Process -FilePath $Exe -PassThru
    Check 'third launch exits and restores the window' ($third.WaitForExit(8000) -and (Wait-For { (Visible-GoamWindows $first.Id).Count -ge 1 }))
}
finally {
    Stop-App $first
}
Finish-Tests
