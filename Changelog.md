# Changelog — Application AZERTY Global

## Version 0.11.2 — 3 juin 2026

**Exercice de typographie**

- Phrase de l'exercice 4 remplacée par : `Lætitia demande « d'où vient ce chef-d'œuvre… » — elle l'approuve à 100 %.`

**Correctifs pré-publication Store**

- Mode compatibilité jeux : les combos natives utilisent désormais de vrais événements scancode (`KEYEVENTF_SCANCODE`) pour les applications qui bindent les touches physiquement.
- Désactivation anti-cheat : la notification de sécurité reste affichée même si les notifications standard sont désactivées.
- Journaux locaux : anonymisation du nom de process dans le log debug compat et suppression du chemin complet `learning-tweaks.json`.

## Version 0.11.1 — 28 mai 2026

**Correctif dispositions système non-AZERTY**

- Correction du pass-through clavier quand la fenêtre cible utilise une disposition système non-AZERTY, notamment QWERTY US.
- Les touches physiques restent pilotées par scancode : `D01` produit bien `a` au lieu de laisser passer `q`, et `E01` produit bien `&` au lieu de laisser passer `1`.
- Le pass-through reste conservé quand la disposition de la fenêtre cible produit déjà le bon caractère.
- Correction associée pour les raccourcis `Ctrl+touche` : `Ctrl+D01` sous QWERTY envoie bien `Ctrl+A`, pas `Ctrl+Q`.

## Version 0.11.0 — 20 mai 2026

**Synchronisation avec la disposition actuelle**

- Ressources embarquées synchronisées avec la disposition actuelle : `AZERTY Global 2026.json` et `character-index.json`.
- Mise à jour des raccourcis : `#` en alternative développeur sur AltGr + :, `^` sur AltGr + I, backtick vif sur AltGr + L, Latin étendu sur AltGr + 6, tiret insécable sur Maj + AltGr + 6.
- Espaces insécables alignées : espace fine insécable sur AltGr + Espace, espace insécable sur Maj + AltGr + Espace.
- Recherche de caractères mise à jour avec 1032 entrées d'index, dont 1003 caractères Unicode et 29 touches mortes.
- Ajout d'un script durable de synchronisation des ressources depuis le site, avec validation des raccourcis critiques.

## Version 0.10.0 — 8 mai 2026

**Audit sécurité indépendant**

- Hardening binaire : Control Flow Guard (CFG) activé sur les binaires AOT x64 et ARM64. Build déterministe explicite.
- Robustesse renforcée : gestion d'erreurs défensive sur le hook clavier (try/catch sur le callback bas niveau) et les allocations mémoire natives (try/finally sur 5 sites `Marshal.AllocHGlobal`).
- Privacy : logs locaux désormais limités (pas de stack traces complètes ni de paths utilisateur dans `error.log`) et noms de process anonymisés via HMAC-SHA256 dans les events critiques de compatibilité.
- Isolation hook : marker d'injection randomisé au démarrage (au lieu d'une valeur fixe), mutex d'instance unique préfixé `Local\` + suffixé SID utilisateur (anti-squat).
- CI GitHub Actions ajoutée (build reproductible x64+ARM64 + tests + Pack-MSIX + Verify-Release + BinSkim hardening + attestation SLSA L1).
- Hygiène repo : suppression d'un fichier doublon `OnboardingWindow (# Name clash...)` issu d'un conflit de sync Proton Drive.

Aucun changement fonctionnel utilisateur visible. Audit complet : `Archives/audits/2026-05/reports/AUDIT-SECURITY-v0.10.0.md`.

## Version 0.9.8 — 5 mai 2026

**Menu tray — entrée « Exercices »**

- Nouvelle entrée `Exercices` dans le menu de la zone de notification (entre `Rechercher un caractère` et le séparateur). Ouvre le `LearningModule` en mode replay : démarre toujours à l'exercice 1, parcourt les 4 exercices normaux puis la page de choix avant les 2 exercices bonus, comme l'onboarding initial.
- Mode replay sans side-effect sur la progression : la valeur `learningMaxStepCompleted` du fichier de configuration n'est jamais modifiée par cette voie. La progression sauvegardée reste celle du premier passage onboarding.
- Garde-fou : si la fenêtre d'onboarding est en cours d'utilisation OU si une autre instance de `LearningModule` est déjà ouverte, le clic est ignoré (no-op) — pas de doublon d'instance.

