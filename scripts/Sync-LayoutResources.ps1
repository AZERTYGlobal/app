param(
    [switch]$SyncPublicRepo
)

$ErrorActionPreference = 'Stop'

$projectRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
function Resolve-FirstExistingPath([string[]]$Candidates, [string]$Label) {
    foreach ($candidate in $Candidates) {
        if (Test-Path $candidate) {
            return (Resolve-Path $candidate)
        }
    }
    throw "$Label introuvable. Chemins testes: $($Candidates -join '; ')"
}

$siteRoot = Resolve-FirstExistingPath @(
    (Join-Path $projectRoot '..\Site AZERTY Global'),
    (Join-Path $projectRoot '..\2026\Site AZERTY Global')
) 'Site AZERTY Global'
$publicRepoRoot = Resolve-FirstExistingPath @(
    (Join-Path $projectRoot '..\..\Microsoft Store - app repo'),
    $projectRoot
) 'Clone public Microsoft Store'

$sourceLayout = Join-Path $siteRoot 'data\AZERTY Global.json'
$sourceIndex = Join-Path $siteRoot 'tester\character-index.json'
$targetLayout = Join-Path $projectRoot 'src\AZERTY Global 2026.json'
$targetIndex = Join-Path $projectRoot 'src\character-index.json'

function Show-GitStatus([string]$RepoPath) {
    $status = git -C $RepoPath status --porcelain
    if ($LASTEXITCODE -ne 0) {
        throw "Impossible de verifier l'etat Git de $RepoPath"
    }
    if ($status) {
        Write-Host "Clone public deja modifie ; seuls les fichiers allowlistes seront synchronises :"
        $status | ForEach-Object { Write-Host " - $_" }
    }
}

function Copy-Exact([string]$Source, [string]$Destination) {
    if ((Test-Path -LiteralPath $Source) -and (Test-Path -LiteralPath $Destination)) {
        $sourceHash = (Get-FileHash -LiteralPath $Source -Algorithm SHA256).Hash
        $destinationHash = (Get-FileHash -LiteralPath $Destination -Algorithm SHA256).Hash
        if ($sourceHash -eq $destinationHash) {
            return
        }
    }

    try {
        [System.IO.File]::Copy($Source, $Destination, $true)
    } catch [System.UnauthorizedAccessException] {
        $sourceResolved = [System.IO.Path]::GetFullPath($Source)
        $destinationResolved = [System.IO.Path]::GetFullPath($Destination)
        $projectResolved = [System.IO.Path]::GetFullPath($projectRoot)
        $publicResolved = if (Test-Path $publicRepoRoot) { [System.IO.Path]::GetFullPath((Resolve-Path $publicRepoRoot)) } else { '' }
        $isInProject = $destinationResolved.StartsWith($projectResolved, [System.StringComparison]::OrdinalIgnoreCase)
        $isInPublic = $publicResolved -and $destinationResolved.StartsWith($publicResolved, [System.StringComparison]::OrdinalIgnoreCase)

        if (-not ($isInProject -or $isInPublic)) {
            throw "Destination hors perimetre de synchronisation: $destinationResolved"
        }

        $sourceDir = Split-Path -Parent $sourceResolved
        $destinationDir = Split-Path -Parent $destinationResolved
        $fileName = Split-Path -Leaf $sourceResolved
        & robocopy $sourceDir $destinationDir $fileName /R:0 /W:0 /NFL /NDL /NJH /NJS | Out-Null
        if ($LASTEXITCODE -gt 7) {
            throw "Robocopy a echoue pour $destinationResolved (exit $LASTEXITCODE)"
        }
    }
}

function Copy-AllowedPublicFile([string]$Source, [string]$Destination) {
    if (-not (Test-Path $Source)) {
        throw "Source allowlistee introuvable: $Source"
    }
    if (-not (Test-Path $Destination)) {
        throw "Destination allowlistee introuvable: $Destination"
    }
    Copy-Exact $Source $Destination
}

