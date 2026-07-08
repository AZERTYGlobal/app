# Publication Microsoft Store — AZERTY Global

Compte développeur Microsoft Partner Center créé. Nom réservé : **AZERTY Global**.

Version cible : 1.0.0
Version publiée Store : 1.0.0
Package Store : 1.0.0.0
Publication Store : 2026-06-29 — v1.0.0 acceptée par Microsoft et publiée
Canal hors Store signé AMCF : MSIX AMCF v1.0.0 produit, signé et vérifié le 2026-06-30, comme artefact distinct du Store

**Audit sécurité indépendant 2026-05** appliqué (9 patches mineurs) — voir `Archives/audits/2026-05/reports/AUDIT-SECURITY-v0.10.0.md`.
**Versions précédentes** : 0.9.8 (publiable, non soumise) — hashes archivés ci-dessous pour traçabilité.
**Statut 1.0.0** : bundle Store `1.0.0.0` produit et vérifié le 2026-06-28 ; WACK PASS ; soumission acceptée par Microsoft et publiée le 2026-06-29. Le MSIX hors Store signé AMCF a été produit et vérifié le 2026-06-30.
**Statut RC 0.12.0** : bundle Store produit et vérifié ; WACK PASS ; non soumis. Sert de base technique à la promotion `1.0.0`.
**Statut 0.11.2** : bundle Store produit et vérifié ; WACK PASS.
**Statut 0.11.1** : bundle Store produit et vérifié ; WACK PASS.
**Statut 0.11.0** : bundle Store produit et vérifié, WACK PASS, version publiée sur le Microsoft Store le 2026-05-26.

## État vérifié au 2026-06-28 (v1.0.0 Store)

- `dotnet restore "src/AZERTYGlobal.csproj"` : PASS.
- `dotnet test "src/AZERTYGlobal.Tests/AZERTYGlobal.Tests.csproj" --no-restore --logger "console;verbosity=normal"` : PASS (127/127).
- `dotnet publish -c Release -r win-x64 --no-restore` : PASS après ajout temporaire de `C:\Program Files (x86)\Microsoft Visual Studio\Installer` au `PATH` pour `vswhere.exe`.
- `dotnet publish -c Release -r win-arm64 --no-restore` : PASS.
- `scripts/Pack-MSIX.ps1` : PASS, bundle dual x64 + ARM64 produit.
- `scripts/Verify-Release.ps1` : PASS, hashes publish = bundle.
- WACK `Archives/wack/2026-06/wack-report-v1.0.0.xml` : `OVERALL_RESULT=PASS`, `APP_VERSION=1.0.0.0`.
- Captures Store : dimensions réelles documentées le 2026-06-28 dans `msix/Fiche Store.md`; recapture 16:9 à faire seulement si Partner Center refuse ou rend mal les ratios non standards.

### Artefacts v1.0.0

- Bundle versionné à uploader : `msix/AZERTYGlobal-1.0.0.0.msixbundle`
- Bundle stable local : `msix/AZERTYGlobal.msixbundle`
- SHA-256 bundle : `E6BC370052CDFF26F8F3C6BD2526C338A749B67A2F48BE24B175C71C672C9855`

| Architecture | SHA-256 de l'exe (publish = bundle) |
|---|---|
| x64 | `1B360D9ACB92AF4EC16FD148321DCEF116CEC4F89550C3E9863E4998316F6962` |
| arm64 | `0E03BEE46D341882160E5E7BC124BE40E85BD5B30BAC208837E14A73261F1318` |

### Résultat WACK v1.0.0 (2026-06-28)

Rapports archivés : `Archives/wack/2026-06/wack-report-v1.0.0.xml` et `Archives/wack/2026-06/wack-report-v1.0.0.htm`. `OVERALL_RESULT=PASS`.

Tous les tests obligatoires passent, dont `DPIAwarenessValidation`. Le test optionnel `Blocked executables` reste en `FAIL` pour des raisons connues et non bloquantes (`ShellExecuteW`, chaînes runtime .NET Native AOT `REg`, `cmD`, `MSBuild`) ; il est dans la section optionnelle du WACK et n'affecte pas le verdict global.

