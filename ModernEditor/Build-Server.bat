@echo off
rem ---------------------------------------------------------------------
rem  Builds the Modern Editor server mod and copies ModernEditor.dll into
rem  SPT_Runtime\user\mods\ModernEditor (the path in
rem  ModernEditor.Server\ModernEditor.Server.csproj, <SptServerDir>).
rem  Restart the SPT server afterwards. Needs the .NET 10 SDK.
rem ---------------------------------------------------------------------
cd /d "%~dp0"
dotnet build "ModernEditor.Server\ModernEditor.Server.csproj" -c Release
if errorlevel 1 (
  echo.
  echo Build FAILED - see the messages above.
  pause
  exit /b 1
)
echo.
echo Done: ModernEditor.dll was copied to your SPT user\mods\ModernEditor folder. Restart the SPT server.
pause
