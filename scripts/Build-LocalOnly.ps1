param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$ArtifactsRoot = ""
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($ArtifactsRoot))
{
    $artifactsRoot = Join-Path $repoRoot "artifacts\local-only"
}
else
{
    $artifactsRoot = $ArtifactsRoot
}

$publishRoot = Join-Path $artifactsRoot "publish"
$stagingRoot = Join-Path $artifactsRoot "staging"
$packageRoot = Join-Path $artifactsRoot "packages"

$clientPublish = Join-Path $publishRoot "JSAI_LocalOnly"
$clientInstallerPublish = Join-Path $publishRoot "clientinstaller"
$updaterPublish = Join-Path $publishRoot "updater"
$configuratorPublish = Join-Path $publishRoot "configurator"
$clientStage = Join-Path $stagingRoot "client"

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
    $source = Join-Path $repoRoot "Workflowsapi"
    if (-not (Test-Path $source))
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
Publish-Project (Join-Path $repoRoot "Updater\Updater.csproj") $updaterPublish
Publish-Project (Join-Path $repoRoot "ClientConfigurator\ClientConfigurator.csproj") $configuratorPublish

Copy-Files $updaterPublish $clientPublish
Copy-Files $configuratorPublish $clientPublish
Copy-WorkflowApi $clientPublish

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

Reset-Directory $clientStage

$payloadZip = Join-Path $clientStage "JSAI_LocalOnly_Payload.zip"
$installerExe = Join-Path $clientInstallerPublish "ClientInstaller.exe"
$setupExe = Join-Path $packageRoot "JSAI_LocalOnly_Setup.exe"
$packageZip = Join-Path $packageRoot "JSAI_LocalOnly_Payload.zip"
$portableZip = Join-Path $packageRoot "JSAI_LocalOnly_Portable.zip"

Compress-Archive -Path (Join-Path $clientPublish "*") -DestinationPath $payloadZip -Force
Copy-Item -LiteralPath $payloadZip -Destination $packageZip -Force
Compress-Archive -Path (Join-Path $clientPublish "*") -DestinationPath $portableZip -Force

if (-not (Test-Path $installerExe))
{
    throw "ClientInstaller.exe was not found after publish."
}

New-EmbeddedBootstrapper $installerExe $payloadZip $setupExe

Write-Host ""
Write-Host "Local-only build completed."
Write-Host "Publish  : $clientPublish"
Write-Host "Setup    : $setupExe"
Write-Host "Payload  : $packageZip"
Write-Host "Portable : $portableZip"
