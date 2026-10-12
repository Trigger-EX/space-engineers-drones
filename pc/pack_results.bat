@echo off
REM Zips everything under results\ into SE_results.zip (send me that file).
powershell -NoProfile -ExecutionPolicy Bypass -Command "Set-Location '%~dp0'; if (Test-Path SE_results.zip) { Remove-Item SE_results.zip }; Compress-Archive -Path results -DestinationPath SE_results.zip -CompressionLevel Optimal; Get-Item SE_results.zip | Select-Object Name,Length"
pause
