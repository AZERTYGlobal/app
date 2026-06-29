# Cahier des charges — AZERTY Global pour Windows

> Application Windows .NET 8 AOT distribuée via Microsoft Store (MSIX bundle x64 + ARM64) et, à partir de la v1.0.0, via MSIX hors Store signé au nom de l'AMCF. Sans dépendance externe, sans droits administrateur pour l'application Store ou MSIX.

---

## 1. Objectifs

### Objectif principal
Permettre à n'importe quel utilisateur Windows d'utiliser AZERTY Global **immédiatement**, sans installation, sans droits admin, et sans alerte de sécurité.

### Cas d'usage cibles
- Salarié sur un PC d'entreprise verrouillé (pas d'admin)
- Étudiant sur un PC d'école/université
- Utilisateur qui veut tester avant d'installer
- Démo lors d'événements (CIC Innovation Awards, salons, conférences)

---

## 2. Exigences fonctionnelles

### 2.1 Remapping clavier complet

- [x] **48 touches** remappées selon la ressource `AZERTY Global 2026.json`, synchronisée depuis la disposition actuelle
- [x] **8 couches par touche** : base, shift, altgr, shift+altgr, caps, caps+shift, caps+altgr, caps+shift+altgr
- [x] **29 touches mortes** avec toutes leurs tables de transformation
- [x] **Touches mortes chaînées** : une touche morte suivie d'un caractère non reconnu doit produire le diacritique isolé + le caractère (fallback)
- [x] **Espace après touche morte** : produit le diacritique/symbole isolé

### 2.2 Verrouillage Majuscule Intelligent (Smart Caps Lock)

C'est la fonctionnalité signature d'AZERTY Global, elle doit être **parfaite**.

- [x] Caps Lock n'affecte **que les lettres** (a–z + é, è, ç, à, œ, æ, ù, ß)
- [x] Les chiffres, symboles et ponctuation ne sont **pas affectés** par Caps Lock
- [x] Caps + é → É, Caps + è → È, Caps + ç → Ç, Caps + à → À
- [x] Caps + Shift inverse le comportement (minuscule quand Caps actif)
- [x] Les couches AltGr respectent aussi le Smart Caps Lock (ex: Caps + AltGr + O → Œ au lieu de œ)
- [ ] Indicateur visuel de l'état Caps Lock (LED physique synchronisée si possible, ou icône tray)

### 2.3 Compatibilité

- [x] **Windows 10** (version 1809+) et **Windows 11**
- [x] Fonctionne avec tous les types de claviers physiques (ISO FR, ANSI US, etc.)
- [x] Compatible avec les raccourcis système (Ctrl+C/V/Z/X, Alt+Tab, Win+..., Alt+F4, etc.)
- [x] Compatible avec les raccourcis applicatifs (Ctrl+S, Ctrl+Shift+S, etc.)
- [ ] Ne doit **pas** interférer avec AltGr quand il est utilisé comme Ctrl+Alt par certaines applications
- [x] Fonctionne dans toutes les applications (navigateurs, éditeurs de texte, IDE, terminal, jeux*)

> *Jeux : idéalement pouvoir désactiver le remapping temporairement (voir §2.9)

### 2.4 Interface utilisateur

L'interface est entièrement en **français** (public cible = francophones).

- [x] **Icône dans la zone de notification** (system tray) avec distinction visuelle actif/inactif
- [x] **Double-clic sur l'icône** : ouvre le clavier virtuel (geste le plus naturel)
- [x] **Clic droit** sur l'icône : menu contextuel avec :
  - Activer / Désactiver le remapping
  - Clavier virtuel (voir §2.5)
  - Rechercher un caractère (voir §2.6)
  - Ouvrir le site azerty.global
  - À propos (version, licence EUPL 1.2)
  - Quitter
