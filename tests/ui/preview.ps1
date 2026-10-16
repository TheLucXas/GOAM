# Preview toggle through the UI. Runs at volume 0, so it is silent.
param([string]$Exe)
. (Join-Path $PSScriptRoot 'Common.ps1')
if (-not $Exe) { $Exe = $script:DefaultExe }

$sb = New-Sandbox 'preview' @{ volumePercent = 0 }
# a 30 second WAV (22050 Hz mono PCM16), written without external tools
$data = New-Object byte[] (22050 * 2 * 30)
for ($i = 0; $i -lt $data.Length; $i += 2) { $data[$i] = 0; $data[$i + 1] = 0x10 }
$ms = New-Object IO.MemoryStream
$bw = New-Object IO.BinaryWriter $ms
$bw.Write([Text.Encoding]::ASCII.GetBytes('RIFF')); $bw.Write([int](36 + $data.Length)); $bw.Write([Text.Encoding]::ASCII.GetBytes('WAVEfmt '))
$bw.Write([int]16); $bw.Write([int16]1); $bw.Write([int16]1); $bw.Write([int]22050); $bw.Write([int]44100); $bw.Write([int16]2); $bw.Write([int16]16)
$bw.Write([Text.Encoding]::ASCII.GetBytes('data')); $bw.Write([int]$data.Length); $bw.Write($data); $bw.Flush()
[IO.File]::WriteAllBytes((Join-Path $sb.Songs 'Long Song.wav'), $ms.ToArray())

$GlyphPlay = [string][char]0x25B6
$GlyphStop = [string][char]0x23F9
$proc = Start-Process -FilePath $Exe -PassThru
try {
    $win = Get-MainWindow $proc.Id
    Start-Sleep -Seconds 2
    $playButton = Get-CellButtons $win $GlyphPlay | Select-Object -First 1
    Check 'preview (play) button present' ($null -ne $playButton)
    Invoke-El $playButton | Out-Null
    Check 'button turns into stop while previewing' (Wait-For { @(Get-CellButtons $win $GlyphStop).Count -eq 1 } 4000)
    Invoke-El (Get-CellButtons $win $GlyphStop | Select-Object -First 1) | Out-Null
    Check 'clicking again stops it' (Wait-For { @(Get-CellButtons $win $GlyphStop).Count -eq 0 } 4000)

    Check 'play button is back' (Wait-For { @(Get-CellButtons $win $GlyphPlay).Count -eq 1 } 4000)
    Invoke-El (Get-CellButtons $win $GlyphPlay | Select-Object -First 1) | Out-Null
    $sw = [Diagnostics.Stopwatch]::StartNew()
    Check 'second preview starts' (Wait-For { @(Get-CellButtons $win $GlyphStop).Count -eq 1 } 4000)
    Check 'preview ends by itself (song is 30 s long)' (Wait-For { @(Get-CellButtons $win $GlyphStop).Count -eq 0 } 9000)
    Check 'preview lasts about 5 seconds' (($sw.Elapsed.TotalSeconds -ge 4) -and ($sw.Elapsed.TotalSeconds -le 7.5)) "$($sw.Elapsed.TotalSeconds) s"
}
finally {
    Stop-App $proc
}
Finish-Tests
