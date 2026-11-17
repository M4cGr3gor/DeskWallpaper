@echo off
rem Genera la version para usar en Distribucion\DeskWallpaper (ver Distribucion\LEEME.txt).
setlocal
cd /d "%~dp0"
rem Si algo de esa carpeta esta en marcha (la app o el ayudante de fondos animados), sus archivos estan bloqueados:
rem borrarla dejaria la carpeta a medias y sin DeskWallpaper.exe. Se para antes de tocar nada.
powershell -NoProfile -Command "$d = [IO.Path]::GetFullPath('Distribucion\DeskWallpaper') + '\'; $p = @(Get-CimInstance Win32_Process | Where-Object { $_.ExecutablePath -and $_.ExecutablePath.StartsWith($d, [StringComparison]::OrdinalIgnoreCase) }); foreach ($x in $p) { '  ' + $x.Name + ' (proceso ' + $x.ProcessId + ')' }; exit $p.Count"
if errorlevel 1 (
  echo No se puede publicar: hay programas en marcha desde Distribucion\DeskWallpaper.
  echo Cierra DeskWallpaper y, si hay un fondo animado, usa "Cerrar (vuelve al reiniciar)" en su icono junto al reloj.
  exit /b 1
)
if exist Distribucion\DeskWallpaper rmdir /s /q Distribucion\DeskWallpaper
call src\DeskWallpaper.Animado\compilar.cmd || exit /b 1
dotnet publish src\DeskWallpaper.App\DeskWallpaper.App.csproj -c Release -p:Platform=x64 -o Distribucion\DeskWallpaper -nologo -v q || exit /b 1
copy /y src\DeskWallpaper.App\LEEME.txt Distribucion\LEEME.txt >nul
echo Listo: Distribucion\DeskWallpaper