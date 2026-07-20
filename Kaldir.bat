@echo off
chcp 65001 >nul
setlocal EnableExtensions

set "ROOT=%~dp0"
set "ROOT=%ROOT:~0,-1%"
set "UNINSTALLPS1=%ROOT%\scripts\uninstall-service.ps1"

net session >nul 2>&1
if errorlevel 1 (
    powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%ComSpec%' -ArgumentList '/c ""%~f0""' -Verb RunAs"
    exit /b
)

set "LOGDIR=%ProgramData%\NetworkHealthMonitor\logs"
if not exist "%LOGDIR%" mkdir "%LOGDIR%" >nul 2>&1
set "LOG=%LOGDIR%\kaldir-%DATE:~-4%%DATE:~3,2%%DATE:~0,2%-%TIME:~0,2%%TIME:~3,2%%TIME:~6,2%.log"
set "LOG=%LOG: =0%"

echo NetworkHealthMonitor kaldirma islemi basliyor.
echo Varsayilan olarak kullanici verileri korunur:
echo   %ProgramData%\NetworkHealthMonitor\data
echo   %ProgramData%\NetworkHealthMonitor\config
echo   %ProgramData%\NetworkHealthMonitor\backups
echo.
set "PURGE="
set /p "PURGE=Kullanici verilerini de kalici olarak silmek icin EVET yazin, korumak icin Enter'a basin: "

if not exist "%UNINSTALLPS1%" (
    echo uninstall-service.ps1 bulunamadi: %UNINSTALLPS1%
    pause
    exit /b 1
)

if /I "%PURGE%"=="EVET" (
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%UNINSTALLPS1%" -RemoveData >> "%LOG%" 2>&1
) else (
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%UNINSTALLPS1%" >> "%LOG%" 2>&1
)
if errorlevel 1 (
    echo Kaldirma tamamlanamadi. Log dosyasi: %LOG%
    pause
    exit /b 1
)

powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "$paths=@([IO.Path]::Combine([Environment]::GetFolderPath('Desktop'),'NetworkHealthMonitor.lnk'), (Join-Path $env:ProgramData 'Microsoft\Windows\Start Menu\Programs\NetworkHealthMonitor\NetworkHealthMonitor.lnk')); foreach($path in $paths){ Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue }" >> "%LOG%" 2>&1

echo.
echo NetworkHealthMonitor servisi kaldirildi.
if /I not "%PURGE%"=="EVET" echo Kullanici verileri korundu: %ProgramData%\NetworkHealthMonitor
echo Log dosyasi: %LOG%
pause
exit /b 0
