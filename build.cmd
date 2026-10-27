@echo off
setlocal
cd /d "%~dp0"
title Scarlet - compilar

where dotnet >nul 2>nul
if errorlevel 1 (
  echo.
  echo  No encontre el SDK de .NET 8.
  echo  Instalalo con:   winget install Microsoft.DotNet.SDK.8
  echo  o desde:         https://dotnet.microsoft.com/download/dotnet/8.0
  echo  y vuelve a abrir este archivo.
  echo.
  pause
  exit /b 1
)

echo.
echo  Compilando Scarlet (la primera vez descarga paquetes, puede tardar unos minutos)...
echo.
dotnet restore XboxScarlet.csproj -r win-x64 -p:SelfContained=true --configfile nuget.config
if errorlevel 1 (
  echo.
  echo  No se pudieron descargar los paquetes de NuGet.
  echo  Revisa que haya internet y que se abra https://api.nuget.org/v3/index.json en el navegador.
  echo  Si usas VPN, proxy o un antivirus con filtro web, prueba sin ellos.
  echo.
  pause
  exit /b 1
)
dotnet publish XboxScarlet.csproj -c Release -r win-x64 --self-contained true -o dist\Scarlet
if errorlevel 1 (
  echo.
  echo  La compilacion fallo. Copia el texto rojo de arriba y mandaselo a Claude.
  echo.
  pause
  exit /b 1
)

echo.
echo  Listo:  %~dp0dist\Scarlet\Scarlet.exe
echo  Si compilaste en otra PC, copia la carpeta dist\Scarlet completa a la Ally.
echo  Recuerda: en AnyFSE elige otra vez Browse - Scarlet.exe (el programa cambio de nombre).
echo.
start "" explorer "%~dp0dist\Scarlet"
pause
