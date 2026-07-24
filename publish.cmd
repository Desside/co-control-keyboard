@echo off
rem Builds a self-contained single-file CLI into .\dist\cocontrol.exe
dotnet publish src\CoControl.App\CoControl.App.csproj ^
  -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true ^
  -o dist
if %errorlevel% neq 0 exit /b %errorlevel%
if exist dist\cocontrol.exe del dist\cocontrol.exe
ren dist\CoControl.App.exe cocontrol.exe
echo.
echo Done: dist\cocontrol.exe
echo Try:  dist\cocontrol.exe rainbow
