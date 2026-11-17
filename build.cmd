@echo off
setlocal
cd /d "%~dp0"
rem Primero el ayudante de fondos animados (C++): la app lo copia junto a ella.
call src\DeskWallpaper.Animado\compilar.cmd || exit /b 1
dotnet build DeskWallpaper.slnx -c Debug -nologo -v q || exit /b 1
if /i "%1"=="test" (
  dotnet test tests\DeskWallpaper.Pruebas\DeskWallpaper.Pruebas.csproj -c Debug --no-build -nologo -v q || exit /b 1
)
exit /b 0