**Notification toggle — suppression du doublon**

- Suppression de la balloon Windows (zone de notification, en bas à droite) lors des bascules `Ctrl+Maj+Verr.Maj` : elle faisait doublon avec la mini-fenêtre flottante en haut à droite (`ToggleNotification`, ajoutée en v0.9.7) qui était déjà plus visible et plus lisible. Reste désormais seule la fenêtre flottante.
- La balloon de démarrage de l'app (rappel du raccourci `Ctrl+Maj+Verr.Maj` au lancement) est conservée — elle a un rôle pédagogique différent.

## Version 0.9.7 — Avril-Mai 2026

**Caps Lock — refonte complète (smoke test in-game, mai 2026)**

- **Désynchronisation entre l'état Caps Lock interne et Windows** corrigée : la frappe `Caps Lock + lettre` puis lancement d'un exercice produisait des majuscules permanentes à cause d'un état désynchros. `_capsLockState` est désormais resynchronisé avec `GetKeyState(0x14)` à chaque frappe non-modifier (`KeyMapper.ProcessKey`), et `RequestCapsLockOff` vérifie l'état Windows réel avant de toggler.
- **Modificateurs Shift/Ctrl/Alt résiduels** corrigés : si l'application est lancée pendant qu'un jeu tient des touches (ex. Maj pour sprinter), le keydown initial était manqué et des frappes ultérieures sortaient en majuscule. `SyncState` appelle désormais `CleanupStaleModifiers()` ; `SyncState` lui-même est invoqué au démarrage de l'app et au retour de focus du LearningModule.
- **Suppression du toggle Caps Lock physique** dans `BuildVkComboInputs` (mode NativeCombo) : auparavant chaque frappe en Caps Lock ON injectait `VK_CAPITAL down/up` deux fois, ce qui spammait la notification Windows « Verr. Maj. activé/désactivé » dans Minecraft, Trackmania, etc. Désormais on inverse logiquement `needsShift` (Caps Lock + Shift s'annulent côté Windows) — sans toucher physiquement à Caps Lock.
- **Détection dynamique « Caps Lock affecte ce VK ? »** via `ToUnicodeEx` (avec/sans état Caps Lock simulé, flags=1 sans consommer le dead-key state). Cache par `(vk, hkl)`. Couvre exactement les touches affectées (lettres A-Z, rangée numérique, ponctuation OEM en AZERTY) et exclut celles qui ne le sont pas (VK_OEM_102 `<>`). Bug `<` qui devenait `>` en Caps Lock corrigé.

**Touches mortes natives — fallback Alt+code**

- Les caractères qui sont eux-mêmes des dead keys sur le layout natif (`^` `¨` `~` `` ` `` en AZERTY traditionnel) faisaient entrer Windows en mode dead-key lors de l'injection en mode NativeCombo, ce qui consommait le caractère sans l'afficher (workaround Tab nécessaire dans Trackmania). `BuildNativeComboInputs` détecte désormais via `IsDeadKeyOnLayout` (`ToUnicodeEx` renvoie -1) et fait fallback automatique sur Alt+code, qui bypass complètement le système dead-key Windows.

**Compatibilité — détection foreground**

- `ForegroundMonitor.Recompute` ignore désormais les transitions vers `explorer.exe`, `SearchHost.exe`, `StartMenuExperienceHost.exe`, `ShellExperienceHost.exe`, `TextInputHost.exe` — effets de bord du clic sur l'icône tray ou de la touche Windows. Sans ce filtre, le sous-menu « Compatibilité » affichait `SearchHost.exe` ou `explorer.exe` au lieu du jeu réel.
- Le PID de notre propre application n'est plus ignoré : quand la fenêtre du LearningModule prend le focus, le mode redevient correctement `Default` (au lieu d'hériter d'un `NativeCombo` parasite d'un jeu antérieur), ce qui empêche l'AltGr+N (`~`) d'être consommé en mode dead-key dans nos exercices.
- Sous-menu « Compatibilité » dans le menu tray filtré quand le foreground est notre propre app (plus d'item « Compatibilité — AZERTY Global.exe »).

**Retour visuel pendant les jeux fullscreen — `ToggleNotification`**

- Nouvelle mini-fenêtre TOPMOST en haut à droite (240×56 px logiques, opacité ~94 %, auto-fermeture 2 s) qui affiche « AZERTY Global activé » (vert) ou « AZERTY Global désactivé » (gris) à chaque toggle via `Ctrl+Maj+Verr.Maj`. Permet de voir l'état du remapping en borderless windowed quand l'icône de la zone de notification est cachée par le jeu. Angle mort accepté en exclusive fullscreen.
- **Garde anti-cheat** : la fenêtre TOPMOST ne s'affiche **jamais** quand un process protégé par anti-cheat kernel-level est au foreground (Valorant, Fortnite, CoD, etc.). Évite tout risque qu'un anti-cheat scanne l'overlay et le flagge comme cheat tiers.

**LearningModule — finitions**

- Le module force `RequestCapsLockOff()` à l'ouverture : tous les exercices commencent désormais avec Caps Lock désactivé, peu importe l'état hérité du contexte extérieur. Empêche que l'utilisateur arrive sur l'exo 1 « Activez Verr. Maj. » avec Verr. Maj. déjà actif.
- Suffixe « (Bonus) » en doré-orangé (`#E29400`) à la suite du titre des exercices facultatifs (ex5, ex6) — remplace l'ancienne pill orange peu lisible. La couleur dorée évite la confusion avec le vert utilisé pour la progression.
- Page de choix fin d'exercice : navigation par flèches (`←` / `↑` = Recommencer ; `→` / `↓` = Suivant ; `Esc` = Quitter).
- Écran final « Bravo ! » : flèches `→` / `↓` + `Esc` ferment la fenêtre (équivalent au bouton Terminer). Bouton Terminer repositionné — aligné à droite (largeur 140 px), juste au-dessus du clavier, pour ne plus chevaucher le sous-titre « Vous maîtrisez les bases d'AZERTY Global. ».
- Tooltip de la touche Backspace désactivée passé sur 2 lignes pour la lisibilité.
- Masquage des caractères secondaires peu utilisés sur le clavier virtuel des exercices (point en chef, point souscrit, double aigu, double grave, corne, crochet, brève, brève inversée, barre oblique/horizontale, macron, latin étendu, cédille, virgule souscrite, alphabet phonétique, rond en chef, symboles scientifiques, caron, ogonek, alphabet cyrillique, symboles divers `→`, guillemet-apostrophe ouvrant, soft hyphen, arobase alternatif sur AltGr+E10, guillemets doubles `“ ”`). Ces caractères restent visibles dans le **tooltip de chaque touche** au survol.
- Tooltips uniformisés : tous les noms de caractères et de touches mortes sont en MAJUSCULES (cohérence visuelle). Format des dead keys : `TOUCHE MORTE + nom` (ex. « TOUCHE MORTE SYMBOLES DIVERS » au lieu de « FLÈCHE VERS LA DROITE »). Override pour `’` qui s'affiche désormais comme « APOSTROPHE TYPOGRAPHIQUE » (au lieu du nom Unicode officiel « GUILLEMET-APOSTROPHE FERMANT »).

**Wizard d'accueil — finitions UX**

- Étape 1 : libellé du bandeau passé de « Version bêta » à « **Phase de tests** » + point ajouté après « donnez votre avis ». Espacement entre le titre « Votre clavier est maintenant amélioré » et la barre de progression réduit (24→12 px).
- Étape 1 : la phrase rassurante « Cette application améliore votre clavier. Aucune frappe n'est enregistrée ni transmise. » est désormais sur une seule ligne avec une fonte dédiée à scaling proportionnel calibré (`-(int)Math.Round(17 * dpiScale / 1.75)` — 10 px à 100 % DPI, 17 px à 175 % DPI).
- Hauteur de la fenêtre wizard réduite de 810 → 770 px (compacité).
- Étape 3 : checkboxes « Lancer au démarrage de Windows » et « Ne plus afficher cet écran au démarrage » désormais cochées par défaut à chaque ouverture (recommandation). Lien « Donner son avis sur la bêta » renommé « Donner son avis sur AZERTY Global » (cohérence avec le menu tray).
- Navigation par flèches sur les 3 étapes (sous-classe `ButtonArrowSubclassProc` sur les boutons Next/Prev/Try) : `↓` / `→` = bouton principal de l'étape (Essayer maintenant / Suivant / C'est parti) ; `↑` / `←` = bouton Précédent (étapes 2 et 3) ; `Esc` = fermer.

**AboutWindow — refonte**

- Hauteur réduite de 320 → 230 px, largeur passée de 420 → 500 px pour faire tenir les liens.
- Description simplifiée : « Disposition clavier améliorée pour les francophones. »
- Ligne « Édité par l'AMCF » + ligne secondaire fusionnées en une seule : « Édité par l'**Association pour la Modernisation du Clavier Français (AMCF)** » avec le nom complet en lien cliquable vers la page HelloAsso de l'association.
- Lien « azerty.global » renommé « Site web ». Lien « Licence EUPL 1.2 » enrichi en « Licence EUPL 1.2 (open source) » — la mention « Licence : EUPL 1.2 (open source) » au-dessus est supprimée (redondance).

**LayoutConflictWindow — wording**

- Mention de la suppression de la disposition système reformulée pour être indépendante de la langue de Windows : « Enlève AZERTY Global de la liste des dispositions chargées dans les options de langue (Paramètres Windows → Heure et langue → Langue → Options de la langue concernée). » (au lieu de « désinstalle le pack « Français — AZERTY Global » »).

**Tutoiement / vouvoiement — stratégie**

- L'OnboardingWindow et le LearningModule (sas d'accueil) **vouvoient** l'utilisateur, en cohérence avec le site web public.
- Toutes les autres fenêtres et messages (LayoutConflictWindow, SettingsWindow, TrayApplication notifications, AutoStart erreurs) **tutoient** l'utilisateur.

