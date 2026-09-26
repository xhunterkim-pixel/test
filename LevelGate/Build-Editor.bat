@echo off
rem ---------------------------------------------------------------------
rem  Builds LevelGate.Editor.exe (the level limits editor) as ONE file into
rem  the "Editor" folder next to this script. Needs the .NET 10 SDK.
rem  Put the finished Editor folder anywhere EXCEPT inside BepInEx (BepInEx
rem  would try to load its dlls), e.g. C:\SPT\LevelGate Editor.
rem ---------------------------------------------------------------------
cd /d "%~dp0"
dotnet publish "LevelGate.Editor\LevelGate.Editor.csproj" -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:DebugType=none -o "%~dp0Editor"
if errorlevel 1 (
  echo.
  echo Build FAILED - see the messages above.
  pause
  exit /b 1
)
echo.
echo Done: %~dp0Editor\LevelGate.Editor.exe
start "" "%~dp0Editor"
pause
