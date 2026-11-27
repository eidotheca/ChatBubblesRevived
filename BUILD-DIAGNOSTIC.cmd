@echo off
cd /d "%~dp0"
dotnet build "ChatBubbles\ChatBubbles.csproj" -c Release --nologo > "%~dp0build-report.txt" 2>&1
set "BUILD_RESULT=%ERRORLEVEL%"
type "%~dp0build-report.txt"
if not "%BUILD_RESULT%"=="0" (
  echo Build failed. See build-report.txt.
  pause
  exit /b %BUILD_RESULT%
)
echo Build succeeded. DLL: ChatBubbles\bin\Release\net10.0-windows\ChatBubblesRevived.dll
pause
exit /b 0
