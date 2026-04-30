# Packaging MSIX - AZERTY Global

> Objectif : packager l'application AZERTY Global pour le Microsoft Store et la distribution hors Store.

## Prerequis

- Windows 10/11 SDK (fournit `MakeAppx.exe` et `SignTool.exe`)
- Compte developpeur Microsoft Partner Center
- Exe AOT publie (`AZERTY Global.exe`)

## Structure du package

```text
msix/
|-- AppxManifest.xml          <- Manifeste du package
|-- Assets/                   <- Logos, icones et captures Store
|   |-- StoreLogo.png         <- 50x50 px
|   |-- Square44x44Logo.png   <- 44x44 px
|   |-- Square150x150Logo.png <- 150x150 px
|   `-- Wide310x150Logo.png   <- 310x150 px
|-- AZERTY Global.exe         <- Copie depuis ../bin/
`-- README.md                 <- Ce fichier
```

## Etapes

### 1. Reserver le nom de l'app

1. Aller sur https://developer.microsoft.com/en-us/microsoft-store/register
2. Creer ou utiliser un compte Microsoft
3. Reserver le nom "AZERTY Global" dans Partner Center
4. Reporter le `Package Identity Name` et le `Publisher` dans `AppxManifest.xml`

### 2. Verifier les assets visuels

Les fichiers PNG du dossier `Assets/` doivent etre coherents avec la version soumise :

- `StoreLogo.png`
- `Square44x44Logo.png`
- `Square150x150Logo.png`
- `Wide310x150Logo.png`
- captures Store (`Screenshot*.png`)

### 3. Assembler le package

```powershell
powershell -ExecutionPolicy Bypass -File "..\scripts\Pack-MSIX.ps1"
```

`Pack-MSIX.ps1` :

- publie l'exe courant si besoin vous avez deja lance `dotnet publish`
- recopie l'exe publie dans `msix\AZERTY Global.exe`
- cree un dossier de staging temporaire pour eviter d'embarquer un ancien `.msix`
- produit un package versionne dans `Microsoft Store/`
- rafraichit aussi `msix\AZERTYGlobal.msix`

`MakeAppx.exe` se trouve en general dans :

```text
C:\Program Files (x86)\Windows Kits\10\bin\<version>\x64\MakeAppx.exe
```

### 4. Tester localement avant soumission

```powershell
Add-AppxPackage -Path ".\AZERTYGlobal.msix"
```

Verifier :

- l'app se lance depuis le menu Demarrer
- l'icone tray apparait
- les hooks clavier fonctionnent
- la recherche de caracteres fonctionne
- l'app fonctionne dans les applications non admin
- pas de blocage Smart App Control

Avant de poursuivre, exécuter aussi :

```powershell
..\scripts\Verify-Release.ps1
```

### 5. Validation WACK

```powershell
appcert.exe test -appxpackagepath "AZERTYGlobal.msix" -reportoutputpath "wack-report.xml"
```

Corriger les problemes signales avant soumission.

### 6. Soumettre au Store

1. Ouvrir la soumission dans Partner Center
2. Uploader le `.msix` ou `.msixupload`
3. Completer la fiche Store, les captures, la privacy policy et la classification
4. Ajouter une note de certification expliquant l'usage de `WH_KEYBOARD_LL`
5. Soumettre

## Notes importantes

- Le dernier segment de version doit rester a `0` pour le Store (ex: `0.9.5.0`)
- Microsoft re-signe automatiquement le package soumis
- L'app utilise `runFullTrust` via Desktop Bridge
- L'onboarding sert de dialogue de consentement explicite

---

*Derniere mise a jour : 2026-04-04*