**Settings — libellés**

- « Notifications (activé / désactivé) » → « Notifications ».
- « Lancer au démarrage de Windows (recommandé) » → « Lancer au démarrage de Windows ».
- « Afficher la fenêtre de bienvenue au démarrage » → « Fenêtre de bienvenue au démarrage ».
- MessageBox de confirmation « Réinitialiser raccourcis » réécrite sur 2 lignes pour rendre la boîte plus compacte.

**Menu tray — corrections**

- L'item « Donner son avis sur AZERTY Global » pointe désormais vers `https://azerty.global/beta` (au lieu de `/feedback`) tant que la phase de retours est en cours.

**Tests automatisés**

- Le test `BuildVkComboInputs_CapsLockActive_AndShiftCombo_TogglesCapsAround` qui validait l'ancien comportement (toggle Caps Lock physique) a été remplacé par `BuildVkComboInputs_CapsLockActive_NoPhysicalToggle` qui valide l'absence d'event `VK_CAPITAL` injecté.
- 77/77 tests xUnit passent. Build Release AOT x64 + ARM64 : 0 warning, 0 error.

---

**Wizard d'accueil — affichage conditionnel et choix utilisateur**

- Le wizard d'accueil ne s'affiche plus systématiquement à chaque démarrage : il reste affiché tant que les 3 premiers exercices n'ont pas tous été complétés. Une fois ces 3 exercices validés, l'application démarre directement en arrière-plan avec une bulle de notification discrète.
- Nouvelle option dans Paramètres : « Afficher la fenêtre de bienvenue au démarrage » — permet à l'utilisateur de désactiver manuellement le wizard à tout moment, même si les exercices ne sont pas terminés.
- État de progression persisté dans la configuration utilisateur (`learningMaxStepCompleted`) pour traverser les redémarrages.
- L'étape 1 du wizard reste au premier plan (`topmost`) pour maximiser la visibilité des 5 améliorations. Les étapes 2 et 3 ne le sont plus, pour permettre de consulter les ressources mentionnées (guide, Discord, bêta) en parallèle d'un navigateur.