- [x] **Raccourci clavier** pour activer/désactiver rapidement : Ctrl+Maj+Verr.Maj
- [x] **Notification au lancement** : bulle discrète confirmant l'activation
- [x] **Notification Caps Lock** : retour visuel discret quand Caps Lock change d'état (bulle ou changement d'icône tray), car le Smart Caps Lock modifie le comportement attendu
- [x] **Retour visuel touche morte** : indication discrète quand une touche morte est active (ex: bulle "^" ou changement d'icône), même sans le clavier virtuel ouvert — sinon l'utilisateur ne sait pas si sa frappe a été "avalée"
- [x] **Fichier de config portable** : les préférences utilisateur (position clavier virtuel, always on top, etc.) sont sauvegardées dans un fichier à côté de l'exe (ex: `config.json`)
- [x] Pas de fenêtre principale imposante — discret, minimaliste

### 2.5 Première utilisation (onboarding)

L'utilisateur grand public qui double-clique sur l'exe doit comprendre immédiatement ce qui se passe et tenir le rôle d'**écran de consentement** au regard de la politique Microsoft Store 10.2.8 (apps qui modifient le comportement système).

L'onboarding actuel s'organise en deux niveaux :

#### Wizard d'accueil — 3 étapes

- [x] **Étape 1** : présentation des 5 améliorations + mention de confidentialité « Cette application améliore votre clavier. Aucune frappe n'est enregistrée ni transmise. » + bouton « Essayer maintenant ».
- [x] **Étape 2** : « Comment utiliser AZERTY Global » — 4 cards : icône tray, activation/désactivation (`Ctrl+Maj+Verr.Maj`), clavier virtuel (`Ctrl+Maj+Q`), recherche de caractère (`Ctrl+Maj+W`).
- [x] **Étape 3** : ressources & communauté — 3 liens (Guide, Donner son avis, Discord) + préférences (lancer au démarrage, ne plus afficher).
- [x] **Bouton « Essayer maintenant »** dans l'étape 1 lance les exercices interactifs (sans avancer silencieusement à l'étape 2). À la sortie, le bouton se transforme en « Suivant ».
- [x] **Esc + croix X** pour fermer.

#### Module d'exercices intégré — 6 exercices

- [x] 4 exercices obligatoires (premier É, ponctuation, e-mail, typographie française).
- [x] 2 exercices bonus (skippables) : ligne de code, mots étrangers — identifiés par une pill « Bonus » à côté du titre.
- [x] Bouton « Passer cet exercice » disponible sur les bonus.
- [x] Page de félicitations en fin de parcours : titre « Bravo ! » + sous-titre « Vous maîtrisez les bases d'AZERTY Global. » + bouton « Terminer ».
- [x] Clavier virtuel intégré : caractère principal + AltGr discret en bas-droite pour les lettres ; grille 2×2 (Maj/Maj+AltGr/Base/AltGr) pour les symboles ; AltGr en accent bleu (cohérence avec le testeur du site).
- [x] Légende footer : « Maj. — Verr. Maj. — AltGr — Touche morte » avec leurs codes couleur.

### 2.6 Clavier virtuel (visualiseur de disposition)

Fenêtre affichant la disposition AZERTY Global de manière interactive, similaire au testeur du site azerty.global.

- [x] **Affichage des 48 touches** avec tous les caractères visibles par couche
- [x] **Réactif aux modificateurs** : l'affichage change en temps réel selon les touches enfoncées :
  - État normal → caractères base
  - Shift enfoncé → caractères shift
  - AltGr enfoncé → caractères AltGr
  - Shift+AltGr → caractères Shift+AltGr
  - Caps Lock actif → caractères caps
- [x] **Réactif aux touches mortes** : quand une touche morte est active, le clavier virtuel montre les transformations possibles (ex: après `^`, afficher `â ê î ô û` sur les touches correspondantes)
- [x] **Code couleur** pour distinguer les couches (ex: blanc = base, bleu = AltGr, orange = touche morte active)
- [x] **Fenêtre redimensionnable** et repositionnable, reste au premier plan (option "always on top")
- [x] **Raccourci clavier** pour afficher/masquer rapidement (ex: Ctrl+Alt+F11)
- [x] **Pas d'interception des clics** : cliquer sur une touche du clavier virtuel ne tape pas le caractère (affichage uniquement, pas un clavier à l'écran)

