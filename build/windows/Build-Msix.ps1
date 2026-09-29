[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+(?:\.\d+)?$')]
    [string] $Version,

    [Parameter(Mandatory)]
    [ValidateSet('x64', 'arm64')]
    [string] $Architecture,

    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string] $Publisher,

    [ValidatePattern('^[A-Za-z0-9-]+/[A-Za-z0-9_.-]+$')]
    [string] $ReleaseRepository = 'Redth/MAUI.Sherpa',

    [Parameter(Mandatory)]
    [ValidatePattern('^v\d+\.\d+\.\d+(?:\.\d+)?$')]
    [string] $ReleaseTag,

    [Parameter(Mandatory)]
    [string] $OutputDirectory,

    [string] $SentryDsn,

    [switch] $PrepareOnly
)

$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$output = [IO.Path]::GetFullPath($OutputDirectory)
$parsedVersion = [version]$Version
$revision = [Math]::Max(0, $parsedVersion.Revision)
$packageVersion = [version]::new($parsedVersion.Major, $parsedVersion.Minor, $parsedVersion.Build, $revision)
if ($packageVersion.Major -eq 0 -and $packageVersion.Minor -eq 0 -and
    $packageVersion.Build -eq 0 -and $packageVersion.Revision -eq 0) {
    throw 'The MSIX version must not be 0.0.0.0.'
}
foreach ($part in $packageVersion.ToString().Split('.')) {
    if ([int]$part -gt 65535) { throw 'Each MSIX version component must be at most 65535.' }
}
$tagVersion = [version]$ReleaseTag.Substring(1)
if ($tagVersion.Major -ne $packageVersion.Major -or $tagVersion.Minor -ne $packageVersion.Minor -or
    $tagVersion.Build -ne $packageVersion.Build -or [Math]::Max(0, $tagVersion.Revision) -ne $revision) {
    throw 'ReleaseTag and Version must identify the same version.'
}
if (-not $PrepareOnly -and -not $IsWindows) { throw 'MSIX builds require Windows.' }

$metadataDirectory = Join-Path $output 'metadata'
New-Item -ItemType Directory -Force -Path $metadataDirectory | Out-Null
$manifestPath = Join-Path $metadataDirectory 'Package.appxmanifest'
$appInstallerPath = Join-Path $metadataDirectory 'install.appinstaller'

[xml]$manifest = Get-Content (Join-Path $PSScriptRoot 'Package.appxmanifest') -Raw
$manifest.Package.Identity.Publisher = $Publisher
$manifest.Package.Identity.Version = $packageVersion.ToString()
$manifest.Save($manifestPath)

$namespace = 'http://schemas.microsoft.com/appx/appinstaller/2018'
$installer = [xml]::new()
$root = $installer.CreateElement('AppInstaller', $namespace)
$root.SetAttribute('Uri', "https://github.com/$ReleaseRepository/releases/download/windows-latest/MAUI-Sherpa.win-$Architecture.appinstaller")
$root.SetAttribute('Version', $packageVersion.ToString())
[void]$installer.AppendChild($root)
$mainPackage = $installer.CreateElement('MainPackage', $namespace)
$mainPackage.SetAttribute('Name', $manifest.Package.Identity.Name)
$mainPackage.SetAttribute('Publisher', $Publisher)
$mainPackage.SetAttribute('Version', $packageVersion.ToString())
$mainPackage.SetAttribute('ProcessorArchitecture', $Architecture)
$mainPackage.SetAttribute('Uri', "https://github.com/$ReleaseRepository/releases/download/$ReleaseTag/MAUI-Sherpa.win-$Architecture.msix")
[void]$root.AppendChild($mainPackage)
$settings = $installer.CreateElement('UpdateSettings', $namespace)
$onLaunch = $installer.CreateElement('OnLaunch', $namespace)
$onLaunch.SetAttribute('HoursBetweenUpdateChecks', '24')
$onLaunch.SetAttribute('ShowPrompt', 'false')
$onLaunch.SetAttribute('UpdateBlocksActivation', 'false')
[void]$settings.AppendChild($onLaunch)
[void]$settings.AppendChild($installer.CreateElement('AutomaticBackgroundTask', $namespace))
[void]$root.AppendChild($settings)
$installer.Save($appInstallerPath)
Copy-Item $appInstallerPath (Join-Path $output "MAUI-Sherpa.win-$Architecture.appinstaller") -Force
if ($PrepareOnly) { return }

