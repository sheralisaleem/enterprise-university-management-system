@echo off
echo Starting FYP Event Management System...

echo Starting Backend API...
start "Backend API" cmd /k "cd backend-api && title Backend API && dotnet run --launch-profile http"

echo Waiting for API to initialize (5 seconds)...
timeout /t 5 /nobreak >nul

echo Starting Web App...
start "Web App" cmd /k "cd web && title Web App && dotnet run --launch-profile http"

echo Applications are starting up! The web browser will open automatically.
echo You can safely close this terminal window.
pause
