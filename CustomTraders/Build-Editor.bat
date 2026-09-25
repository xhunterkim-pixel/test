@echo off
rem ---------------------------------------------------------------------
rem  Builds CustomTraders.Editor.exe as ONE file into the "Editor" folder
rem  next to this script. Double-click, then run Editor\CustomTraders.Editor.exe
rem  (make a desktop shortcut to it if you like). Needs the .NET 10 SDK.
rem ---------------------------------------------------------------------
cd /d "%~dp0"
dotnet publish "CustomTraders.Editor\CustomTraders.Editor.csproj" -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:DebugType=none -o "%~dp0Editor"
if errorlevel 1 (
  echo.
  echo Build FAILED - see the messages above.
  pause
  exit /b 1
)
echo.
echo Done: %~dp0Editor\CustomTraders.Editor.exe
start "" "%~dp0Editor"
pause
