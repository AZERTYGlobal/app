# Distribution Entreprises — AZERTY Global

Beaucoup d'entreprises bloquent le Microsoft Store. Le canal hors Store prioritaire est désormais un **MSIX signé au nom de l'AMCF via Microsoft Artifact Signing**.

## Canal retenu

**MSIX signé AMCF avec Microsoft Artifact Signing** — installation hors Store avec signature Public Trust, utilisable pour sideload / Intune / SCCM / GPO selon les politiques internes.

## Statut actuel

- Signature au nom de l'AMCF via Microsoft Artifact Signing : opérationnelle depuis le 2026-06-24 (validation Organization / Public terminée).
- v1.0.0 publiée sur le Microsoft Store depuis le 2026-06-29.
- MSIX hors Store signé AMCF v1.0.0 : produit et disponible depuis le 2026-06-30, comme artefact distinct du bundle Store.
- Signature vérifiée (chaîne Public Trust, éditeur AMCF, horodatage Microsoft) ; installable en sideload ou par déploiement géré (Intune / SCCM / GPO) selon les politiques internes.

## Contexte distribution

Les entreprises sont un public cible important (surtout via LDLC/SILL), et le Store bloqué reste un frein réel à l'adoption. Le MSIX signé AMCF devient donc le canal recommandé pour les organisations qui ne peuvent pas utiliser le Microsoft Store.

À ne pas confondre :

- **Bundle Store** : upload Partner Center, re-signé par Microsoft.
- **Bundle hors Store signé AMCF** : distribution directe / entreprise, ne pas uploader dans Partner Center.
- **Certificat local de test** : uniquement pour tests machine, jamais pour diffusion.

---

*Dernière mise à jour : 2026-07-06*
