# Distribution Entreprises — AZERTY Global

Beaucoup d'entreprises bloquent le Microsoft Store. Deux méthodes de distribution existent actuellement (Microsoft Store + installeur EXE), mais aucune ne convient bien aux entreprises avec Store bloqué.

## Piste retenue

**MSIX signé avec Azure Trusted Signing** — permettrait une installation hors-Store avec signature de confiance.

## Blocage actuel

Azure Trusted Signing n'est pas encore disponible pour les développeurs solo en France (mars 2026).

## Contexte

Les entreprises sont un public cible important (surtout via LDLC/SILL), et le Store bloqué est un frein réel à l'adoption. Surveiller la disponibilité d'Azure Trusted Signing pour les individus en France. Quand disponible, créer un workflow de signature MSIX comme 3e canal de distribution.

---

*Dernière mise à jour : 2026-03-28*
