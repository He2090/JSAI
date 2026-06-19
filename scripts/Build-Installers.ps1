param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$ArtifactsRoot = ""
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($ArtifactsRoot))
{
    $artifactsRoot = Join-Path $repoRoot "artifacts\installers"
}
else
{
    $artifactsRoot = $ArtifactsRoot
}
$publishRoot = Join-Path $artifactsRoot "publish"
$stagingRoot = Join-Path $artifactsRoot "staging"
$packageRoot = Join-Path $artifactsRoot "packages"

$clientPublish = Join-Path $publishRoot "client"
$clientInstallerPublish = Join-Path $publishRoot "clientinstaller"
$serverPublish = Join-Path $publishRoot "server"
$serverInstallerPublish = Join-Path $publishRoot "serverinstaller"
$updaterPublish = Join-Path $publishRoot "updater"
$configuratorPublish = Join-Path $publishRoot "configurator"
$localApiPublish = Join-Path $publishRoot "localapi"

$clientStage = Join-Path $stagingRoot "client"
$serverStage = Join-Path $stagingRoot "server"

function Reset-Directory([string]$path)
{
    if (Test-Path $path)
    {
        Remove-Item -LiteralPath $path -Recurse -Force
    }

    New-Item -ItemType Directory -Path $path | Out-Null
}

function Publish-Project([string]$projectPath, [string]$outputPath)
{
    Reset-Directory $outputPath
    & dotnet publish $projectPath -c $Configuration -r $Runtime --self-contained true /p:PublishSingleFile=false /p:PublishReadyToRun=false -o $outputPath
    if ($LASTEXITCODE -ne 0)
    {
        throw "dotnet publish failed: $projectPath"
    }
}

function Publish-SingleFileProject([string]$projectPath, [string]$outputPath)
{
    Reset-Directory $outputPath
    & dotnet publish $projectPath -c $Configuration -r $Runtime --self-contained true /p:PublishSingleFile=true /p:PublishReadyToRun=false /p:IncludeNativeLibrariesForSelfExtract=true -o $outputPath
    if ($LASTEXITCODE -ne 0)
    {
        throw "dotnet publish failed: $projectPath"
    }
}

function New-EmbeddedBootstrapper(
    [string]$bootstrapExe,
    [string]$payloadZip,
    [string]$outputExe)
{
    $markerBytes = [System.Text.Encoding]::ASCII.GetBytes("JSAI_PAYLOAD_V1")
    $bootstrapBytes = [System.IO.File]::ReadAllBytes($bootstrapExe)
    $payloadBytes = [System.IO.File]::ReadAllBytes($payloadZip)
    $payloadLengthBytes = [System.BitConverter]::GetBytes([Int64]$payloadBytes.LongLength)

    $stream = [System.IO.File]::Open($outputExe, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write, [System.IO.FileShare]::None)
    try
    {
        $stream.Write($bootstrapBytes, 0, $bootstrapBytes.Length)
        $stream.Write($payloadBytes, 0, $payloadBytes.Length)
        $stream.Write($payloadLengthBytes, 0, $payloadLengthBytes.Length)
        $stream.Write($markerBytes, 0, $markerBytes.Length)
        $stream.Flush()
    }
    finally
    {
        $stream.Dispose()
    }
}

function Copy-Files([string]$source, [string]$destination)
{
    if (-not (Test-Path $source))
    {
        return
    }

    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    Copy-Item -Path (Join-Path $source "*") -Destination $destination -Recurse -Force
}

function Remove-IfExists([string]$path)
{
    if (Test-Path $path)
    {
        Remove-Item -LiteralPath $path -Recurse -Force
    }
}

function Copy-WorkflowApi([string]$destination)
{
    $candidates = @(
        (Join-Path $repoRoot "Workflowsapi"),
        (Join-Path $repoRoot "WinApp\bin\Debug\net8.0-windows\Workflowsapi"),
        (Join-Path $repoRoot "WinApp\bin\Release\net8.0-windows\Workflowsapi")
    )

    $source = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
    if ($null -eq $source)
    {
        Write-Warning "Workflowsapi folder was not found. Local ComfyUI templates will not be bundled."
        return
    }

    Copy-Files $source (Join-Path $destination "Workflowsapi")
}

Reset-Directory $artifactsRoot
New-Item -ItemType Directory -Path $publishRoot | Out-Null
New-Item -ItemType Directory -Path $stagingRoot | Out-Null
New-Item -ItemType Directory -Path $packageRoot | Out-Null

