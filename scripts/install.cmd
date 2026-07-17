@echo off
setlocal

fltmc.exe >nul 2>&1
if not "%errorlevel%"=="0" (
    powershell.exe -NoProfile -NonInteractive -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)

cd /d "%~dp0"
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "%~dp0install-service.ps1"
set "pulse_exit_code=%errorlevel%"

if not "%pulse_exit_code%"=="0" (
    echo.
    echo A instalacao falhou. Revise a mensagem acima antes de fechar esta janela.
    pause
)

exit /b %pulse_exit_code%