# Build and package together so WinRT activation metadata and MAUI resources are retained.
$packageDirectory = Join-Path $output "packages/$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Force -Path $packageDirectory | Out-Null
$arguments = @(
    'build', (Join-Path $repoRoot 'src/MauiSherpa/MauiSherpa.csproj'),
    '-f', 'net10.0-windows10.0.19041.0', '-c', 'Release',
    "-p:Platform=$Architecture", "-p:RuntimeIdentifier=win-$Architecture",
    '-p:SherpaMsixBuild=true',
    "-p:SherpaMsixManifest=$manifestPath", "-p:SherpaAppInstaller=$appInstallerPath",
    "-p:SherpaReleaseRepository=$ReleaseRepository",
    "-p:AppVersion=$Version",
    "-p:ApplicationDisplayVersion=$($packageVersion.Major).$($packageVersion.Minor).$($packageVersion.Build)",
    "-p:ApplicationVersion=$revision",
    "-p:AppxPackageDir=$packageDirectory/", '-verbosity:minimal'
)
if (-not [string]::IsNullOrWhiteSpace($SentryDsn)) {
    $arguments += "-p:SentryDsn=$SentryDsn"
}
& dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw "MSIX build failed for $Architecture." }
$packages = @(Get-ChildItem $packageDirectory -Recurse -Filter '*.msix' |
    Where-Object { $_.Name -notmatch 'symbols' })
if ($packages.Count -ne 1) { throw "Expected one MSIX for $Architecture; found $($packages.Count)." }

$packagePath = $packages[0].FullName
$archive = [IO.Compression.ZipFile]::OpenRead($packagePath)
try {
    $manifestEntry = $archive.GetEntry('AppxManifest.xml')
    $reader = [IO.StreamReader]::new($manifestEntry.Open())
    try { [xml]$builtManifest = $reader.ReadToEnd() } finally { $reader.Dispose() }
    if ($builtManifest.Package.Identity.Name -ne $manifest.Package.Identity.Name -or
        $builtManifest.Package.Identity.Publisher -ne $Publisher -or
        $builtManifest.Package.Identity.Version -ne $packageVersion.ToString() -or
        $builtManifest.Package.Identity.ProcessorArchitecture -ne $Architecture) {
        throw 'Built package identity does not match the App Installer metadata.'
    }
    if ($builtManifest.SelectNodes("//*[local-name()='PackageDependency']") |
        Where-Object { $_.Name -like 'Microsoft.WindowsAppRuntime*' }) {
        throw 'Direct MSIX must bundle the Windows App SDK runtime.'
    }
    foreach ($required in @('MauiSherpa.exe', 'MauiSherpa.dll', 'coreclr.dll', 'Microsoft.UI.Xaml.dll', 'Microsoft.WindowsAppRuntime.dll')) {
        if (-not ($archive.Entries | Where-Object { $_.Name -eq $required })) {
            throw "MSIX is missing required self-contained payload: $required"
        }
    }
    if (-not ($archive.Entries | Where-Object { $_.FullName -like '*wwwroot/index.html' })) {
        throw 'MSIX is missing the Blazor host page.'
    }
    $builtManifestText = $builtManifest.OuterXml
    if ($builtManifestText.Contains('$placeholder$') -or
        -not $builtManifest.SelectSingleNode("//*[local-name()='AutoUpdate']/*[local-name()='AppInstaller' and @File='install.appinstaller']")) {
        throw 'MSIX contains unresolved MAUI branding or missing automatic update metadata.'
    }
    foreach ($variant in @('*Logo.scale-100.png', '*Logo.altform-unplated_targetsize-16.png', '*Logo.altform-lightunplated_targetsize-16.png')) {
        if (-not ($archive.Entries | Where-Object { $_.Name -like $variant })) {
            throw "MSIX is missing generated Windows icon variant: $variant"
        }
    }
    $embedded = $archive.GetEntry('install.appinstaller')
    if (-not $embedded) { throw 'MSIX is missing install.appinstaller.' }
    $reader = [IO.StreamReader]::new($embedded.Open())
    try { $embeddedText = $reader.ReadToEnd() } finally { $reader.Dispose() }
    if ($embeddedText -cne [IO.File]::ReadAllText($appInstallerPath)) {
        throw 'Embedded App Installer metadata differs from the published descriptor.'
    }
}
finally {
    $archive.Dispose()
}

$destination = Join-Path $output "MAUI-Sherpa.win-$Architecture.msix"
Copy-Item $packagePath $destination -Force
Write-Host "$Architecture MSIX: $([Math]::Round((Get-Item $destination).Length / 1MB, 2)) MiB"
