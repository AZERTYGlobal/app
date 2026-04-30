$ErrorActionPreference = 'Stop'

# Architectures cibles (x64 + ARM64 natif)
$architectures = @('x64', 'arm64')

$projectRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$srcDir = Join-Path $projectRoot 'src'
$msixDir = Join-Path $projectRoot 'msix'
$bundleStagingDir = Join-Path $projectRoot '.msix-bundle-staging'
$archivesDir = Join-Path $projectRoot 'Archives'
$csprojPath = Join-Path $srcDir 'AZERTYGlobal.csproj'

# Chemins de sortie
$stableBundlePath = Join-Path $msixDir 'AZERTYGlobal.msixbundle'

function Copy-DirectoryContent([string]$Source, [string]$Destination) {
    Get-ChildItem $Source -Force |
        Where-Object { $_.Name -notlike '*.msix' -and $_.Name -notlike '*.msixbundle' -and
                       $_.Name -notlike '*.exe' -and $_.Name -notlike 'wack-report*' } |
        ForEach-Object {
            $target = Join-Path $Destination $_.Name
            if ($_.PSIsContainer) {
                Copy-Item $_.FullName -Destination $target -Recurse -Force
            }
            else {
                Copy-Item $_.FullName -Destination $target -Force
            }
        }
}

function Resolve-MakeAppx() {
    $direct = Get-Command MakeAppx.exe -ErrorAction SilentlyContinue
    if ($direct) {
        return $direct.Source
    }

    $sdkTool = Get-ChildItem 'C:\Program Files (x86)\Windows Kits\10\bin' -Recurse -Filter MakeAppx.exe -ErrorAction SilentlyContinue |
        Where-Object FullName -like '*\x64\MakeAppx.exe' |
        Sort-Object FullName -Descending |
        Select-Object -First 1

    if (-not $sdkTool) {
        throw 'MakeAppx.exe introuvable. Installer le Windows 10/11 SDK.'
    }

    return $sdkTool.FullName
}

# Lire la version depuis le .csproj
[xml]$csproj = [System.IO.File]::ReadAllText($csprojPath)
$version = $csproj.Project.PropertyGroup.Version
if ([string]::IsNullOrWhiteSpace($version)) {
    throw "Version introuvable dans $csprojPath"
}

$storeVersion = "$version.0"
$versionedBundlePath = Join-Path $projectRoot ("AZERTYGlobal-{0}.msixbundle" -f $storeVersion)

# Vérifier que les exécutables publiés existent
foreach ($arch in $architectures) {
    $publishExe = Join-Path $srcDir "bin\Release\net8.0-windows10.0.17763.0\win-$arch\publish\AZERTY Global.exe"
    if (-not (Test-Path $publishExe)) {
        throw "Publish introuvable pour $arch : $publishExe`nLancer: dotnet publish -c Release -r win-$arch"
    }
}

$makeAppx = Resolve-MakeAppx
$msixFiles = @()

# Créer un .msix par architecture
foreach ($arch in $architectures) {
    $stagingDir = Join-Path $projectRoot ".msix-staging-$arch"
    $publishExe = Join-Path $srcDir "bin\Release\net8.0-windows10.0.17763.0\win-$arch\publish\AZERTY Global.exe"
    $msixPath = Join-Path $projectRoot ("AZERTYGlobal-{0}-{1}.msix" -f $storeVersion, $arch)

    Write-Host "--- Construction $arch ---"

    # Nettoyer le staging
    if (Test-Path $stagingDir) {
        Remove-Item -LiteralPath $stagingDir -Recurse -Force
    }
    New-Item -ItemType Directory -Path $stagingDir | Out-Null

    # Copier le template msix/ (Assets, manifest, config)
    Copy-DirectoryContent $msixDir $stagingDir

    # Copier l'exécutable publié
    Copy-Item $publishExe (Join-Path $stagingDir 'AZERTY Global.exe') -Force

    # Ajuster ProcessorArchitecture dans le manifest copié
    $manifestPath = Join-Path $stagingDir 'AppxManifest.xml'
    [xml]$manifest = [System.IO.File]::ReadAllText($manifestPath)
    $manifest.Package.Identity.ProcessorArchitecture = $arch
    $manifest.Save($manifestPath)

    # Empaqueter
    if (Test-Path $msixPath) {
        Remove-Item -LiteralPath $msixPath -Force
    }
    & $makeAppx pack /d $stagingDir /p $msixPath
    if ($LASTEXITCODE -ne 0) { throw "MakeAppx pack a échoué pour $arch" }

    $msixFiles += $msixPath

    # Nettoyer le staging
    Remove-Item -LiteralPath $stagingDir -Recurse -Force
}

# Créer le bundle
Write-Host "--- Construction du bundle ---"
if (Test-Path $bundleStagingDir) {
    Remove-Item -LiteralPath $bundleStagingDir -Recurse -Force
}
New-Item -ItemType Directory -Path $bundleStagingDir | Out-Null

foreach ($msix in $msixFiles) {
    Copy-Item $msix $bundleStagingDir -Force
}

if (Test-Path $versionedBundlePath) {
    Remove-Item -LiteralPath $versionedBundlePath -Force
}

& $makeAppx bundle /d $bundleStagingDir /p $versionedBundlePath
if ($LASTEXITCODE -ne 0) { throw "MakeAppx bundle a échoué" }

# Archiver l'ancien bundle stable
if (Test-Path $stableBundlePath) {
    New-Item -ItemType Directory -Path $archivesDir -Force | Out-Null
    $timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $backupPath = Join-Path $archivesDir ("AZERTYGlobal-previous-{0}.msixbundle" -f $timestamp)
    Move-Item -LiteralPath $stableBundlePath -Destination $backupPath -Force
}

# Copier le bundle versionné en stable
Copy-Item $versionedBundlePath $stableBundlePath -Force

# Nettoyer
Remove-Item -LiteralPath $bundleStagingDir -Recurse -Force
foreach ($msix in $msixFiles) {
    Remove-Item -LiteralPath $msix -Force
}

Write-Host ""
Write-Host "MSIX bundle reconstruit:"
Write-Host " - Versioned : $versionedBundlePath"
Write-Host " - Stable    : $stableBundlePath"
Write-Host " - Archs     : $($architectures -join ', ')"