function Invoke-ResourceValidation([string]$LayoutPath, [string]$IndexPath) {
    $validator = @'
const fs = require('fs');
const [layoutPath, indexPath] = process.argv.slice(-2);
function readJson(path) {
  return JSON.parse(fs.readFileSync(path, 'utf8').replace(/^\uFEFF/, ''));
}
function assertEqual(actual, expected, label) {
  if (actual !== expected) {
    throw new Error(`${label} invalide: attendu ${JSON.stringify(expected)}, obtenu ${JSON.stringify(actual)}`);
  }
}
function keyByPosition(layout, position) {
  for (const row of layout.rows || []) {
    for (const key of row.keys || []) {
      if (key.position === position) return key;
    }
  }
  throw new Error(`Position ${position} introuvable`);
}
function character(index, value) {
  const entry = (index.characters || {})[value];
  if (!entry) throw new Error(`Entree character-index introuvable: ${value}`);
  return entry;
}
function assertAlias(entry, alias, label) {
  if (!Array.isArray(entry.frenchAliases) || !entry.frenchAliases.includes(alias)) {
    throw new Error(`${label}: alias ${alias} introuvable`);
  }
}
function assertDirectMethod(entry, key, layer, recommended, label) {
  const method = (entry.methods || []).find(m => m.type === 'direct' && m.key === key && m.layer === layer);
  if (!method) throw new Error(`${label}: methode directe ${key}/${layer} introuvable`);
  if (!!method.recommended !== recommended) {
    throw new Error(`${label}: recommended attendu ${recommended}, obtenu ${!!method.recommended}`);
  }
}
function assertDeadKeyActivation(entry, deadkey, key, layer, label) {
  const method = (entry.methods || []).find(m =>
    m.type === 'deadkey_activation' &&
    m.deadkey === deadkey &&
    m.key === key &&
    m.layer === layer &&
    m.recommended === true);
  if (!method) throw new Error(`${label}: activation ${deadkey} ${key}/${layer} introuvable`);
}
function assertDeadKeyMethod(entry, deadkey, key, layer, recommended, label) {
  const method = (entry.methods || []).find(m =>
    m.type === 'deadkey' &&
    m.deadkey === deadkey &&
    m.key === key &&
    m.layer === layer);
  if (!method) throw new Error(`${label}: methode touche morte ${deadkey} ${key}/${layer} introuvable`);
  if (!!method.recommended !== recommended) {
    throw new Error(`${label}: recommended attendu ${recommended}, obtenu ${!!method.recommended}`);
  }
}
const layout = readJson(layoutPath);
const index = readJson(indexPath);
assertEqual(Object.keys(layout.dead_keys || {}).length, 29, 'Nombre de touches mortes');
assertEqual(layout.statistics.direct_characters, 131, 'direct_characters');
assertEqual(layout.statistics.dead_key_combinations, 1016, 'dead_key_combinations');
assertEqual(layout.statistics.total_unique_characters, 1005, 'total_unique_characters');
assertEqual(index.totalCharacters, 1034, 'totalCharacters');
assertEqual(Object.keys(index.characters || {}).length, 1034, 'Nombre de caracteres indexes');
const checks = [
  ['E00', 'shift', '#', '# sur Maj+@'],
  ['B09', 'alt_gr', '#', '# sur AltGr+:'],
  ['D08', 'alt_gr', '^', '^ sur AltGr+I'],
  ['C09', 'alt_gr', '`', 'Backtick sur AltGr+L'],
  ['B06', 'alt_gr', '~', 'Tilde sur AltGr+N'],
  ['A03', 'alt_gr', '\u202F', 'Espace fine insecable sur AltGr+Espace'],
  ['A03', 'shift_alt_gr', '\u00A0', 'Espace insecable sur Maj+AltGr+Espace'],
  ['E06', 'alt_gr', 'dk_extended_latin', 'Latin etendu sur AltGr+6'],
  ['E06', 'shift_alt_gr', '\u2011', 'Tiret insecable sur Maj+AltGr+6'],
  ['E01', 'alt_gr', 'dk_hook', 'Crochet en chef sur AltGr+1'],
  ['E01', 'shift_alt_gr', 'dk_horn', 'Cornu sur Maj+AltGr+1'],
  ['E03', 'alt_gr', 'dk_dot_below', 'Point souscrit sur AltGr+3'],
  ['E03', 'shift_alt_gr', 'dk_dot_above', 'Point en chef sur Maj+AltGr+3']
];
for (const [position, layer, expected, label] of checks) {
  assertEqual(keyByPosition(layout, position)[layer] ?? null, expected, label);
}
assertDirectMethod(character(index, '#'), 'Backquote', 'Shift', true, '# recommande');
assertDirectMethod(character(index, '#'), 'Period', 'AltGr', false, '# alternative developpeur');
assertAlias(character(index, '#'), 'hashtag', '#');
assertDirectMethod(character(index, '^'), 'KeyI', 'AltGr', true, '^');
assertDeadKeyMethod(character(index, '^'), 'dk_circumflex', 'Space', 'Base', false, 'Accent circonflexe espace');
assertEqual(character(index, '^').unicodeNameFr, 'CIRCONFLEXE', '^ nom francais');
assertAlias(character(index, '^'), 'accent circonflexe', '^');
assertDirectMethod(character(index, '`'), 'KeyL', 'AltGr', true, 'Backtick');
assertDeadKeyMethod(character(index, '`'), 'dk_grave', 'Space', 'Base', false, 'Accent grave espace');
assertEqual(character(index, '`').unicodeNameFr, 'BACKTICK', 'Backtick nom francais');
assertAlias(character(index, '`'), 'backtick', 'Backtick');
assertAlias(character(index, '`'), 'accent grave', 'Backtick');
assertDirectMethod(character(index, '~'), 'KeyN', 'AltGr', true, 'Tilde');
assertEqual(character(index, '<').unicodeNameFr, 'SIGNE INF\u00c9RIEUR \u00c0', '< nom francais');
assertAlias(character(index, '<'), 'chevron ouvrant', '<');
assertEqual(character(index, '>').unicodeNameFr, 'SIGNE SUP\u00c9RIEUR \u00c0', '> nom francais');
assertAlias(character(index, '>'), 'chevron fermant', '>');
assertDirectMethod(character(index, '\u202F'), 'Space', 'AltGr', true, 'Espace fine insecable');
assertDirectMethod(character(index, '\u00A0'), 'Space', 'Shift+AltGr', true, 'Espace insecable');
assertDirectMethod(character(index, '\u2011'), 'Digit6', 'Shift+AltGr', true, 'Tiret insecable');
assertDeadKeyActivation(character(index, 'dk:hook'), 'dk_hook', 'Digit1', 'AltGr', 'Crochet en chef');
assertDeadKeyActivation(character(index, 'dk:horn'), 'dk_horn', 'Digit1', 'Shift+AltGr', 'Cornu');
assertDeadKeyActivation(character(index, 'dk:dot_below'), 'dk_dot_below', 'Digit3', 'AltGr', 'Point souscrit');
assertDeadKeyActivation(character(index, 'dk:dot_above'), 'dk_dot_above', 'Digit3', 'Shift+AltGr', 'Point en chef');
assertDeadKeyActivation(character(index, 'dk:extended_latin'), 'dk_extended_latin', 'Digit6', 'AltGr', 'Latin etendu');
assertDeadKeyMethod(character(index, '\u0253'), 'dk_acute', 'KeyB', 'Base', true, 'b crosse minuscule');
assertDeadKeyMethod(character(index, '\u0181'), 'dk_acute', 'KeyB', 'Shift', true, 'b crosse majuscule');
assertDeadKeyMethod(character(index, '\u0199'), 'dk_circumflex', 'KeyK', 'Base', true, 'k crosse minuscule');
assertDeadKeyMethod(character(index, '\u0198'), 'dk_circumflex', 'KeyK', 'Shift', true, 'k crosse majuscule');
assertDeadKeyMethod(character(index, '\u0272'), 'dk_extended_latin', 'KeyJ', 'Base', true, 'n crochet gauche minuscule');
assertDeadKeyMethod(character(index, '\u019D'), 'dk_extended_latin', 'KeyJ', 'Shift', true, 'n crochet gauche majuscule');
assertDeadKeyMethod(character(index, '\u0269'), 'dk_extended_latin', 'KeyI', 'Base', true, 'iota latin minuscule');
assertDeadKeyMethod(character(index, '\u0196'), 'dk_extended_latin', 'KeyI', 'Shift', true, 'iota latin majuscule');
assertDeadKeyMethod(character(index, '\u0188'), 'dk_hook', 'KeyC', 'Base', true, 'c crosse minuscule');
assertDeadKeyMethod(character(index, '\u0187'), 'dk_hook', 'KeyC', 'Shift', true, 'c crosse majuscule');
assertDeadKeyMethod(character(index, '\u01A5'), 'dk_hook', 'KeyP', 'Base', true, 'p crosse minuscule');
assertDeadKeyMethod(character(index, '\u01A4'), 'dk_hook', 'KeyP', 'Shift', true, 'p crosse majuscule');
assertDeadKeyMethod(character(index, '\u02BC'), 'dk_acute', 'Digit4', 'Base', true, 'apostrophe modificative prioritaire');
assertDeadKeyMethod(character(index, '\u02BC'), 'dk_extended_latin', 'Digit4', 'Base', false, 'apostrophe modificative alternative');
assertDeadKeyMethod(character(index, '\u2116'), 'dk_misc_symbols', 'Backquote', 'Base', true, 'symbole numero via arobase');
assertDeadKeyMethod(character(index, '\u2116'), 'dk_misc_symbols', 'Backquote', 'Shift', false, 'symbole numero via croisillon');
assertDeadKeyMethod(character(index, '\u2209'), 'dk_scientific', 'KeyE', 'AltGr', true, 'n appartient pas a');
assertDeadKeyMethod(character(index, '\u2286'), 'dk_scientific', 'KeyJ', 'AltGr', true, 'sous-ensemble ou egal');
assertDeadKeyMethod(character(index, '\u2287'), 'dk_scientific', 'KeyK', 'AltGr', true, 'sur-ensemble ou egal');
'@
    node -e $validator -- $LayoutPath $IndexPath
    if ($LASTEXITCODE -ne 0) {
        throw 'Validation des ressources echouee'
    }
}

