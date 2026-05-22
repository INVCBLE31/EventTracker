@echo off
echo ============================================
echo  EventTracker - Build Script
echo ============================================
echo.

where dotnet >nul 2>nul
if %ERRORLEVEL% NEQ 0 (
    echo [ERROR] .NET SDK not found!
    echo.
    echo Please install .NET 8 SDK from:
    echo https://dotnet.microsoft.com/download/dotnet/8.0
    echo.
    pause
    exit /b 1
)

echo [1/3] Restoring NuGet packages...
dotnet restore EventTracker.csproj -r win-x64
if %ERRORLEVEL% NEQ 0 ( echo Restore failed. & pause & exit /b 1 )

echo.
echo [2/3] Building...
dotnet build EventTracker.csproj -c Release --no-restore
if %ERRORLEVEL% NEQ 0 ( echo Build failed. & pause & exit /b 1 )

echo.
echo [3/3] Publishing self-contained executable...
dotnet publish EventTracker.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true --no-restore
if %ERRORLEVEL% NEQ 0 ( echo Publish failed. & pause & exit /b 1 )

echo.
echo ============================================
echo  BUILD SUCCESSFUL
echo ============================================
echo.
echo Executable: bin\Release\net8.0-windows\win-x64\publish\EventTracker.exe
echo.
pause
