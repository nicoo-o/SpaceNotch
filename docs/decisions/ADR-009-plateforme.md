# ADR-009 — Windows 11 23H2 minimum

**Statut** : accepté, **amendé le 2026-10-18** (voir « Amendement » en fin de document)
**Date** : 2026

## Contexte

Fallait-il supporter Windows 10 et 11, ou seulement Windows 11 ?

## Décision

**Windows 11 23H2 minimum** (`10.0.22631`), compilé contre le SDK `10.0.26100`. Aucun chemin de
compatibilité Windows 10.

## Justification

Windows 11 apporte la base graphique et la cohérence de rendu que le projet exploite : les matériaux
Mica et Desktop Acrylic, les coins arrondis DWM (que le projet **désactive** volontairement, mais dont
l'existence implique une pile DWM à jour), et les API modernes de composition et de notification.

Supporter Windows 10 imposerait des chemins de repli, des tests supplémentaires et des cas
particuliers pour un rendu dégradé. Le coût dépasse le bénéfice : le projet n'a pas d'utilisateur sur
Windows 10 dont dépendrait son adoption.

Le manifeste est déclaré pour 23H2. La capability `systemAIModels` avait été supprimée tant
qu'aucun modèle système n'était utilisé : une déclaration de capacité non utilisée est un écart,
même sans effet visible. Elle est revenue avec Phi Silica ([ADR-025](ADR-025-assistant-local-ou-distant.md)),
qui l'exige.

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

## Amendement — 2026-10-18 (n° 30)

Le texte d'origine écrivait « 23H2 (`10.0.26100`) » : `10.0.26100` est la version du SDK (24H2), pas
celle de 23H2 (`10.0.22631`). Ce que le dépôt déclare :

| Où | Valeur |
|---|---|
| `Directory.Build.props`, `SpaceNotch.App.csproj` | `TargetFramework` `net10.0-windows10.0.26100.0`, `TargetPlatformMinVersion` `10.0.22631.0` |
| `src/SpaceNotch.App/Package.appxmanifest` | `MinVersion` `10.0.22631.0`, `MaxVersionTested` `10.0.26226.0` |
| `packaging/identity/AppxManifest.xml` (identité installée) | `MinVersion` `10.0.22000.0`, `MaxVersionTested` `10.0.26100.0` |
| `src/SpaceNotch.App/app.manifest` | `PerMonitorV2` |

Le manifeste d'identité déclare encore 21H2 (`10.0.22000`), en dessous du reste. Effet non
vérifié : l'identité s'installerait peut-être sur 21H2 ou 22H2, où l'application n'est ni construite
ni testée. Écart noté dans la feuille de route ; il n'est pas corrigé ici, parce qu'un manifeste
d'identité changé change la variante installée et sa signature publiée.

La capability `systemAIModels` est de retour dans le manifeste d'identité depuis ADR-025.
