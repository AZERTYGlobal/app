# TO-DO — Application AZERTY Global (Microsoft Store)

Version actuelle : 0.12.0 — RC Store vérifiée le 2026-06-26 ; 0.11.0 publiée sur le Microsoft Store le 2026-05-26

---

## Post-publication Store 0.11.0

- [ ] Vérifier la fiche publique Microsoft Store : textes FR/EN, captures, liens support, politique de confidentialité.
- [ ] Vérifier l'installation depuis le Store sur une machine propre : menu Démarrer, tray, hook, recherche, clavier virtuel, autostart, onboarding.
- [ ] Synchroniser le repo public `AZERTYGlobal/app`, créer le tag `v0.11.0` et la GitHub Release si souhaité. Ne jamais pousser sans validation explicite.
- [ ] Suivre les premiers retours Store : installation, SmartScreen, antivirus, bugs clavier, avis utilisateurs.

---

## Findings audit sécu 2026-05 — v0.10.0 (post-fix)

✅ **Tous les findings 🟠 corrigés** en v0.10.0. Audit post-fix : [`Archives/audits/2026-05/reports/AUDIT-SECURITY-v0.10.0.md`](Archives/audits/2026-05/reports/AUDIT-SECURITY-v0.10.0.md). Baseline originale v0.9.8 : `Archives/audits/2026-05/baselines/baseline-pre-audit-v0.9.8/` — archive locale.

### 🟡 v1.1+ ou hors scope code (à traiter au fil des releases)

- [ ] **[SEV-RGPD-01..03]** Politique confidentialité dédiée, test désinstall MSIX clean-up, registre traitements AMCF.
- [ ] Audit méthodique des 21 sites `catch { }` silent swallow (auto-revue).
- [ ] CodeQL CI scan (queries `csharp/security-extended`).
- [ ] AV testing avant diffusion de l'EXE autonome hors Store (Defender, Bitdefender, Kaspersky).
- [ ] Reproductibilité binaire complète (SLSA L2 attestation provenance Sigstore).
- [ ] `SECURITY.md` au repo `AZERTYGlobal/app` (politique vulnerability disclosure 90 j).
- [ ] Pinning des actions GitHub par SHA (au lieu de tags `@v4`) dans `.github/workflows/ci.yml`.

---

## v1.0 — 15 juin 2026

### Leçons d'apprentissage (chantier principal)

Version nominale post-publication. Le Store est déjà public depuis la 0.11.0 ; la v1.0 doit apporter les finitions applicatives restantes.

- [ ] **Leçons progressives** : les 5 changements d'abord, puis les touches mortes, puis les langues étrangères.
- [ ] **Badges / réussites** : "Maître des accents", "Typographe", "Polyglotte", etc.

### Autres fonctionnalités
- [ ] Favoris et historique dans la recherche de caractères : retrouver rapidement les symboles récemment copiés ou épinglés.
- [ ] Auto-suspension pour les jeux : raccourci dédié ou détection du mode jeu/fullscreen.
- [ ] Indicateur visuel LED physique Caps Lock synchronisée (si possible).

### Robustesse
- [ ] Gestion AltGr comme Ctrl+Alt : vérifier compatibilité avec VPN, accès distant, applications qui confondent les deux.
- [ ] Réinstallation automatique du hook après déconnexion/reconnexion RDP.
- [ ] Messages d'erreur user-friendly : remplacer les exceptions brutes par des messages clairs avec lien vers support.

### Qualité
- [ ] Tests unitaires (au minimum : remapping, smart caps lock, touches mortes).
- [ ] Documenter l'architecture dans un README technique dans src/.

---

## v2.0+

### Fonctionnalités
- [ ] Profils multiples : charger d'autres dispositions (QWERTY Français, QWERTY Globale) depuis des JSON.
- [ ] Onglet Settings « Apps suspendues » : UI centralisée pour gérer les overrides par-process (forceOff / forceOn) déjà existants dans `config.json`. Vue liste avec bouton Ajouter (file picker), Retirer, et radio Auto/forceOn/forceOff par entrée. Aujourd'hui, l'override est uniquement modifiable via le sous-menu tray Compatibilité quand le process est foreground — pas découvrable et pas de vue d'ensemble.
- [ ] Vérification de mise à jour optionnelle (vérifier la dernière version sur GitHub/Store).

---

## Points à surveiller / améliorations éventuelles

- **Recherche de caractère** : l'essentiel de la navigation clavier est déjà en place. Décider plus tard s'il faut aller plus loin (Tab/Shift+Tab, focus plus explicite, raccourcis supplémentaires).
- **Compatibilité AltGr/Ctrl+Alt** : partiellement gérée, à surveiller dans VPN, RDP et applications qui confondent les deux.

---

*Dernière mise à jour : 2026-06-26*
