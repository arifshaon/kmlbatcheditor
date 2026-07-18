param(
    [switch]$Installer
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$Project = Join-Path $Root "KmlScopedEditor\KmlScopedEditor.csproj"
$PublishDir = Join-Path $Root "artifacts\publish\win-x64"
$PortableDir = Join-Path $Root "artifacts\portable"
$InstallerDir = Join-Path $Root "artifacts\installer"
$InstallerScript = Join-Path $Root "Installer\KmlScopedEditor.iss"

function Assert-Command {
    param([Parameter(Mandatory = $true)][string]$Name)

    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "'$Name' was not found. Install the .NET 8 SDK or later, then run this script again."
    }
}

Assert-Command "dotnet"

if (-not (Test-Path -LiteralPath $Project)) {
    throw "Project file not found: $Project"
}

$SdkVersion = (& dotnet --version).Trim()
$StableSdkVersion = $SdkVersion.Split('-')[0]

if ([version]$StableSdkVersion -lt [version]"8.0.0") {
    throw "The .NET 8 SDK or later is required. Detected SDK: $SdkVersion"
}

[xml]$ProjectXml = Get-Content -LiteralPath $Project
$Version = $ProjectXml.Project.PropertyGroup |
    ForEach-Object { [string]$_.Version } |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
    Select-Object -First 1

if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = "1.0.0"
}

Write-Host "Publishing KML Scoped Editor $Version for Windows x64..." -ForegroundColor Cyan

foreach ($Path in @($PublishDir, $PortableDir)) {
    if (Test-Path -LiteralPath $Path) {
        Remove-Item -LiteralPath $Path -Recurse -Force
    }

    New-Item -ItemType Directory -Path $Path -Force | Out-Null
}

& dotnet publish $Project `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --output $PublishDir `
    --nologo `
    --verbosity minimal `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:PublishTrimmed=false `
    -p:DebugType=None `
    -p:DebugSymbols=false

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

$PublishedExe = Join-Path $PublishDir "KmlScopedEditor.exe"

if (-not (Test-Path -LiteralPath $PublishedExe)) {
    throw "Publish completed, but KmlScopedEditor.exe was not found in $PublishDir"
}

$PortableZip = Join-Path $PortableDir "KML-Scoped-Editor-Portable-$Version-win-x64.zip"
Compress-Archive `
    -Path (Join-Path $PublishDir "*") `
    -DestinationPath $PortableZip `
    -CompressionLevel Optimal `
    -Force

Write-Host "Published application:" -ForegroundColor Green
Write-Host "  $PublishedExe"
Write-Host "Portable ZIP:" -ForegroundColor Green
Write-Host "  $PortableZip"

if (-not $Installer) {
    Write-Host "Portable build completed successfully." -ForegroundColor Green
    exit 0
}

if (-not (Test-Path -LiteralPath $InstallerScript)) {
    throw "Inno Setup script not found: $InstallerScript"
}

$InnoCandidates = @(
    (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"),
    (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"),
    (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe")
) | Where-Object { $_ -and (Test-Path -LiteralPath $_) }

$Iscc = $InnoCandidates | Select-Object -First 1

if (-not $Iscc) {
    throw @"
Inno Setup 6 was not found.
Install it and run Build-Installer.cmd again.

Using Windows Package Manager:
  winget install --id JRSoftware.InnoSetup -e
"@
}

if (Test-Path -LiteralPath $InstallerDir) {
    Remove-Item -LiteralPath $InstallerDir -Recurse -Force
}

New-Item -ItemType Directory -Path $InstallerDir -Force | Out-Null

Write-Host "Building Setup.exe with Inno Setup..." -ForegroundColor Cyan

& $Iscc "/DMyAppVersion=$Version" $InstallerScript

if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup failed with exit code $LASTEXITCODE."
}

$Setup = Get-ChildItem `
    -LiteralPath $InstallerDir `
    -Filter "KML-Scoped-Editor-Setup-*.exe" |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if (-not $Setup) {
    throw "Installer compilation completed, but no Setup.exe was found in $InstallerDir"
}

Write-Host "Installer created:" -ForegroundColor Green
Write-Host "  $($Setup.FullName)"
Write-Host "Keep the AppId in Installer\KmlScopedEditor.iss unchanged for future upgrades." -ForegroundColor Green
