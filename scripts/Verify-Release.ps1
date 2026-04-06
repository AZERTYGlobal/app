$ErrorActionPreference = 'Stop'

$projectRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$srcDir = Join-Path $projectRoot 'src'
$msixDir = Join-Path $projectRoot 'msix'

$csprojPath = Join-Path $srcDir 'AZERTYGlobal.csproj'
$programPath = Join-Path $srcDir 'Program.cs'
$assemblyInfoPath = Join-Path $srcDir 'Properties/AssemblyInfo.cs'
$manifestPath = Join-Path $msixDir 'AppxManifest.xml'
$fichePath = Join-Path $msixDir 'Fiche Store.md'
$publicationPath = Join-Path $projectRoot 'Publication Microsoft Store.md'
$todoPath = Join-Path $projectRoot 'TO-DO.md'
$changelogPath = Join-Path $projectRoot 'Changelog.md'
$contextAppPath = Resolve-Path (Join-Path $projectRoot '..\..\..\.agent\CONTEXT_APP_MICROSOFT_STORE.md')
$contextProjectPath = Resolve-Path (Join-Path $projectRoot '..\..\..\.agent\CONTEXT_AZERTY_GLOBAL.md')
$publishExePath = Join-Path $srcDir 'bin\Release\net8.0-windows10.0.17763.0\win-x64\publish\AZERTY Global.exe'
$stagedExePath = Join-Path $msixDir 'AZERTY Global.exe'
$msixPackagePath = Join-Path $msixDir 'AZERTYGlobal.msix'

function Get-FileText([string]$Path) {
    return [System.IO.File]::ReadAllText($Path)
}

function Assert-Match([string]$Path, [string]$Pattern, [string]$Label) {
    $content = Get-FileText $Path
    if ($content -notmatch $Pattern) {
        throw "$Label invalide dans $Path"
    }
}

function Get-ZipEntryHash([string]$ZipPath, [string]$EntryName) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [System.IO.Compression.ZipFile]::OpenRead($ZipPath)
    try {
        $entry = $zip.Entries | Where-Object {
            $_.FullName -eq $EntryName -or
            [System.Uri]::UnescapeDataString($_.FullName) -eq $EntryName
        } | Select-Object -First 1
        if (-not $entry) {
            throw "Entrée $EntryName introuvable dans $ZipPath"
        }

        $stream = $entry.Open()
        try {
            $sha = [System.Security.Cryptography.SHA256]::Create()
            try {
                $hashBytes = $sha.ComputeHash($stream)
                return ([System.BitConverter]::ToString($hashBytes)).Replace('-', '')
            }
            finally {
                $sha.Dispose()
            }
        }
        finally {
            $stream.Dispose()
        }
    }
    finally {
        $zip.Dispose()
    }
}

[xml]$csproj = Get-FileText $csprojPath
$version = $csproj.Project.PropertyGroup.Version
if ([string]::IsNullOrWhiteSpace($version)) {
    throw "Version introuvable dans $csprojPath"
}

$storeVersion = "$version.0"
$programText = Get-FileText $programPath
$assemblyInfoText = Get-FileText $assemblyInfoPath
[xml]$manifest = Get-FileText $manifestPath

$programVersion = [regex]::Match($programText, 'internal const string Version = "([^"]+)"').Groups[1].Value
if ($programVersion -ne $version) {
    throw "Program.cs n'est pas aligné sur $version"
}

$fileVersion = [regex]::Match($assemblyInfoText, 'AssemblyFileVersion\("([^"]+)"\)').Groups[1].Value
$informationalVersion = [regex]::Match($assemblyInfoText, 'AssemblyInformationalVersion\("([^"]+)"\)').Groups[1].Value
$assemblyVersion = [regex]::Match($assemblyInfoText, 'AssemblyVersion\("([^"]+)"\)').Groups[1].Value

if ($fileVersion -ne $storeVersion) { throw "AssemblyFileVersion n'est pas aligné sur $storeVersion" }
if ($informationalVersion -ne $version) { throw "AssemblyInformationalVersion n'est pas aligné sur $version" }
if ($assemblyVersion -ne $storeVersion) { throw "AssemblyVersion n'est pas aligné sur $storeVersion" }
if ($manifest.Package.Identity.Version -ne $storeVersion) { throw "AppxManifest.xml n'est pas aligné sur $storeVersion" }

Assert-Match $fichePath ("Version {0} :" -f [regex]::Escape($version)) 'Fiche Store FR'
Assert-Match $fichePath ("Version {0}:" -f [regex]::Escape($version)) 'Fiche Store EN'
Assert-Match $publicationPath ("Version cible : {0}" -f [regex]::Escape($version)) 'Publication Microsoft Store'
Assert-Match $publicationPath ("Package Store : {0}" -f [regex]::Escape($storeVersion)) 'Publication Microsoft Store'
Assert-Match $todoPath ("Version actuelle : {0}" -f [regex]::Escape($version)) 'TO-DO'
Assert-Match $changelogPath ("## Version {0}" -f [regex]::Escape($version)) 'Changelog'
Assert-Match $contextAppPath ("> \*\*Version actuelle\*\* : {0}" -f [regex]::Escape($version)) 'Contexte app'
Assert-Match $contextProjectPath ("\*\*Application Microsoft Store\*\* v{0}" -f [regex]::Escape($version)) 'Contexte projet'

foreach ($path in @($publishExePath, $stagedExePath, $msixPackagePath)) {
    if (-not (Test-Path $path)) {
        throw "Fichier requis introuvable: $path"
    }
}

$publishHash = (Get-FileHash $publishExePath -Algorithm SHA256).Hash
$stagedHash = (Get-FileHash $stagedExePath -Algorithm SHA256).Hash
if ($publishHash -ne $stagedHash) {
    throw "L'exe publié et l'exe stagé dans msix/ ne correspondent pas"
}

$packageHash = Get-ZipEntryHash $msixPackagePath 'AZERTY Global.exe'
if ($publishHash -ne $packageHash) {
    throw "L'exe embarqué dans AZERTYGlobal.msix ne correspond pas au publish courant"
}

Write-Host "Release vérifiée: version $version / package $storeVersion"
Write-Host "SHA256 publish/staged/package: $publishHash"