### Soumission Partner Center 1.0.0

- Package uploadé : `msix/AZERTYGlobal-1.0.0.0.msixbundle`.
- Notes de version FR/EN : section `Version 1.0.0` de `msix/Fiche Store.md`.
- Politique de confidentialité : `https://azerty.global/mentions-legales`, mise à jour le 2026-06-26 pour couvrir config locale, progression locale des leçons sans texte tapé ni caractères erronés saisis, logs, compatibilité par application, presse-papiers local et bug report.
- Notes certification Microsoft : utiliser la section `Informations communes (Partner Center)` de `msix/Fiche Store.md`, notamment hook clavier, `runFullTrust`, privacy, WACK PASS et optional `Blocked executables`.
- Résultat : soumission acceptée par Microsoft et v1.0.0 publiée le 2026-06-29.
- Suite GitHub : clone public synchronisé localement ; commit et tag `v1.0.0` à pousser après validation explicite. GitHub Release à créer si souhaité ; le MSIX hors Store signé AMCF a été produit le 2026-06-30.

### MSIX hors Store signé AMCF 1.0.0

- Signature : Microsoft Artifact Signing au nom de l'AMCF, effectuée le 2026-06-30.
- Objectif : distribution directe / entreprise lorsque le Microsoft Store est bloqué.
- Artefact : bundle `1.0.0.0` reconstruit avec l'éditeur AMCF puis signé, distinct du bundle destiné au Partner Center.
- Signature vérifiée (chaîne Public Trust, éditeur AMCF, horodatage Microsoft) ; disponible au téléchargement depuis le site officiel.
- Smoke test d'installation sur machine propre à confirmer avant diffusion large.
- Ne pas uploader cet artefact hors Store dans Partner Center sans validation explicite.

## État vérifié au 2026-06-26 (RC v0.12.0 Store)

- `dotnet test "src/AZERTYGlobal.Tests/AZERTYGlobal.Tests.csproj" --no-restore` : PASS (121 tests)
- `dotnet restore "src/AZERTYGlobal.csproj"` : PASS après nettoyage `obj/`
- `dotnet publish -c Release -r win-x64 --no-restore` : PASS après ajout temporaire de `C:\Program Files (x86)\Microsoft Visual Studio\Installer` au `PATH` pour `vswhere.exe`
- `dotnet publish -c Release -r win-arm64 --no-restore` : PASS
- `scripts/Pack-MSIX.ps1` : PASS, bundle dual x64 + ARM64 produit
- `scripts/Verify-Release.ps1` : PASS, hashes publish = bundle
- WACK `Archives/wack/2026-06/wack-report-v0.12.0.xml` : `OVERALL_RESULT=PASS`
- Privacy publique mise à jour le 2026-06-26 et fiche Store réalignée le 2026-06-26 : config locale, progression locale des leçons, absence de texte tapé ou de caractères erronés saisis dans les fichiers de progression, logs locaux, overrides de compatibilité par application, usage local du presse-papiers et URL volontaire de bug report avec version app + build Windows.

### Artefacts v0.12.0

- Bundle versionné à uploader : `msix/AZERTYGlobal-0.12.0.0.msixbundle`
- Bundle stable local : `msix/AZERTYGlobal.msixbundle`
- SHA-256 bundle : `55CCEA2ADE02DAF0DFB7A46071B97E56A708038C3106B7CD10206C62EC5C8A5F`

| Architecture | SHA-256 de l'exe (publish = bundle) |
|---|---|
| x64 | `E6E3C5DF05A8DE36410BDC79E038C84A406075ABC3849495A97140177CFA60D8` |
| arm64 | `197A6C090D26E278EC8CFF379811D4FFE68FD033ED4F867EF42FEC7FE70C1DE2` |

### Résultat WACK v0.12.0 (2026-06-26)

Rapport archivé : `Archives/wack/2026-06/wack-report-v0.12.0.xml`. `OVERALL_RESULT=PASS`.

