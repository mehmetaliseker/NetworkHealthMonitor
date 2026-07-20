@echo off
chcp 65001 >nul
setlocal EnableExtensions

set "ROOT=%~dp0"
set "ROOT=%ROOT:~0,-1%"
set "UIEXE=%ROOT%\ui\NetworkHealthMonitor.exe"
set "WORKEREXE=%ROOT%\worker\NetworkHealthMonitor.Worker.exe"
set "INSTALLPS1=%ROOT%\scripts\install-service.ps1"
set "UIUSER=%USERDOMAIN%\%USERNAME%"
if /I "%~1"=="__elevated" (
    if not "%~2"=="" set "UIUSER=%~2"
)

net session >nul 2>&1
if errorlevel 1 (
    powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%ComSpec%' -ArgumentList '/c ""%~f0"" __elevated ""%UIUSER%""' -Verb RunAs"
    exit /b
)

set "LOGDIR=%ProgramData%\NetworkHealthMonitor\logs"
if not exist "%LOGDIR%" mkdir "%LOGDIR%" >nul 2>&1
set "LOG=%LOGDIR%\kurulum-%DATE:~-4%%DATE:~3,2%%DATE:~0,2%-%TIME:~0,2%%TIME:~3,2%%TIME:~6,2%.log"
set "LOG=%LOG: =0%"

call :log "NetworkHealthMonitor kurulumu basladi."
call :log "Paket klasoru: %ROOT%"
call :log "UI kullanicisi: %UIUSER%"

if not exist "%UIEXE%" (
    call :fail "UI dosyasi bulunamadi: %UIEXE%"
    exit /b 1
)
if not exist "%WORKEREXE%" (
    call :fail "Worker dosyasi bulunamadi: %WORKEREXE%"
    exit /b 1
)
if not exist "%INSTALLPS1%" (
    call :fail "install-service.ps1 bulunamadi: %INSTALLPS1%"
    exit /b 1
)

call :log "Dosya engelleri kaldiriliyor."
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "Get-ChildItem -LiteralPath '%ROOT%' -Recurse -File | Unblock-File -ErrorAction SilentlyContinue" >> "%LOG%" 2>&1
if errorlevel 1 (
    call :fail "Dosya engelleri kaldirilamadi."
    exit /b 1
)

call :log "Worker servisi kuruluyor veya guncelleniyor."
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%INSTALLPS1%" -BinaryPath "%WORKEREXE%" -UiUser "%UIUSER%" >> "%LOG%" 2>&1
if errorlevel 1 (
    call :fail "Worker servisi kurulumu basarisiz."
    exit /b 1
)

call :log "Kisayollar olusturuluyor."
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "$ui='%UIEXE%'; $shell=New-Object -ComObject WScript.Shell; $desktop=[Environment]::GetFolderPath('Desktop'); $start=Join-Path $env:ProgramData 'Microsoft\Windows\Start Menu\Programs\NetworkHealthMonitor'; New-Item -ItemType Directory -Force -Path $start | Out-Null; foreach($path in @(Join-Path $desktop 'NetworkHealthMonitor.lnk', Join-Path $start 'NetworkHealthMonitor.lnk')) { $s=$shell.CreateShortcut($path); $s.TargetPath=$ui; $s.WorkingDirectory=Split-Path -Parent $ui; $s.Description='NetworkHealthMonitor'; $s.Save() }" >> "%LOG%" 2>&1
if errorlevel 1 (
    call :fail "Kisayollar olusturulamadi."
    exit /b 1
)

call :log "Worker durumu dogrulaniyor."
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%ROOT%\scripts\service-health-check.ps1" -WorkerPath "%WORKEREXE%" >> "%LOG%" 2>&1
if errorlevel 1 (
    call :fail "Worker saglik kontrolu basarisiz."
    exit /b 1
)

call :log "UI aciliyor."
start "" explorer.exe "%UIEXE%"

echo.
echo NetworkHealthMonitor kurulumu tamamlandı. Worker arka planda çalışıyor ve Windows açıldığında otomatik başlayacak.
echo Log dosyasi: %LOG%
pause
exit /b 0

:log
echo [%DATE% %TIME%] %~1
>> "%LOG%" echo [%DATE% %TIME%] %~1
exit /b 0

:fail
call :log "HATA: %~1"
echo.
echo Kurulum tamamlanamadi: %~1
echo Log dosyasi: %LOG%
pause
exit /b 1
