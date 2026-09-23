# ADR-009 — Windows 11 23H2 minimum

**Statut** : accepté
**Date** : 2026

## Contexte

Fallait-il supporter Windows 10 et 11, ou seulement Windows 11 ?

## Décision

**Windows 11 23H2 minimum** (`10.0.26100`). Aucun chemin de compatibilité Windows 10.

## Justification

Windows 11 apporte la base graphique et la cohérence de rendu que le projet exploite : les matériaux
Mica et Desktop Acrylic, les coins arrondis DWM (que le projet **désactive** volontairement, mais dont
l'existence implique une pile DWM à jour), et les API modernes de composition et de notification.

Supporter Windows 10 imposerait des chemins de repli, des tests supplémentaires et des cas
particuliers pour un rendu dégradé. Le coût dépasse le bénéfice : le projet n'a pas d'utilisateur sur
Windows 10 dont dépendrait son adoption.

Le manifeste est déclaré pour 23H2, et la capability `systemAIModels` a été supprimée : le projet
n'utilise aucun modèle système et ne doit donc pas demander la permission de le faire. Une déclaration
de capacité non utilisée est un écart, même sans effet visible.

## Conséquences

- Une seule base à valider. Les tests manuels se concentrent sur les configurations DPI 100 % à 250 %.
- Le comportement des matériaux en **mode économie d'énergie** doit être pris en compte : Windows y
  désactive Desktop Acrylic. C'est la raison pour laquelle le mode flouté passe par le pinceau de
  fond du compositeur plutôt que par Acrylic — voir
  [windows-integration.md](../windows-integration.md).
- Un retour sur la décision resterait possible : elle est isolée dans le ciblage `TargetFramework` et
  dans `SystemVisualState`.

## Alternatives écartées

- **Windows 10 + 11** : rendu dégradé sur l'une des deux bases, tests doublés.
- **Windows 11 22H2** : quelques API utilisées n'y sont pas disponibles, pour aucun bénéfice.
