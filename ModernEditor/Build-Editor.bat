@echo off
rem ---------------------------------------------------------------------
rem  Builds ModernEditor.exe (traders, level limits, item stats, progression)
rem  into the "Editor" folder next to this script. The interface is built
rem  into the exe. Needs the .NET 10 SDK.
rem  Put the Editor folder anywhere EXCEPT inside BepInEx, e.g. C:\SPT\Modern Editor.
rem ---------------------------------------------------------------------
cd /d "%~dp0"
dotnet publish "ModernEditor.Editor\ModernEditor.Editor.csproj" -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:DebugType=none -o "%~dp0Editor"
if errorlevel 1 (
  echo.
  echo Build FAILED - see the messages above.
  pause
  exit /b 1
)
echo.
echo Done: %~dp0Editor\ModernEditor.exe
start "" "%~dp0Editor"
pause