Tous les tests obligatoires passent, dont `DPIAwarenessValidation`. Le test optionnel `Blocked executables` reste en `FAIL` pour des raisons connues et non bloquantes (`ShellExecuteW`, chaînes runtime .NET Native AOT `REg`, `cmD`, `MSBuild`) ; il est dans la section optionnelle du WACK et n'affecte pas le verdict global.

### Préparation Partner Center 0.12.0

- Package non soumis : `msix/AZERTYGlobal-0.12.0.0.msixbundle`.
- Notes de version FR/EN : section historique `Version 0.12.0`.
- Politique de confidentialité : `https://azerty.global/mentions-legales`, mise à jour le 2026-06-26 pour couvrir config locale, progression locale des leçons sans texte tapé ni caractères erronés saisis, logs, compatibilité par application, presse-papiers local et bug report.
- Notes certification Microsoft : utiliser la section `Informations communes (Partner Center)` de `msix/Fiche Store.md`, notamment hook clavier, `runFullTrust`, privacy, WACK PASS et optional `Blocked executables`.
- Non effectué dans cette passe : upload Partner Center, soumission Store, synchronisation du clone public GitHub, tag `v0.12.0`, push.

## État vérifié au 2026-06-03 (v0.11.2)

- `dotnet test --no-restore` : PASS (96 tests)
- `dotnet publish -c Release -r win-x64 --no-restore` : PASS
- `dotnet publish -c Release -r win-arm64 --no-restore` : PASS
- `scripts/Pack-MSIX.ps1` : PASS, bundle dual x64 + ARM64 produit
- `scripts/Verify-Release.ps1` : PASS, hashes publish = bundle
- WACK `wack-report-v0.11.2.xml` : `OVERALL_RESULT=PASS`

### Artefacts v0.11.2

- Bundle versionné : `msix/AZERTYGlobal-0.11.2.0.msixbundle`
- Bundle stable : `msix/AZERTYGlobal.msixbundle`
- SHA-256 bundle : `9E20474283B3939E80C65C8D27FEA52A7FC179295F13945550B8C8619AFCFBDA`

| Architecture | SHA-256 de l'exe (publish = bundle) |
|---|---|
| x64 | `D10185EA517B3A0FF99E113364B18B0C0EB27CA2EB2F4B235800B16E35CA7343` |
| arm64 | `43F23085B61765B52C05999601E1293C0412D024D3732BA9C4AE12CF7500BE0A` |

### Résultat WACK v0.11.2 (2026-06-03)

Rapport : `wack-report-v0.11.2.xml`. `OVERALL_RESULT=PASS`.

