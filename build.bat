@echo off
chcp 65001 >nul
setlocal enabledelayedexpansion
title EventTracker - Build

echo.
echo  +==========================================+
echo  ^|       EventTracker - Build Script        ^|
echo  +==========================================+
echo.

:: Check .NET SDK
dotnet --version >nul 2>&1
if errorlevel 1 (
    echo  [ERROR] .NET SDK not found.
    echo.
    echo  Please install .NET 8 SDK from:
    echo  https://dotnet.microsoft.com/download/dotnet/8.0
    echo.
    pause
    exit /b 1
)

for /f "tokens=*" %%v in ('dotnet --version') do set SDK_VER=%%v
echo  [OK] .NET SDK found: %SDK_VER%
echo.

:: Restore NuGet packages
echo  [1/3] Restoring packages...
dotnet restore EventTracker.csproj --nologo -v quiet
if errorlevel 1 (
    echo  [ERROR] Restore failed.
    pause
    exit /b 1
)
echo  [OK] Packages restored.
echo.

:: Publish self-contained single-file exe
echo  [2/3] Building self-contained EXE...
echo        (this may take 1-2 minutes on first run)
echo.

dotnet publish EventTracker.csproj ^
    --configuration Release ^
    --runtime win-x64 ^
    --self-contained true ^
    -p:PublishSingleFile=true ^
    -p:IncludeNativeLibrariesForSelfExtract=true ^
    -p:EnableCompressionInSingleFile=true ^
    -p:DebugType=None ^
    -p:DebugSymbols=false ^
    --output .\publish ^
    --nologo ^
    -v quiet

if errorlevel 1 (
    echo.
    echo  [ERROR] Build failed. Check errors above.
    pause
    exit /b 1
)

echo.
echo  [OK] Build complete!
echo.

:: Show result
echo  [3/3] Output:
echo.
if exist ".\publish\EventTracker.exe" (
    for %%F in (".\publish\EventTracker.exe") do (
        set SIZE=%%~zF
        set /a SIZE_MB=!SIZE! / 1048576
        echo         File : .\publish\EventTracker.exe
        echo         Size : !SIZE_MB! MB
    )
) else (
    echo  [WARN] EXE not found at expected path. Check .\publish\ folder.
)

echo.
echo  +==========================================+
echo  ^|   Done! Run: publish\EventTracker.exe   ^|
echo  +==========================================+
echo.

:: Open output folder?
set /p OPEN="  Open output folder? (y/n): "
if /i "!OPEN!"=="y" explorer.exe .\publish

endlocal
