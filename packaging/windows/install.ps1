# Convertino installer for Windows — per-user, no admin rights needed.
#   powershell -ExecutionPolicy Bypass -File install.ps1
# Run it from an extracted release (folder with app\Convertino.exe) or from a source checkout (builds first).
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$dest = Join-Path $env:LOCALAPPDATA 'Programs\Convertino'

if (Test-Path (Join-Path $here 'app\Convertino.exe')) {
    $src = Join-Path $here 'app'
} else {
    $proj = Join-Path $here '..\..\src\Convertino.Windows\Convertino.Windows.csproj'
    if (-not (Test-Path $proj)) { throw "Can't find app\Convertino.exe or the source tree next to this script." }
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'Building from source needs the .NET 10 SDK (https://dot.net).' }
    Write-Host 'Building Convertino (Release)...'
    $src = Join-Path $here '..\..\dist\win-x64'
    dotnet publish $proj -c Release -r win-x64 --self-contained true -o $src -v q -nologo
    if ($LASTEXITCODE -ne 0) { throw 'build failed' }
}

Get-Process Convertino -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 400
robocopy $src $dest /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { throw "copy failed ($LASTEXITCODE)" }

$lnk = Join-Path ([Environment]::GetFolderPath('Programs')) 'Convertino.lnk'
$sh = New-Object -ComObject WScript.Shell
$s = $sh.CreateShortcut($lnk)
$s.TargetPath = Join-Path $dest 'Convertino.exe'
$s.WorkingDirectory = $dest
$s.Description = 'Drag a file, tap Shift - convert and edit from a radial menu'
$s.Save()

# Explorer: right-click → Send to → Convertino opens the flower for the selected files
$sendTo = Join-Path ([Environment]::GetFolderPath('SendTo')) 'Convertino.lnk'
$st = $sh.CreateShortcut($sendTo)
$st.TargetPath = Join-Path $dest 'Convertino.exe'
$st.Arguments = 'menu'
$st.WorkingDirectory = $dest
$st.Description = 'Open the Convertino flower for these files'
$st.Save()

if (-not (Get-Command ffmpeg -ErrorAction SilentlyContinue)) {
    Write-Host 'Tip: video & audio petals need ffmpeg  ->  winget install Gyan.FFmpeg' -ForegroundColor Yellow
}
Start-Process (Join-Path $dest 'Convertino.exe')
Write-Host "Convertino installed to $dest and running (tray icon). Drag any file and tap Shift."