### 2.7 Recherche de caractère

Permet à l'utilisateur de trouver comment taper n'importe quel caractère disponible dans AZERTY Global.

- [x] **Champ de recherche** accessible depuis le menu tray ou par raccourci clavier (ex: Ctrl+Alt+F10)
- [x] **Recherche par caractère** : coller ou taper `É` → affiche "Caps Lock + é" ou "^ + E"
- [x] **Recherche par nom** : taper "e accent aigu" ou "euro" ou "guillemet" → affiche le caractère et sa combinaison
- [x] **Résultat visuel** : montre la combinaison de touches sous forme lisible (ex: `AltGr + W → «`) et surligne la touche sur le clavier virtuel si celui-ci est ouvert
- [x] **Données issues de `character-index.json`** : réutilise le fichier existant du site qui contient déjà `unicodeNameFr`, `frenchAliases`, `methods` avec flag `recommended` — pas besoin de reconstruire ces données
- [x] **Méthode recommandée en priorité** : affiche d'abord la méthode marquée `"recommended": true` dans le JSON

### 2.8 Fonctionnalités supplémentaires (v1)

- [x] **Compensation des DK système** : gérer les conflits avec les touches mortes de l'AZERTY Windows sous-jacent (^, ¨, `, ~) — essentiel car l'utilisateur a probablement l'AZERTY traditionnel comme disposition système. Le hook doit intercepter les DK système avant qu'elles ne soient traitées par Windows, puis appliquer le comportement AZERTY Global.
- [x] **Nettoyage auto des modificateurs** : réinitialiser l'état si un modificateur reste "bloqué" après quelques secondes d'inactivité (ex: Alt enfoncé dans un alt-tab interrompu)

### 2.9 Compatibilité jeux (livré en v0.9.7)

