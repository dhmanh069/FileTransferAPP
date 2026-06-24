@echo off

start "" dotnet run --project FileTransferServer

timeout /t 2 >nul

start "" dotnet run --project FileTransferClient

start "" dotnet run --project FileTransferClient