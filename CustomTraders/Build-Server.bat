@echo off
rem ---------------------------------------------------------------------
rem  Builds the CustomTraders server mod and copies CustomTraders.dll into
rem  SPT_Runtime\user\mods\CustomTraders (the path in
rem  CustomTraders.Server\CustomTraders.Server.csproj, <SptServerDir>).
rem  Restart the SPT server afterwards. Needs the .NET 10 SDK.
rem ---------------------------------------------------------------------
cd /d "%~dp0"
dotnet build "CustomTraders.Server\CustomTraders.Server.csproj" -c Release
if errorlevel 1 (
  echo.
  echo Build FAILED - see the messages above.
  pause
  exit /b 1
)
echo.
echo Done: the mod was copied to your SPT user\mods\CustomTraders folder. Restart the SPT server.
pause
