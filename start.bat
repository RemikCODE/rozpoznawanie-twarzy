@echo off

echo === Start ML (Python) ===
cd /d %~dp0
cd ml
start cmd /k "python service.py"

echo === Start backend (ASP.NET - LAN) ===
cd /d %~dp0
cd backend\FaceRecognitionApi
start cmd /k dotnet run --launch-profile Lan

echo === Start frontend (Vue) ===
cd /d %~dp0
cd webowka
cd face-recognition-web
start cmd /k "npm run dev"

echo === Start desktop (maui) ===
cd /d %~dp0
cd maui
cd MauiApp1
start cmd /k dotnet run -f net10.0-windows10.0.19041.0

echo === Wszystko uruchomione! ===
pause