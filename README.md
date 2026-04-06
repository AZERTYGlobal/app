# AZERTY Global — Application Windows

Application AZERTY Global pour Windows, disponible sur le [Microsoft Store](https://apps.microsoft.com/detail/AZERTY%20Global).

## Qu'est-ce qu'AZERTY Global ?

AZERTY Global est une disposition clavier française améliorée, alternative à l'AZERTY traditionnel de Windows (1984) et à la norme AFNOR (2019). Elle corrige les problèmes quotidiens du clavier français tout en conservant 99 % des habitudes existantes.

**Site web :** [azerty.global](https://azerty.global)

## L'application

L'application Windows permet d'utiliser AZERTY Global **sans installation système et sans droits administrateur**. Elle fonctionne en arrière-plan et intercepte les frappes clavier pour appliquer la disposition.

### Fonctionnalités

- **Remapping clavier complet** — 48 touches, 8 couches par touche, 27 touches mortes
- **Verrouillage Majuscule Intelligent** — N'affecte que les lettres : `É`, `È`, `Ç`, `À` en un appui
- **Clavier virtuel** — Visualisation interactive de la disposition
- **Recherche de caractères** — Trouvez n'importe quel caractère parmi les 1 000+ disponibles
- **Icône dans la zone de notification** — Activation/désactivation rapide
- **Aucune installation requise** — Fonctionne depuis le Microsoft Store

### Configuration requise

- Windows 10 (version 1809+) ou Windows 11
- Compatible avec tous les types de claviers physiques (ISO, ANSI, ergonomique)

## Compilation

Le projet utilise .NET 8.0 avec compilation AOT native :

```bash
dotnet publish -c Release
```

> **Note :** Le linker AOT peut échouer si le chemin contient des espaces. Si c'est le cas, copiez les sources dans un chemin sans espaces (ex : `C:\temp\agp-build`).

Le binaire compilé se trouve dans `src/bin/Release/net8.0-windows10.0.17763.0/win-x64/publish/`.

## Structure du projet

```
src/                        Code source C#
├── Program.cs              Point d'entrée
├── TrayApplication.cs      Application tray (icône, menu)
├── KeyboardHook.cs         Hook clavier bas niveau
├── KeyMapper.cs            Mapping des touches (8 couches)
├── LayoutLoader.cs         Chargement du JSON de disposition
├── CharacterSearch.cs      Recherche de caractères
├── VirtualKeyboard.cs      Clavier virtuel interactif
├── OnboardingWindow.cs     Fenêtre de première utilisation
├── SettingsWindow.cs       Fenêtre des paramètres
├── ConfigManager.cs        Gestion de la configuration
├── AutoStart.cs            Démarrage automatique
├── GdiHelpers.cs           Utilitaires GDI+ (rendu texte)
├── GdiImageLoader.cs       Chargement d'images GDI+
├── Win32.cs                Interop Win32 / P/Invoke
├── AZERTY Global 2026.json Disposition clavier (ressource embarquée)
├── character-index.json    Index de recherche (ressource embarquée)
├── favicon-azerty-global.png  Icône (ressource embarquée)
└── discord-icon.png        Icône Discord (ressource embarquée)
msix/                       Packaging Microsoft Store
├── AppxManifest.xml        Manifeste MSIX
└── Assets/                 Logos et screenshots Store
scripts/                    Scripts de build
├── Pack-MSIX.ps1           Packaging MSIX
└── Verify-Release.ps1      Vérification pré-publication
```

## Licence

[EUPL 1.2](https://eupl.eu/1.2/fr/) — European Union Public Licence

© 2017–2026 Antoine Olivier
