# Décisions d'architecture

Une décision est écrite **quand elle est prise**, avec son contexte et ses conséquences. C'est ce qui
évite de prendre une mauvaise décision une seconde fois, six mois plus tard, faute de se souvenir
pourquoi la première avait été retenue.

| Réf. | Décision | Statut |
|---|---|---|
| [ADR-001](ADR-001-langage.md) | C# plutôt que C++ | Accepté |
| [ADR-002](ADR-002-ui-framework.md) | WinUI 3 / Windows App SDK | Accepté |
| [ADR-003](ADR-003-sans-webview.md) | Aucun WebView, aucune technologie web embarquée | Accepté |
| [ADR-004](ADR-004-evenementiel.md) | Architecture événementielle, aucune scrutation | Accepté |
| [ADR-005](ADR-005-animation-ressort.md) | Animation par ressort, résolue analytiquement | Accepté |
| [ADR-006](ADR-006-deux-surfaces.md) | Deux surfaces distinctes : interactive et décorative | Accepté |
| [ADR-007](ADR-007-cycle-de-vie-activites.md) | Le cycle de vie des activités appartient au gestionnaire | Accepté |
| [ADR-008](ADR-008-dissolution.md) | Dissolution calculée par le compositeur, repli XAML | Accepté |
| [ADR-009](ADR-009-plateforme.md) | Windows 11 23H2 minimum | Accepté |
| [ADR-010](ADR-010-scene-tiers.md) | Le répertoire des scènes appartient à l'hôte | Accepté |
| [ADR-011](ADR-011-fil-interface.md) | L'hôte rétablit le fil d'interface, pas la fonctionnalité | Accepté |
| [ADR-017](ADR-017-notch-attachee.md) | Une notch attachée au bord supérieur, jamais une capsule flottante | Accepté |
| [ADR-018](ADR-018-mouvement-hypnotique.md) | Mouvement hypnotique : langage du travail en cours, rejoué par le compositeur | Accepté |
| [ADR-019](ADR-019-notch-detachable.md) | Notch détachable par glisser (goutte, ressort, aimants) et bulle pour les activités importantes | Accepté |

## Format

Chaque décision suit la même structure :

- **Contexte** — le problème, avec ses contraintes réelles.
- **Décision** — ce qui a été retenu.
- **Justification** — pourquoi, y compris ce qui a été mesuré ou vérifié.
- **Conséquences** — ce que la décision rend obligatoire, y compris ce qui devient plus difficile.
- **Alternatives écartées** — et la raison du rejet, pas seulement le nom.

Les conséquences négatives sont écrites comme les autres. Une décision qui ne présente aucun
inconvénient est une décision mal analysée.

## Où regarder d'abord

- Pour comprendre la contrainte visuelle qui structure le rendu → **ADR-017**, puis **ADR-008** et
  **ADR-006**. Le plan d'ensemble est dans [SpaceNotch 2.0](../ux/spacenotch-2.0.md).
- Pour comprendre pourquoi le code est découpé ainsi → **ADR-004**, puis **ADR-007**.
- Pour comprendre la cible et les dépendances de plateforme → **ADR-009**, puis **ADR-001** et
  **ADR-002**.
- Pour écrire un greffon → **ADR-010**, puis [plugin-api.md](../plugin-api.md) et
  [`samples/SpaceNotch.SamplePlugin.Weather`](../../samples/SpaceNotch.SamplePlugin.Weather/README.md).
