# Compila dist\ScreenshotUploader.exe: un solo file, .NET incluso (non serve installare niente).
# Niente EnableCompressionInSingleFile: l'.exe pesa la meta' ma decomprime tutto in memoria
# (52 MB privati invece di 13 per un'app che resta sempre aperta).
#   powershell -ExecutionPolicy Bypass -File app\build.ps1 [-Dotnet <percorso di dotnet.exe>]
param([string]$Dotnet = "dotnet")
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

& $Dotnet publish ScreenshotUploader.csproj -c Release -r win-x64 --self-contained `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=none -o ..\dist
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Get-Item ..\dist\ScreenshotUploader.exe | Select-Object Name, @{n = "MB"; e = { [math]::Round($_.Length / 1MB, 1) } }
