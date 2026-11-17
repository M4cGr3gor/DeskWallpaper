@echo off
rem Compila el ayudante de fondos animados (C++) y su generador de video de prueba con las Build Tools de Visual Studio.
setlocal
set "VS="
cd /d "%~dp0"
set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
if not exist "%VSWHERE%" set "VSWHERE=C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe"
"%VSWHERE%" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath > "%TEMP%\dw-vs.txt"
set /p VS=<"%TEMP%\dw-vs.txt"
del "%TEMP%\dw-vs.txt" 2>nul
if not defined VS (echo No se encuentran las herramientas de C++ de Visual Studio & exit /b 1)
rem vcvars64 avisa si no encuentra vswhere en el PATH aunque funcione: el aviso se descarta.
call "%VS%\VC\Auxiliary\Build\vcvars64.bat" >nul 2>nul || exit /b 1
if not exist bin mkdir bin
if not exist obj mkdir obj
set OPC=/nologo /std:c++20 /O2 /EHsc /W4 /WX /permissive- /utf-8 /DUNICODE /D_UNICODE /Fo:obj\
cl %OPC% /Fe:bin\DeskWallpaper.Animado.exe Animado.cpp Bandeja.cpp Config.cpp Escritorio.cpp Info.cpp Pausa.cpp Reducir.cpp Registro.cpp Reproductor.cpp /link /SUBSYSTEM:WINDOWS d3d11.lib dxgi.lib dwmapi.lib mfplat.lib mfreadwrite.lib mfuuid.lib windowscodecs.lib windowsapp.lib ole32.lib oleaut32.lib user32.lib shell32.lib advapi32.lib wtsapi32.lib || exit /b 1
cl %OPC% /Fe:bin\GenerarVideoPrueba.exe prueba\GenerarVideo.cpp /link /SUBSYSTEM:CONSOLE mfplat.lib mfreadwrite.lib mfuuid.lib d2d1.lib dwrite.lib windowscodecs.lib ole32.lib || exit /b 1
echo Listo: bin\DeskWallpaper.Animado.exe
