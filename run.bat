@echo off
chcp 65001 >nul
title EventTracker - Run
echo.
echo  [*] Starting EventTracker...
echo.
dotnet run --project EventTracker.csproj