**Menu de la zone de notification — réorganisation**

- Nouvelle entrée « À propos » en dessous des Paramètres : ouvre une mini-fenêtre custom avec version, licence EUPL 1.2, mention de l'AMCF, et 3 liens cliquables (site, code source GitHub, licence).
- Sous-menu « Compatibilité « process » » déplacé sous « À propos ». Le séparateur qui le suivait n'apparaît plus quand aucun process foreground n'est détecté (plus de séparateur orphelin).
- Libellé « Signaler un bug » enrichi en « Signaler un bug (version + OS) » pour clarifier les données techniques transmises au support.

**Conflit avec disposition système AZERTY Global — popup éclairée**

- Si l'application détecte qu'une disposition système AZERTY Global est déjà installée, elle ouvre désormais une mini-fenêtre custom (au lieu d'une `MessageBox` standard) qui présente le trade-off entre les deux solutions :
  1. Garder la disposition système (nécessaire pour taper avec AZERTY Global avant le login Windows : mot de passe, écran de verrouillage, UAC, BitLocker)
  2. Garder l'application (clavier virtuel et recherche de caractère, plus user-friendly post-login)
- Si la fenêtre de bienvenue devait s'afficher au démarrage, son ouverture est différée jusqu'à ce que l'utilisateur ait choisi « Garder l'application ». Évite que le wizard recouvre la mini-fenêtre d'explication.

**Refonte du mini-onboarding**

- Bouton « Essayer maintenant » : largeur dimensionnée dynamiquement selon le texte (corrige la troncature visible « ssayer maintenar »).
- Instruction des exercices : passage à un gris foncé `#404040` (contraste ~9:1) pour une lisibilité nette sur fond clair.
- Mention de confidentialité ajoutée à l'étape 1 : « Cette application améliore votre clavier. Aucune frappe n'est enregistrée ni transmise. »
- Comportement « Essayer maintenant » revu : on reste sur l'étape 1 ; le bouton se transforme en « Suivant » à la sortie des exercices (au lieu d'avancer silencieusement à l'étape 2).
- Fenêtre wizard agrandie de 750 → 810 px de hauteur pour absorber la nouvelle mention.

**Module d'apprentissage**

- Renommage « Étape 1/6 » → « Exercice 1/6 » pour distinguer du wizard d'accueil 3 étapes.
- Exercices 5 et 6 (facultatifs) : pill « Bonus » à côté du titre pour signaler qu'ils sont skippables.
- Renommage du bouton « Passer cette étape » → « Passer cet exercice ».
- Page de fin enrichie : titre « Bravo ! » en grande police orange + sous-titre « Vous maîtrisez les bases d'AZERTY Global. ».
- Légende du clavier en bas : « Maj. — Verr. Maj. — AltGr — Touche morte » avec leurs codes couleur respectifs.
- Caractères AltGr du clavier des exercices désormais en bleu accent (cohérence avec le testeur du site web).
- Reformulation des instructions des exercices 1 et 2 pour être plus explicites :
  - Exercice 1 : « Activez Verr. Maj. puis tapez sur la lettre é »
  - Exercice 2 : « Gardez le Verrouillage Majuscule activé pour taper cette phrase »
- La touche Backspace est désormais grisée pendant les exercices avec un tooltip dédié au survol : « Désactivé pendant les exercices — continue de taper, l'erreur se corrige toute seule ». Évite la confusion quand l'utilisateur appuie par réflexe sur Retour arrière après une erreur.

**Wizard d'accueil — étape 3 simplifiée**

- Retrait du lien « S'entraîner avec les leçons de frappe » (doublon avec les exercices intégrés).
- Retrait de la note d'avertissement « Le testeur en ligne nécessite de désactiver temporairement l'application. » (jugée disruptive).
- Conservation des liens Guide, Bêta et Discord.

**Bugs corrigés**

- Couleur des touches mortes (`CLR_DK_RESULT`) : corrigée d'une valeur hex à 9 chiffres invalide vers le vert intentionné `#339900`.

**Compatibilité jeux**

Refonte majeure de la couche d'injection pour résoudre les problèmes de compatibilité avec les jeux qui filtrent les frappes synthétiques (Minecraft Java, mods comme JEI, jeux Unity, SDL, GLFW…).

- **Saut impossible en sprint** (Maj+Z+Espace dans Minecraft) : la barre d'espace est désormais en pass-through même quand Shift est maintenu, puisque sa sortie ne dépend pas du Shift. Le jeu reçoit un vrai `WM_KEYDOWN VK_SPACE`.
- **« Touche fantôme »** après usage du raccourci `Ctrl+Maj+Verr.Maj` pendant qu'une touche était maintenue (personnage continuant à avancer ou aller à gauche dans les jeux) : un keyup synthétique est désormais émis pour chaque touche en pass-through avant de purger l'état interne, évitant que l'app cible ne perçoive la touche comme toujours enfoncée.
- **`Ctrl + lettre` dans les jeux qui bindent par position physique** (Minecraft via GLFW, SDL, DirectInput) : si la touche physique correspond déjà au bon VK natif, on laisse passer la frappe d'origine au lieu d'injecter une touche synthétique. Corrige `Ctrl+A` (drop d'item dans l'inventaire Minecraft).
- **Combo native pour les caractères injectés en jeu** : quand un jeu compatible (Minecraft, Trackmania, jeux Unity, SDL, etc.) est au premier plan, les caractères AZERTY Global (`@`, `#`, accents, guillemets typo) sont désormais injectés via une combinaison de touches natives du clavier sous-jacent. Marche dans les chats et tous les champs de saisie modés (notamment la recherche d'items JEI dans Minecraft, qui était cassée auparavant).
- **Alt+code automatique pour les caractères inaccessibles** sur le layout natif (`É`, `«»`, `–`, `œ`, etc.) : injection via la séquence `Alt+0XXX` du Numpad pour permettre la frappe en jeu sans perdre la fonctionnalité Smart Caps Lock ni les guillemets typographiques.
- **Désactivation automatique sur jeux protégés par anti-cheat kernel-level** (Valorant, League of Legends, Fortnite, Apex Legends, Call of Duty, R6 Siege, PUBG, Tarkov, Genshin Impact, Honkai Star Rail, Roblox, FACEIT, Battlefield 2042, The Finals, Counter-Strike 2, Marvel Rivals, Helldivers 2, etc.) : AZERTY Global se met automatiquement en pause à l'ouverture du jeu pour éviter tout risque de bannissement, avec une bulle d'information ; réactivation automatique à la fermeture.
- **Le raccourci `Ctrl+Maj+Verr.Maj` est désormais refusé pendant la désactivation auto anti-cheat** : tant qu'un jeu protégé est au premier plan, l'utilisateur ne peut pas réactiver AZERTY Global, même via raccourci. Une bulle de sécurité explique le refus. Évite les bannissements accidentels.
- **Sous-menu de compatibilité par application** dans le menu de la zone de notification : permet de forcer la compatibilité jeu, ou la désactivation totale, pour une application précise détectée au premier plan. La désactivation utilisateur sur un process protégé par anti-cheat est refusée par sécurité.
- **Fonctionnement correct en RDP, VPN et applications qui simulent AltGr via `Ctrl+Alt`** : la séquence Alt+code utilisée pour injecter les caractères inaccessibles (`É`, `«»`, `–`, `œ`…) relâche désormais correctement les modificateurs physiques tenus dans ce mode. Auparavant l'application cible recevait `Ctrl+Alt+0XXX` au lieu de `Alt+0XXX`, ce qui pouvait déclencher des raccourcis au lieu de produire le caractère.

**Architecture interne**

- Refonte modulaire : nouvelle couche `IWin32Api` permettant l'injection de dépendances et facilitant la maintenance future.
- Suite de tests automatisés (~70 tests xUnit) couvrant la liste anti-cheat, la persistance des overrides utilisateur, la détection de mode, et la construction des séquences d'injection (combo native, Alt+code, fallback Unicode).
- Rotation automatique du journal d'erreurs à 5 Mo (au lieu de la troncature à 1 Mo précédente).

**Outils internes (build DEBUG uniquement)**

- Nouvelle entrée dans le menu tray « 🛠 Réinitialiser onboarding » pour faciliter les tests visuels du parcours.

## Version 0.9.6 — Avril 2026

**Consolidation**
- Audit complet de l'architecture et du code (16 fichiers, ~7 500 lignes).
- Aucun bug bloquant identifié — version de consolidation sans changement fonctionnel.

## Version 0.9.5 — Avril 2026

**Fiabilité de la publication Store**
- Alignement des métadonnées de release sur `0.9.5` côté application et `0.9.5.0` côté package Store.
- Ajout d'un contrôle de cohérence pour la chaîne `publish -> msix -> documentation`.

**Lancement automatique plus fiable**
- Les fenêtres de paramètres et d'accueil relisent désormais l'état réel de Windows au lieu d'un cache local.
- Les messages d'erreur distinguent correctement le mode MSIX du mode non packagé.

**Recherche de caractère**
- La copie dans le presse-papiers ne signale plus un succès sans validation réelle de `SetClipboardData`.
- La fenêtre gère maintenant les changements de DPI en recalculant polices, layout et taille.

**Robustesse interne**
- Le hook clavier peut être réinstallé sans fenêtre de coupure visible.
- Les composants auxiliaires (`CharacterSearch`, `VirtualKeyboard`) n'empêchent plus le remapping de démarrer s'ils échouent isolément.
- Nettoyage des `JsonDocument` temporaires et protection du log fatal contre les erreurs d'écriture.

## Version 0.9 — Mars 2026

**Démarrage automatique avec Windows**
- AZERTY Global peut maintenant se lancer automatiquement au démarrage de Windows, sans droits administrateur et sans modifier le registre.

**Raccourcis clavier personnalisables**
- Les raccourcis pour ouvrir le clavier virtuel et la recherche de caractère sont désormais configurables pour éviter les conflits avec vos autres applications.

**Meilleure compatibilité**
- Correction d'un problème où certaines touches mortes de l'AZERTY traditionnel pouvaient interférer avec la saisie.
- Les touches de modification (Maj, AltGr) ne restent plus « bloquées » dans de rares cas.

**Préparation Microsoft Store**
- AZERTY Global sera bientôt disponible sur le Microsoft Store pour une installation encore plus simple.

---

## Version 0.8 — Mars 2026

**Recherche de caractère**
- Nouveau : trouvez n'importe quel caractère en tapant son nom en français (« e accent aigu », « euro », « tiret cadratin »…) ou en collant directement le caractère recherché.
- Le résultat indique clairement la combinaison de touches à utiliser (ex : AltGr + E → €).
- La recherche surligne automatiquement les touches correspondantes sur le clavier virtuel.

---

## Version 0.7 — Mars 2026

**Clavier virtuel**
- Nouveau : un clavier virtuel affiche en temps réel les caractères disponibles selon les touches enfoncées (Maj, AltGr, Verrouillage Majuscule).
- Le clavier s'adapte quand vous appuyez sur une touche morte pour montrer les caractères accentués possibles.
- Fenêtre redimensionnable, repositionnable et toujours visible si vous le souhaitez.

**Écran d'accueil**
- Au premier lancement, un écran d'accueil explique les bases : AZERTY Global est actif, l'icône est dans la barre des tâches, et un raccourci clavier ouvre le clavier virtuel.

**Menu amélioré**
- L'icône dans la barre des tâches donne accès au clavier virtuel, à la recherche de caractère et au site azerty.global.

---

## Version 0.6 — Mars 2026

**Premier exécutable autonome**
- AZERTY Global est désormais un fichier unique qui fonctionne sans installation ni dépendance.

---

## Version 0.5 — Mars 2026

**Première version**
- Prise en charge complète de la disposition AZERTY Global 2026 avec ses 8 couches de caractères.
- Verrouillage Majuscule intelligent : n'affecte que les lettres, pas les chiffres ni les symboles.
- Icône dans la barre des tâches pour activer ou quitter le programme.
- Une seule instance peut tourner à la fois.

---

*Dernière mise à jour : 2026-06-03 (v0.11.2 — correctifs pré-WACK)*