L'application reste active dans les jeux pour permettre à l'utilisateur de continuer à taper en français correctement (chat, recherche d'items modés, langues étrangères) tout en garantissant que les frappes injectées ne cassent ni les bindings de gameplay ni les anti-cheats.

- [x] **Désactivation automatique sur les jeux protégés par anti-cheat kernel-level** (Valorant, League of Legends, Fortnite, Apex Legends, Call of Duty Warzone/Black Ops, R6 Siege, PUBG, Escape from Tarkov, Genshin Impact, Honkai Star Rail, Roblox, FACEIT, Battlefield 2042, The Finals, Delta Force, Marvel Rivals, Helldivers 2, etc.) avec bulle d'information à l'utilisateur. Réactivation automatique à la fermeture du jeu. Liste hardcodée mise à jour à chaque release. Sources : rapports Gemini et Perplexity du 2026-04-26.
- [x] **Combo native ciblée pour les jeux compatibles** : pour les jeux qui filtrent les frappes synthétiques (Minecraft Java + mods comme JEI, jeux Unity, SDL, GLFW, DirectInput…), l'application injecte les caractères via une combinaison de touches natives du clavier sous-jacent au lieu d'un VK_PACKET ignoré. Détection automatique via les modules chargés (`glfw3.dll`, `lwjgl_glfw.dll`, `SDL2.dll`, `UnityPlayer.dll`, `dinput8.dll`, `allegro-5.2.dll`, etc.).
- [x] **Alt+code pour les caractères inaccessibles sur le layout natif** (`É`, `«»`, `–`, `œ`, etc.) : injection via la séquence `Alt+0XXX` du Numpad pour préserver Smart Caps Lock et la typographie typographique en jeu.
- [x] **Override utilisateur par application** dans le menu de la zone de notification (`Auto`, `Forcer compatibilité jeu`, `Forcer désactivation`). Refus de l'override `forceOn` sur un jeu protégé par anti-cheat (sécurité utilisateur). Audit automatique au démarrage : un override `forceOn` sur un jeu nouvellement ajouté à la liste anti-cheat est supprimé avec bulle d'avertissement.
- [x] **Filet de sécurité contre les "stuck keys"** : émission de keyup synthétiques pour toutes les touches en pass-through avant tout reset interne (toggle off/on, désactivation auto). Évite que le personnage continue à avancer après réactivation manuelle.

### 2.10 Fonctionnalités v2+

- [x] Auto-start au démarrage Windows (raccourci dans le dossier Startup, sans admin)
- [ ] Détection de disposition native (éviter le double remapping)
- [ ] Profils : charger d'autres dispositions (QWERTY Français, QWERTY Globale) depuis des JSON
- [ ] Vérification de mise à jour optionnelle post-publication Store / MSIX signé AMCF
- [ ] Mise à jour de la liste anti-cheat sans recompilation (fichier JSON téléchargé périodiquement)

---

## 3. Exigences non-fonctionnelles

### 3.1 Sécurité et confiance

- [x] **Zéro droits administrateur** requis
- [ ] **Réduire les alertes SmartScreen / Smart App Control** : signature AMCF via Artifact Signing opérationnelle, réputation éditeur/fichier à construire
- [ ] **Zéro faux positif antivirus** (choix de technologie non flaggée + soumission aux éditeurs AV)
- [x] **Pas de keylogger** : l'application ne doit jamais enregistrer, stocker ou transmettre les frappes
- [x] **Pas d'accès réseau** sauf vérification de mise à jour optionnelle
- [x] **Open source** (EUPL 1.2) : le code est auditable

### 3.2 Performance

- [x] **Latence imperceptible** : < 1ms de délai ajouté par frappe
- [ ] **Empreinte mémoire** : < 20 Mo en RAM
- [ ] **Pas de CPU visible** dans le Gestionnaire des tâches en usage normal
- [ ] Démarrage rapide : < 2 secondes

### 3.3 Autonomie système

- [x] **Exécutable unique** AOT autonome (~5 Mo x64, ~5 Mo ARM64)
- [x] Pas de dépendance externe à installer (.NET 8 compilé en code natif via PublishAot)
- [x] Pas d'écriture dans le registre Windows
- [x] Configuration utilisateur localisée : `%LocalAppData%\AZERTY Global\config.json` en mode MSIX, à côté de l'exe en mode unpackaged

### 3.4 Distribution

- [x] **Microsoft Store** : MSIX bundle x64 + ARM64 (~11 Mo bundle, ~5 Mo par architecture)
- [ ] **MSIX hors Store signé AMCF** via Microsoft Artifact Signing pour les environnements sans accès Microsoft Store — à produire après la publication Store 1.0.0
- [ ] **Installeur EXE autonome classique signé AMCF** à produire si le canal EXE reste nécessaire
- [x] Le JSON `AZERTY Global 2026.json` est embarqué dans le binaire comme ressource, synchronisée depuis la disposition actuelle

---

## 4. Architecture technique — Contraintes

### Ce que doit faire l'application techniquement

1. **Installer un low-level keyboard hook** (`SetWindowsHookEx` avec `WH_KEYBOARD_LL`) pour intercepter les frappes
2. **Mapper les scancodes** vers les caractères selon la couche active (base/shift/altgr/caps)
3. **Gérer l'état** : Caps Lock on/off, touche morte active, modificateurs enfoncés
4. **Émettre les caractères** via une méthode fiable (SendInput, SendKeys, ou injection Unicode directe)
5. **Afficher une icône tray** avec menu contextuel

### Contraintes Windows connues

- Les hooks `WH_KEYBOARD_LL` fonctionnent sans admin mais ont un timeout de ~300ms imposé par Windows (LowLevelHooksTimeout) — le traitement doit être rapide
- Certaines applications en mode élevé (admin) ne reçoivent pas les hooks d'un processus non-admin → limitation connue, à documenter
- Les jeux en mode DirectInput/Raw Input peuvent bypasser les hooks → limitation connue
- UAC : si une fenêtre admin est au premier plan, le hook ne s'applique pas

---

## 5. Comparatif des technologies candidates

| Critère | AutoHotkey v2 | C# / .NET | Rust | Go |
|---------|:---:|:---:|:---:|:---:|
| **Hook clavier sans admin** | ✅ | ✅ | ✅ | ✅ |
| **Risque faux positifs AV** | ⛔ Très élevé | ✅ Faible | ✅ Faible | ✅ Faible |
| **Exe unique (single-file)** | ✅ | ✅ (.NET 8 AOT) | ✅ | ✅ |
| **Taille exe** | ~1–2 Mo | ~5–15 Mo (AOT) | ~2–5 Mo | ~5–10 Mo |
| **Runtime requis** | Aucun (compilé) | Aucun (AOT) ou .NET 8+ | Aucun | Aucun |
| **GUI / System Tray** | ✅ Natif | ✅ WinForms/WPF | ⚠️ Bibliothèques tierces | ⚠️ Bibliothèques tierces |
| **Facilité de dev** | ✅ Simple | ✅ Simple | ⚠️ Courbe d'apprentissage | ⚠️ Moins adapté GUI Windows |
| **Écosystème Windows** | Bon | Excellent (natif Microsoft) | Bon | Moyen |
| **Maintenance long terme** | ⚠️ Communauté réduite | ✅ Support Microsoft | ✅ Communauté active | ✅ Communauté active |
| **Signable (Trusted Signing)** | ✅ | ✅ | ✅ | ✅ |

### Décision : C# / .NET 8 AOT ✅

- **Exe unique autonome** (~8-12 Mo) sans aucune dépendance ni runtime
- **GUI native** Windows (WinForms) pour le system tray et le clavier virtuel
- **Faible risque AV** (binaire .NET natif, pas de bytecode interprété)
- **Écosystème Microsoft** cohérent avec Azure Trusted Signing
- **Maintenance** : C# est très répandu, facilite les contributions futures

---

## 6. Données d'entrée

La ressource d'entrée embarquée est `AZERTY Global 2026.json`, copie synchronisée depuis la disposition actuelle, qui contient :
- **48 touches** avec jusqu'à 8 couches chacune
- **29 touches mortes** avec leurs tables de transformation complètes
- Les conventions du Smart Caps Lock

L'application doit lire ce JSON au démarrage et construire ses tables de mapping en mémoire.

---

## 7. Critères de validation

### Tests minimaux avant publication

**Remapping :**
- [x] Toutes les lettres a–z produisent le bon caractère (base + shift + caps)
- [x] É, È, Ç, À fonctionnent avec Caps Lock
- [x] Les 5 touches mortes principales fonctionnent (circonflexe, tréma, aigu, grave, tilde)
- [x] Touche morte + caractère non reconnu → diacritique isolé + caractère (fallback)
- [x] Les symboles de programmation fonctionnent (AltGr + D/F/G/H/J/K → { } \ | [ ])
- [x] Les guillemets français fonctionnent (AltGr + W/X → « »)
- [x] œ et æ fonctionnent (AltGr + O/A)
- [x] Espaces insécables : fine insécable en AltGr + Espace, insécable en Maj + AltGr + Espace
- [x] Les raccourcis Ctrl+C/V/Z/X ne sont pas cassés
- [x] Compensation DK système : ^ puis e produit ê (pas ^e ou ^^e)

**Interface :**
- [x] L'application se lance et se ferme proprement
- [x] L'icône tray s'affiche et le menu contextuel fonctionne
- [x] Double-clic sur l'icône ouvre le clavier virtuel
- [x] Le clavier virtuel réagit aux modificateurs (Shift, AltGr, Caps)
- [x] Le clavier virtuel réagit aux touches mortes actives
- [x] La recherche de caractère fonctionne (par caractère et par nom)
- [x] L'onboarding s'affiche au premier lancement uniquement

**Performance :**
- [x] L'application ne consomme pas de CPU au repos
- [x] Latence de frappe imperceptible

---

*Dernière mise à jour : 2026-06-27*
