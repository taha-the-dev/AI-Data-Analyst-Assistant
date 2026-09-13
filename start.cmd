@echo off
REM Starts the API and the frontend together, each in its own window.
REM Optional: set GEMINI_API_KEY first to use Gemini for the assistant.
REM   set GEMINI_API_KEY=your-key-here

echo Starting AI Analyst...
echo.

start "AI Analyst API" cmd /k "cd /d %~dp0backend\AnalystAI.Api && dotnet run --urls http://localhost:5180"

echo Waiting for the API to come up...
timeout /t 8 /nobreak >nul

start "AI Analyst web" cmd /k "cd /d %~dp0frontend && npm run dev"

echo.
echo   API      http://localhost:5180/api/health
echo   Frontend http://localhost:5173
echo.
echo Close both windows to stop.
