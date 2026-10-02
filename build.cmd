@echo off
rem Rebuild Simple-AI-Tag-Tool (Debug).
rem
rem   build.cmd                 build only
rem   build.cmd run             build, then start the program
rem   build.cmd run "C:\data"   build, then start it on that dataset folder
rem
rem The ScreenListerNET.dll dependency comes from a sibling clone of
rem https://github.com/starik222/ScreenLister next to this repository; it is
rem cloned and built automatically the first time.
setlocal
cd /d "%~dp0"

tasklist /fi "imagename eq Simple-AI-Tag-Tool.exe" 2>nul | find /i "Simple-AI-Tag-Tool.exe" >nul
if not errorlevel 1 (
    echo Simple-AI-Tag-Tool is running. Close it first: Windows locks the exe while it runs.
    exit /b 1
)

if not exist "..\ScreenLister\ScreenList\bin\Release\net6.0-windows\ScreenListerNET.dll" (
    if not exist "..\ScreenLister" (
        echo Cloning ScreenLister next to this repository...
        git clone https://github.com/starik222/ScreenLister.git "..\ScreenLister" || exit /b 1
    )
    echo Building ScreenListerNET...
    dotnet build "..\ScreenLister\ScreenList\ScreenListerNET.csproj" -c Release -v quiet || exit /b 1
)

rem ErrorsOnly hides upstream's long-standing warnings (NU1701 for two .NET Framework UI packages, and
rem about 40 compiler warnings in upstream files). -tl:off is needed with it: the .NET terminal logger,
rem used when the console supports it, ignores -clp and prints every warning.
dotnet build "BooruDatasetTagManager\BooruDatasetTagManager.csproj" -c Debug -v quiet -nologo -tl:off -clp:ErrorsOnly || exit /b 1

set "EXE=%~dp0BooruDatasetTagManager\bin\Debug\net8.0-windows\Simple-AI-Tag-Tool.exe"
echo.
echo [OK] Built %EXE%

if /i not "%~1"=="run" exit /b 0
if "%~2"=="" (
    start "" "%EXE%"
) else (
    start "" "%EXE%" "%~2"
)
exit /b 0