Publish-Project (Join-Path $repoRoot "WinApp\WinApp.csproj") $clientPublish
Publish-SingleFileProject (Join-Path $repoRoot "ClientInstaller\ClientInstaller.csproj") $clientInstallerPublish
Publish-SingleFileProject (Join-Path $repoRoot "ServerInstaller\ServerInstaller.csproj") $serverInstallerPublish
Publish-Project (Join-Path $repoRoot "Updater\Updater.csproj") $updaterPublish
Publish-Project (Join-Path $repoRoot "ClientConfigurator\ClientConfigurator.csproj") $configuratorPublish
Publish-Project (Join-Path $repoRoot "UpdateServer\UpdateServer.csproj") $serverPublish
Publish-Project (Join-Path $repoRoot "LocalApiOnlyApp\LocalApiOnlyApp.csproj") $localApiPublish

Copy-Files $updaterPublish $clientPublish
Copy-Files $configuratorPublish $clientPublish
Copy-WorkflowApi $clientPublish
Copy-WorkflowApi $localApiPublish
Copy-Item -LiteralPath (Join-Path $repoRoot "LocalApiOnlyApp\README.md") -Destination (Join-Path $localApiPublish "README.md") -Force

Remove-IfExists (Join-Path $clientPublish "model-settings.json")
Remove-IfExists (Join-Path $clientPublish "membership-config.json")
Remove-IfExists (Join-Path $clientPublish "update-config.json")
Remove-IfExists (Join-Path $clientPublish "member-credential.dat")
Remove-IfExists (Join-Path $clientPublish "member-session.dat")
Remove-IfExists (Join-Path $clientPublish "machine-license.dat")
Remove-IfExists (Join-Path $clientPublish "crash.log")
Remove-IfExists (Join-Path $clientPublish "updater.log")
Remove-IfExists (Join-Path $clientPublish "logs")
Remove-IfExists (Join-Path $clientPublish "outputs")
Remove-IfExists (Join-Path $clientPublish "XMwenjian")
Remove-IfExists (Join-Path $clientPublish "jiaose")
Remove-IfExists (Join-Path $clientPublish "fenjing")

Remove-IfExists (Join-Path $localApiPublish "local-api-settings.json")
Remove-IfExists (Join-Path $localApiPublish "machine-license.dat")
Remove-IfExists (Join-Path $localApiPublish "outputs")
Remove-IfExists (Join-Path $localApiPublish "logs")

Remove-IfExists (Join-Path $serverPublish "data\membership.db")
Remove-IfExists (Join-Path $serverPublish "admin-config.json")

$serverMailTemplate = @"
{
  "host": "",
  "port": 465,
  "enableSsl": true,
  "username": "",
  "password": "",
  "senderEmail": "",
  "senderName": "JSAI 工作助手"
}
"@
Set-Content -LiteralPath (Join-Path $serverPublish "mail-config.json") -Value $serverMailTemplate -Encoding UTF8

New-Item -ItemType Directory -Path (Join-Path $serverPublish "downloads") -Force | Out-Null
if (-not (Test-Path (Join-Path $serverPublish "downloads\.keep")))
{
    New-Item -ItemType File -Path (Join-Path $serverPublish "downloads\.keep") | Out-Null
}

Reset-Directory $clientStage
Reset-Directory $serverStage

$clientPayloadZip = Join-Path $clientStage "client-payload.zip"
$serverPayloadZip = Join-Path $serverStage "server-payload.zip"
$localApiPayloadZip = Join-Path $packageRoot "JSAI_LocalApiOnly_Payload.zip"
$clientInstallerExe = Join-Path $clientInstallerPublish "ClientInstaller.exe"
$serverInstallerExe = Join-Path $serverInstallerPublish "ServerInstaller.exe"
$clientSetupExe = Join-Path $packageRoot "JSAI_Client_Setup.exe"
$serverSetupExe = Join-Path $packageRoot "JSAI_Server_Setup.exe"

Compress-Archive -Path (Join-Path $clientPublish "*") -DestinationPath $clientPayloadZip -Force
Compress-Archive -Path (Join-Path $serverPublish "*") -DestinationPath $serverPayloadZip -Force
Compress-Archive -Path (Join-Path $localApiPublish "*") -DestinationPath $localApiPayloadZip -Force

if (-not (Test-Path $clientInstallerExe))
{
    throw "ClientInstaller.exe was not found after publish."
}

if (-not (Test-Path $serverInstallerExe))
{
    throw "ServerInstaller.exe was not found after publish."
}

Copy-Item -LiteralPath $clientPayloadZip -Destination (Join-Path $packageRoot "JSAI_Client_Payload.zip") -Force
Copy-Item -LiteralPath $serverPayloadZip -Destination (Join-Path $packageRoot "JSAI_Server_Payload.zip") -Force

New-EmbeddedBootstrapper $clientInstallerExe $clientPayloadZip $clientSetupExe
New-EmbeddedBootstrapper $serverInstallerExe $serverPayloadZip $serverSetupExe

Write-Host ""
Write-Host "Build completed."
Write-Host "Client publish : $clientPublish"
Write-Host "Server publish : $serverPublish"
Write-Host "Packages       : $packageRoot"
