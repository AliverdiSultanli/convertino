# Removes Convertino for the current user. Settings in %APPDATA%\Convertino are kept unless -Purge.
param([switch]$Purge)
Get-Process Convertino -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 400
Remove-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name Convertino -ErrorAction SilentlyContinue
Remove-Item (Join-Path ([Environment]::GetFolderPath('Programs')) 'Convertino.lnk') -ErrorAction SilentlyContinue
Remove-Item (Join-Path ([Environment]::GetFolderPath('SendTo')) 'Convertino.lnk') -ErrorAction SilentlyContinue
Remove-Item (Join-Path $env:LOCALAPPDATA 'Programs\Convertino') -Recurse -Force -ErrorAction SilentlyContinue
if ($Purge) { Remove-Item (Join-Path $env:APPDATA 'Convertino') -Recurse -Force -ErrorAction SilentlyContinue }
Write-Host 'Convertino removed.'
