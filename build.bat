@echo off
setlocal
pushd "%~dp0"

rem --- config ---
set "PROJECT=RakutenDrive\RakutenDrive.csproj"
set "OUT=%~dp0artifacts\publish\win-x64"
rem Remove the fixed ZIP declaration – we'll build it dynamically
rem set "ZIP=%~dp0artifacts\publish\RakutenDrive-win-x64.zip"

rem --- clear output folder ---
echo Cleaning "%OUT%"...
if exist "%OUT%" rmdir /S /Q "%OUT%"
mkdir "%OUT%"

rem --- build/publish ---
msbuild "%PROJECT%" /t:Restore;Build;Publish ^
  /p:Configuration=Release ^
  /p:Platform=x64 ^
  /p:RuntimeIdentifier=win-x64 ^
  /p:SelfContained=true ^
  /p:PublishSingleFile=false ^
  /p:PublishReadyToRun=false ^
  /p:PublishDir="%OUT%"

set ERR=%ERRORLEVEL%
if not "%ERR%"=="0" (
  echo.
  echo Publish FAILED with exit code %ERR%.
  popd
  exit /b %ERR%
)

rem --- compute the Build and Revision numbers from the published assembly ---
for /F "tokens=1-4 delims=." %%A in ('
  powershell -NoLogo -NoProfile -Command ^
    "[System.Reflection.AssemblyName]::GetAssemblyName((Join-Path '%OUT%' 'RakutenDrive.dll')).Version.ToString()"
') do (
  set MAJOR=%%A
  set MINOR=%%B
  set BUILD=%%C
  set REVISION=%%D
)

rem Construct the zip path including the build/revision numbers
set "ZIP=%~dp0artifacts\publish\RakutenDrive-win-x64-%BUILD%_%REVISION%.zip"

rem --- zip contents of OUT (without the OUT folder itself) ---
echo Creating zip "%ZIP%"...
if exist "%ZIP%" del /F /Q "%ZIP%"
powershell -NoLogo -NoProfile -Command "Compress-Archive -Path '%OUT%\*' -DestinationPath '%ZIP%'"

echo.
echo Publish succeeded. Output:
echo   "%OUT%"
echo Zip created:
echo   "%ZIP%"

popd
exit /b 0
