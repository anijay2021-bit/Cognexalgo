@echo off
echo Building Cognexalgo Desktop App...
dotnet build Cognexalgo.UI\Cognexalgo.UI.csproj
if %errorlevel% neq 0 (
    echo Build Failed!
    pause
    exit /b %errorlevel%
)

echo Starting App...
start Cognexalgo.UI\bin\Debug\net8.0-windows\Cognexalgo.UI.exe
