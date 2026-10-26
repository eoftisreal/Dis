@echo off
setlocal
cd /d "%~dp0"
where dotnet >nul 2>nul
if errorlevel 1 (
  echo Install the .NET 10 SDK first: https://dotnet.microsoft.com/download/dotnet/10.0
  exit /b 1
)
dotnet run --project tests\Dis.Core.Tests -c Release
if errorlevel 1 exit /b 1
cmake -S native -B artifacts\native -A x64
if errorlevel 1 exit /b 1
cmake --build artifacts\native --config Release --parallel
if errorlevel 1 exit /b 1
dotnet publish src\Dis.Host\Dis.Host.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o artifacts\windows
if errorlevel 1 exit /b 1
copy /y artifacts\native\Release\Dis.Codec.dll artifacts\windows\Dis.Codec.dll >nul
echo Built artifacts\windows\Dis.exe
