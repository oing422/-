@echo off
setlocal
where dotnet >nul 2>nul || (
  echo .NET 8 SDK가 필요합니다.
  pause
  exit /b 1
)
dotnet workload install maui-android
dotnet publish NovelpiaMobile.csproj -f net8.0-android -c Debug
echo.
echo APK 검색:
dir /s /b *.apk
pause