if (-not (Test-Path $sourceLayout)) { throw "Source layout introuvable: $sourceLayout" }
if (-not (Test-Path $sourceIndex)) { throw "Source character-index introuvable: $sourceIndex" }

if ($SyncPublicRepo) {
    if (-not (Test-Path $publicRepoRoot)) {
        throw "Clone public introuvable: $publicRepoRoot"
    }
    Show-GitStatus $publicRepoRoot
}

Copy-Exact $sourceLayout $targetLayout
Copy-Exact $sourceIndex $targetIndex

Invoke-ResourceValidation $targetLayout $targetIndex

if ($SyncPublicRepo) {
    $publicRootResolved = Resolve-Path $publicRepoRoot
    Copy-AllowedPublicFile $targetLayout (Join-Path $publicRootResolved 'src\AZERTY Global 2026.json')
    Copy-AllowedPublicFile $targetIndex (Join-Path $publicRootResolved 'src\character-index.json')
    Copy-AllowedPublicFile $PSCommandPath (Join-Path $publicRootResolved 'scripts\Sync-LayoutResources.ps1')
    Copy-AllowedPublicFile (Join-Path $projectRoot 'src\AZERTYGlobal.Tests\ResourceAlignmentTests.cs') (Join-Path $publicRootResolved 'src\AZERTYGlobal.Tests\ResourceAlignmentTests.cs')
    Copy-AllowedPublicFile (Join-Path $projectRoot 'Changelog.md') (Join-Path $publicRootResolved 'Changelog.md')
    Copy-AllowedPublicFile (Join-Path $projectRoot 'msix\Fiche Store.md') (Join-Path $publicRootResolved 'msix\Fiche Store.md')
    Invoke-ResourceValidation (Join-Path $publicRootResolved 'src\AZERTY Global 2026.json') (Join-Path $publicRootResolved 'src\character-index.json')
}

Write-Host 'Ressources layout synchronisees.'
Write-Host " - Layout: 29 touches mortes"
Write-Host " - Character index: 1034 entrees (1005 caracteres Unicode + 29 touches mortes)"
if ($SyncPublicRepo) {
    Write-Host " - Clone public synchronise: $publicRepoRoot"
}