Tous les tests obligatoires passent, dont `DPIAwarenessValidation`. Le test optionnel `Fichiers exécutables bloqués` reste en `FAIL` pour les mêmes raisons documentées en v0.11.0 (`ShellExecuteW`, chaînes runtime .NET Native AOT, mentions d'outils dans des ressources empaquetées) ; il est marqué `OPTIONAL=TRUE` et n'affecte pas le verdict global.

## État vérifié au 2026-05-28 (v0.11.1)

- `dotnet test --no-restore` : PASS (88 tests)
- `dotnet publish -c Release -r win-x64 --no-restore` : PASS avec warning NuGet réseau `NU1900`
- `dotnet publish -c Release -r win-arm64 --no-restore` : PASS avec warning NuGet réseau `NU1900`
- `scripts/Pack-MSIX.ps1` : PASS, bundle dual x64 + ARM64 produit
- `scripts/Verify-Release.ps1` : PASS, hashes publish = bundle
- WACK `wack-report-v0.11.1.xml` : `OVERALL_RESULT=PASS`

### Artefacts v0.11.1

- Bundle versionné : `AZERTYGlobal-0.11.1.0.msixbundle`
- Bundle stable : `msix/AZERTYGlobal.msixbundle`
- SHA-256 bundle : `25624851F12611269C2C4EA03531A2429089CAA1AD188FBA258187D130854611`

| Architecture | SHA-256 de l'exe (publish = bundle) |
|---|---|
| x64 | `5202ABE82C4FB1CA23302E94870B82D748B8506F8E512C2E91260493E3731733` |
| arm64 | `0CE53F0229E2E1F3F4E2DC0BABBF6AF5ABDA895D9E0AD5B0FEC90E84234615C5` |

### Résultat WACK v0.11.1 (2026-05-28)

Rapport : `wack-report-v0.11.1.xml`. `OVERALL_RESULT=PASS`.

Tous les tests obligatoires passent, dont `DPIAwarenessValidation`. Le test optionnel `Blocked executables` reste en `FAIL` pour les mêmes raisons documentées en v0.11.0 (`ShellExecuteW`, chaînes runtime .NET Native AOT, mention `powershell` dans le README) ; il est marqué `OPTIONAL=TRUE` et n'affecte pas le verdict global.

## État vérifié au 2026-05-23 (v0.11.0)

- `dotnet test --no-restore` : PASS
- `dotnet publish -c Release -r win-x64 --no-restore` : PASS
- `dotnet restore -r win-arm64 --ignore-failed-sources` : PASS avec warning NuGet réseau `NU1900`
- `dotnet publish -c Release -r win-arm64 --no-restore` : PASS
- `scripts/Pack-MSIX.ps1` : PASS, bundle dual x64 + ARM64 produit
- `scripts/Verify-Release.ps1` : PASS, hashes publish = bundle
- WACK `wack-report-v0.11.0.xml` : `OVERALL_RESULT=PASS`

### Artefacts v0.11.0

- Bundle versionné : `AZERTYGlobal-0.11.0.0.msixbundle`
- Bundle stable : `msix/AZERTYGlobal.msixbundle`
- SHA-256 bundle : `D4CADBEBD5FE8D32EDAB1A03F560D639392308ED7FEE2454E1C16DEF1C813A25`

| Architecture | SHA-256 de l'exe (publish = bundle) |
|---|---|
| x64 | `12332C74C2B9535AF684F2441991B9898BF89119EBAB6F0CA2AEF76312FF3A12` |
| arm64 | `68C9D9FABE34A71B703238741BC302E556C668FE5DCC979C1D63CF0D04042E6E` |

### Résultat WACK v0.11.0 (2026-05-23)

Rapport : `wack-report-v0.11.0.xml`. `OVERALL_RESULT=PASS`.

| Test | Résultat | Type | Justification |
|---|---|---|---|
| Fichiers exécutables bloqués | FAIL | OPTIONAL | Référence à `shell32.dll!ShellExecuteW` utilisée pour ouvrir des liens externes. Détections de chaînes `CsI`, `cdB`, `CMD`, `MSBuild` dans l'EXE et `powershell` dans `README.md`. Test optionnel donc non bloquant Store. |

Les 23 autres tests passent (sur 24 au total). `DPIAwarenessValidation` passe en v0.11.0.

## État vérifié au 2026-05-05 (v0.9.8 finale, publishable)

- `dotnet build -c Release` : 0 avertissement, 0 erreur
- 77 tests xUnit verts
- Code applicatif aligné sur `0.9.8` (Verify-Release.ps1 PASS)
- Manifest MSIX aligné sur `0.9.8`
- Fiche Store FR/EN à jour (« What's new » 0.9.8) — ARM64 listé
- Bundle dual x64 + ARM64 produit : `AZERTYGlobal-0.9.8.msixbundle` — signé avec cert local
- WACK : OVERALL_RESULT = WARNING (publishable). 1 OPTIONAL FAIL + 1 REQUIRED WARNING (faux positif AOT documenté)

### Hashes SHA-256 du bundle 0.9.8 (Verify-Release 2026-05-05)

| Architecture | SHA-256 de l'exe (publish = bundle) |
|---|---|
| x64   | `1A5CDA5ADD32D166EB986D4EA5F5C7F2DFB43685B940ADD3FA4E5AB80F860742` |
| arm64 | `5CF655B65618AF9DEF16EAE5F3C6C811A2AC1BD0BD44B02DF9CCF069B560E988` |

### Résultat WACK v0.9.8 (2026-05-05)

Rapport : `msix/wack-report-v0.9.8.xml`. OVERALL_RESULT = `WARNING` → publishable Store.

| Test | Résultat | Type | Justification |
|---|---|---|---|
| Fichiers exécutables bloqués | FAIL | OPTIONAL | Référence à `shell32.dll!ShellExecuteW` (utilisé pour ouvrir des URLs externes — site, formulaire de retour, donations). Référence à `powershell` dans le `README.md` distribué. Test optionnel donc non bloquant Store. Persistant depuis v0.9.7. |
| DPIAwarenessValidation | WARNING | REQUIRED | Faux positif sur les binaires AOT : WACK n'arrive pas à parser l'EXE pour confirmer la DPI awareness, malgré que `app.manifest` déclare correctement Per-Monitor V2. Persistant depuis v0.9.7. À mentionner dans la fiche Store si Microsoft renvoie cette warning lors de la certification. |

Les 22 autres tests passent (sur 24 au total).

### Changements v0.9.8 vs v0.9.7

- Entrée tray « Exercices » (relancer les 6 exos en mode replay, sans persister la progression)
- Suppression de la balloon Windows lors des bascules on/off (doublon avec ToggleNotification haut-droite). Balloon de démarrage rappelant le raccourci conservée.
- Onboarding : default UNCHECKED sur « Ne plus afficher » + tracking `_step3Reached` (pas d'opt-out implicite si l'utilisateur ferme via la croix avant l'étape 3)
- Onboarding : sync bidirectionnel des flags `_learningModuleAttempted/Done` avec `LearningMaxStepCompleted` (≥1 = attempted, ≥3 = done) — affiche directement « Suivant » à l'étape 1 si exos déjà faits
- IDM_ONBOARDING tray : injection des deps Mapper/Hook/AppLayout (correction du silent fail « Essayer maintenant » sans onboarding auto-déclenché)
- LearningModule : `WS_CLIPCHILDREN` (anti-flicker boutons en frappe rapide), TIMER_CAPS_RESYNC 50 ms (anti-désync visuel Verr. Maj. après RequestCapsLockOff), suppression du refresh tooltip dans OnStateChanged

## Suivi post-publication 1.0.0

- Vérifier la fiche publique Microsoft Store : textes FR/EN, captures, liens support, politique de confidentialité.
- Vérifier l'installation depuis le Store sur une machine propre : menu Démarrer, tray, hook, recherche, clavier virtuel, autostart, onboarding.
- Synchroniser le repo GitHub public et taguer `v1.0.0` si souhaité. Ne jamais pousser sans validation explicite.
- Capturer de nouveaux screenshots si la fiche publique affiche des bandes noires ou un état d'UI dépassé.
- Smoke-tester le MSIX hors Store signé AMCF sur machine propre avant diffusion large.

## Séquence de release à appliquer aux prochaines versions

1. `dotnet publish -c Release`
2. Exécuter `scripts/Pack-MSIX.ps1`
3. Vérifier le package versionné produit dans `msix/`
4. Exécuter `scripts/Verify-Release.ps1`
5. Passer le WACK
6. Soumettre dans Partner Center
7. Après publication confirmée, **synchroniser le repo GitHub** (cf. section ci-dessous)

---

## Workflow versionnage — repo GitHub `AZERTYGlobal/app`

Le code source de l'application est versionné publiquement sur [https://github.com/AZERTYGlobal/app](https://github.com/AZERTYGlobal/app). Le repo doit être mis à jour **à chaque nouvelle version publiée sur le Microsoft Store** pour préserver la traçabilité publique du code et la cohérence avec le binaire distribué.

### Working copy

Le clone local du repo est dans `AZERTY Global/Microsoft Store - app repo/` (en dehors de `2026/`). Il sert de zone de transit entre le développement local (`2026/Microsoft Store/`) et le push GitHub.

### Procédure à chaque release

1. **Avant la soumission Store, ou immédiatement après si la version est déjà publiée** :
   - `cd "AZERTY Global/Microsoft Store - app repo"`
   - `git pull origin main` (récupérer d'éventuels changements externes — rare en pratique)
   - Reporter les changements depuis `AZERTY Global/2026/Microsoft Store/` vers le clone :
     ```powershell
     robocopy "AZERTY Global\2026\Microsoft Store\src" "AZERTY Global\Microsoft Store - app repo\src" /E /XD bin obj /XF *.exe *.pdb
     robocopy "AZERTY Global\2026\Microsoft Store\msix" "AZERTY Global\Microsoft Store - app repo\msix" /E /XF AZERTYGlobal.msixbundle config.json wack-report-*.xml
     robocopy "AZERTY Global\2026\Microsoft Store\scripts" "AZERTY Global\Microsoft Store - app repo\scripts" /E
     ```
   - Mettre à jour `README.md` du clone si nouvelles fonctionnalités, modules ou structure
   - `git -C "AZERTY Global/Microsoft Store - app repo" status` → vérifier le diff
   - Vérifier qu'aucun fichier sensible n'est ajouté (pas de `.env`, pas de certificat, pas de clé)
   - Stager explicitement les familles de fichiers réellement synchronisées ; ne jamais stager tout le clone par réflexe
   - Créer le commit de release seulement après vérification du diff
   - Créer le tag annoté seulement après validation explicite d'Antoine

2. **Après publication Store réussie et validation explicite d'Antoine** :
   - Pousser la branche principale et le tag depuis le clone public
   - Vérifier sur GitHub que commit + tag sont visibles
   - Si la release Store a un changelog notable, créer une **GitHub Release** depuis le tag (UI ou `gh release create v0.X.Y --notes "..."`)

### Fichiers à exclure du repo

- `bin/`, `obj/`, `*.pdb`, `*.exe` : artefacts de build régénérables (déjà couverts par `.gitignore` du repo)
- `config.json` : config utilisateur locale (déjà gitignored)
- `*.msix`, `*.msixbundle` : binaires (déjà gitignored)
- `wack-report-*.xml` : rapports WACK (volumineux, régénérés à chaque WACK, pas utiles pour rebuilder)

### État post-publication 0.11.0 au 2026-05-26

- Version 0.11.0 publiée sur le Microsoft Store.
- À vérifier dans le clone public : commit et tag `v0.11.0` visibles avant de considérer la release GitHub close.

### État post-publication 1.0.0 au 2026-06-29

- Version 1.0.0 acceptée par Microsoft et publiée sur le Microsoft Store.
- Bundle soumis : `msix/AZERTYGlobal-1.0.0.0.msixbundle`.
- À vérifier dans le clone public : synchronisation du code, commit et tag `v1.0.0` avant de considérer la release GitHub close.
- Topics GitHub configurés : `azerty`, `csharp`, `dotnet`, `french`, `keyboard-layout`, `microsoft-store`, `native-aot`, `windows`
- Homepage : `https://azerty.global`

## Reste à faire post-publication

- Vérifier l'expérience d'installation depuis la fiche Store publique.
- Tester le DPI 100 / 125 / 150 / 175 % sur la version installée depuis le Store.
- Surveiller les premiers retours utilisateurs, avis Store et problèmes d'installation.
- Privacy publique 0.12.0 : point couvert le 2026-06-26 dans `mentions-legales.html` et `msix/Fiche Store.md` (progression locale des leçons sans texte tapé ni caractères erronés saisis, logs locaux, `compatibility`, presse-papiers local, URL bug report version + OS). La politique de confidentialité dédiée et le registre RGPD restent à traiter séparément.

## Objectif

Objectif initial atteint le 2026-05-26 : publier sur le Microsoft Store pour contourner SmartScreen / Smart App Control, gagner en crédibilité et disposer d'un compteur de téléchargements.

---

*Dernière mise à jour : 2026-07-06*
