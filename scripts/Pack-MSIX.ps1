$ErrorActionPreference = 'Stop'

$projectRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$srcDir = Join-Path $projectRoot 'src'
$msixDir = Join-Path $projectRoot 'msix'
$stagingDir = Join-Path $projectRoot '.msix-staging'
$archivesDir = Join-Path $projectRoot 'Archives'
$csprojPath = Join-Path $srcDir 'AZERTYGlobal.csproj'
$publishExePath = Join-Path $srcDir 'bin\Release\net8.0-windows10.0.17763.0\win-x64\publish\AZERTY Global.exe'
$stagedExePath = Join-Path $msixDir 'AZERTY Global.exe'
$stablePackagePath = Join-Path $msixDir 'AZERTYGlobal.msix'

function Copy-DirectoryContent([string]$Source, [string]$Destination) {
    Get-ChildItem $Source -Force | Where-Object Name -notlike '*.msix' | ForEach-Object {
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

[xml]$csproj = [System.IO.File]::ReadAllText($csprojPath)
$version = $csproj.Project.PropertyGroup.Version
if ([string]::IsNullOrWhiteSpace($version)) {
    throw "Version introuvable dans $csprojPath"
}

$storeVersion = "$version.0"
$versionedPackagePath = Join-Path $projectRoot ("AZERTYGlobal-{0}.msix" -f $storeVersion)

if (-not (Test-Path $publishExePath)) {
    throw "Publish introuvable: $publishExePath"
}

Copy-Item $publishExePath $stagedExePath -Force

if (Test-Path $stagingDir) {
    Remove-Item -LiteralPath $stagingDir -Recurse -Force
}

New-Item -ItemType Directory -Path $stagingDir | Out-Null
Copy-DirectoryContent $msixDir $stagingDir

$makeAppx = Resolve-MakeAppx
if (Test-Path $versionedPackagePath) {
    Remove-Item -LiteralPath $versionedPackagePath -Force
}

& $makeAppx pack /d $stagingDir /p $versionedPackagePath

if (Test-Path $stablePackagePath) {
    New-Item -ItemType Directory -Path $archivesDir -Force | Out-Null
    $timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $backupPath = Join-Path $archivesDir ("AZERTYGlobal-previous-{0}.msix" -f $timestamp)
    Move-Item -LiteralPath $stablePackagePath -Destination $backupPath -Force
}

Copy-Item $versionedPackagePath $stablePackagePath -Force
Remove-Item -LiteralPath $stagingDir -Recurse -Force

Write-Host "MSIX reconstruit:"
Write-Host " - Versioned : $versionedPackagePath"
Write-Host " - Stable    : $stablePackagePath"
