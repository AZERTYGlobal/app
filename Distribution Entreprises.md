# Distribution Entreprises — AZERTY Global

Beaucoup d'entreprises bloquent le Microsoft Store. Le canal hors Store prioritaire est désormais un **MSIX signé au nom de l'AMCF via Microsoft Artifact Signing**.

## Canal retenu

**MSIX signé AMCF avec Microsoft Artifact Signing** — installation hors Store avec signature Public Trust, utilisable pour sideload / Intune / SCCM / GPO selon les politiques internes.

## Statut actuel

- Artifact Signing account AMCF : opérationnel (`amcfazerty`, West Europe, Basic).
- Validation Organization / Public : terminée le 2026-06-24.
- Signature au nom de l'AMCF : opérationnelle.
- v1.0.0 est publiée sur le Microsoft Store ; le bundle MSIX hors Store signé AMCF reste à produire comme artefact séparé.

## Contexte distribution

Les entreprises sont un public cible important (surtout via LDLC/SILL), et le Store bloqué reste un frein réel à l'adoption. Le MSIX signé AMCF devient donc le canal recommandé pour les organisations qui ne peuvent pas utiliser le Microsoft Store.

À ne pas confondre :

- **Bundle Store** : upload Partner Center, re-signé par Microsoft.
- **Bundle hors Store signé AMCF** : distribution directe / entreprise, ne pas uploader dans Partner Center.
- **Certificat local de test** : uniquement pour tests machine, jamais pour diffusion.

---

*Dernière mise à jour : 2026-06-29*
