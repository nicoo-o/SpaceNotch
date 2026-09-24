# NotchFlow

Une Dynamic Island native pour Windows. Minimaliste, fluide, pratiquement invisible lorsqu'elle n'est
pas utile — et un véritable centre d'interactions lorsqu'on l'ouvre.

Ce n'est pas « une barre noire en haut de l'écran ». C'est un espace contextuel qui apparaît quand
quelque chose mérite votre attention.

```
════════╭──────────────────╮════════   collée au bord de l'écran, jamais flottante
        │  ◉   Spotify  ▶  │           au repos : une petite notch, presque rien
        ╰──────────────────╯

══════╭────────────────────────╮══════
      │ Spotify                │
      │     ALBUM ART          │        ouverte : une surface de travail
      │     ━━━━━━━━━          │
      │   ◀     ▶     ▶        │
      ╰────────────────────────╯
            ░░░░░░░░                sans bord marqué : le bas se dissout
         ░░░░░░░░░░░
```

Le plan de refonte est dans [SpaceNotch 2.0](docs/ux/spacenotch-2.0.md) : notch attachée
([ADR-017](docs/decisions/ADR-017-notch-attachee.md)), présentation Hidden / Compact / Preview /
Expanded, et mouvement hypnotique pour le travail en cours
([ADR-018](docs/decisions/ADR-018-mouvement-hypnotique.md)).

## Philosophie

> **Native first. Événementiel. Accéléré par le GPU. Invisible au repos.**

- **Aucun WebView, aucun Electron, aucun Tauri, aucun Python.** Une application dont le but est la
  légèreté n'embarque pas un navigateur.
- **Aucune scrutation.** Le flux est `Événement → État → Animation → Repos`, et le repos est mesuré.
- **Aucun bord sous l'Island.** Le bas se dissout dans l'écran par un masque calculé par le
  compositeur Windows, pas par un dégradé posé par-dessus.
- **Aucune capture par défaut.** Le presse-papier n'est pas observé tant que vous ne l'avez pas
  demandé.

## État

`v0.x` — socle et fonctionnalités de base en place. L'API de greffons existe et est testée, mais
reste en version zéro : une rupture est possible, elle sera documentée.

| Mesure | Résultat |
|---|---|
| CPU au repos | **0,24 à 0,31 % d'un cœur** |
| Journal pendant l'inactivité | **aucune ligne ajoutée** sur 15 s |
| Tests | **174** (cœur et greffon, sans machine graphique) |
| Construction Release | 0 erreur, **0 avertissement** |
| Chemin de dissolution | **compositeur**, confirmé par sonde à l'exécution |
| Greffon d'exemple | chargé par l'hôte réel : **1 fonctionnalité, 0 échec** |

La cible mémoire de 30–60 MB n'est **pas** atteinte : 96–98 MB mesurés. Le plancher du runtime .NET
et de WinUI 3 est réel. La mesure est publiée telle quelle — voir
[performance.md](docs/performance.md).

## Fonctionnalités

Média (pochette, titre, artiste, transport, position, teinte d'ambiance dérivée de l'album) · HUD
volume · HUD luminosité · notifications · Bluetooth · minuteur de focus · minuteur · lanceur
d'application · étagère de fichiers · presse-papier avec historique (désactivé par défaut) · pile
d'activités navigable · greffons externes.

Un **greffon d'exemple complet** — météo locale, source de données réelle, actions, tests — vit dans
[`samples/`](samples/NotchFlow.SamplePlugin.Weather/README.md). Il est écrit sans modifier
l'application et ne référence que `NotchFlow.Core` : c'est la démonstration que l'API est utilisable
par un tiers. Voir [plugin-api.md](docs/plugin-api.md).

## Technologie

| Élément | Choix |
|---|---|
| Langage | C# |
| UI | WinUI 3 / Windows App SDK |
| Rendu | Compositeur Windows (`Microsoft.UI.Composition`) |
| Interop | Win32 / P/Invoke, uniquement où nécessaire |
| Cible | Windows 11 23H2+ |
| Tests | xUnit |

## Construire

```bash
dotnet build -c Debug
dotnet test
```

```bash
EXE="src/NotchFlow.App/bin/x64/Release/net10.0-windows10.0.26100.0/win-x64/NotchFlow.App.exe"
"$EXE"
```

L'application démarre sans console. Pour l'observer : *Diagnostics* dans le menu de la zone de
notification, ou `%LocalAppData%\NotchFlow\logs\notchflow.log`.

## Documentation

- [Architecture](docs/architecture.md) — les couches et la direction des dépendances
- [Machine à états](docs/state-machine.md) — états, transitions, arbitrage
- [Système d'activités](docs/activity-system.md) — le modèle et son cycle de vie
- [Système d'animation](docs/animation-system.md) — le ressort et la dissolution
- [Performance](docs/performance.md) — les règles et les mesures
- [API de greffons](docs/plugin-api.md) — écrire une fonctionnalité
- [Intégration Windows](docs/windows-integration.md) — fenêtres, DPI, API système
- [Décisions](docs/decisions/README.md) — pourquoi chaque choix structurant a été fait
- [Développement](docs/development/building.md) — construire, tester, conventions

## Structure

```
src/NotchFlow.Core               état, activités, bus, animation, contrats
src/NotchFlow.Platform.Windows   interop Win32, média, audio, affichage, notifications
src/NotchFlow.Features           les fonctionnalités concrètes
src/NotchFlow.Infrastructure     configuration, journalisation, greffons
src/NotchFlow.App                fenêtres, vues de scène, composition, réglages
tests/                           tests du cœur, greffon de test, tests du greffon d'exemple
samples/                         greffon d'exemple, écrit comme le ferait un tiers
docs/                            documentation et décisions d'architecture
```

## Sécurité et vie privée

Aucun compte, aucune télémétrie, aucun traçage. La configuration est locale
(`%AppData%\NotchFlow\config.json`), les secrets iraient dans le gestionnaire d'identification
Windows si le besoin apparaissait.

**L'application elle-même n'effectue aucun appel réseau.** Une précision nécessaire depuis
l'existence des greffons : le greffon d'exemple, lui, interroge un service météo public — c'est le
propre d'une météo. L'application ne fait aucun appel de sa part et ne charge que les greffons que
vous déposez vous-même ; un greffon tiers fait ce que son auteur a écrit, et il est prudent de le
considérer comme du code que vous choisissez d'exécuter.

## Licence

Voir [LICENSE](LICENSE).
