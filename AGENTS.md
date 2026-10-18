# SpaceNotch

Une « Dynamic Island » pour Windows 11 : une notch attachée au bord de l'écran qui montre les
activités en cours (musique, notifications, téléchargements, minuteurs, agents IA…). C# sur .NET 10,
WinUI 3 / Windows App SDK, sans WebView.

Ce fichier suit le format [AGENTS.md](https://agents.md) : il est lu par tout agent de code (Codex, Cursor,
Copilot, Claude Code…) et par les contributeurs. Les réglages propres à Claude Code sont dans `CLAUDE.md`,
qui l'importe.

## Construire et tester

```bash
dotnet build SpaceNotch.sln -c Debug -p:Platform=x64      # boucle de développement
dotnet build SpaceNotch.sln -c Release -p:Platform=x64    # référence : avertissement = erreur
dotnet test SpaceNotch.sln -c Release                     # ce que lance la CI
```

- **Toujours passer `-p:Platform=x64`** à la construction (WinUI ne se construit pas en AnyCPU).
- **Ne pas utiliser `--no-restore`** sur `build`/`test`. La CI l'a fait et a échoué en NETSDK1112 :
  l'identifiant d'exécution (win-x64) est déduit de l'architecture du processus qui évalue le projet,
  et la restauration séparée ne l'a pas déduit comme la construction. Une commande qui se restaure
  elle-même ne peut pas diverger d'elle-même (voir `.github/workflows/ci.yml`).
- `TreatWarningsAsErrors` n'est actif qu'en **Release** (`Directory.Build.props`), avec les
  analyseurs `latest-recommended` : un build Debug vert ne garantit pas une CI verte.
- Les tests (`tests/SpaceNotch.Core.Tests`, xUnit) tournent **sans interface graphique** :
  `SpaceNotch.Core` ne référence aucune bibliothèque Windows. Les fonctionnalités se testent en
  construisant la classe avec un `ActivityManager` et un `EventBus` réels, sans fenêtre.
- Le SDK est épinglé par `global.json` (10.0.401). Les versions des paquets sont dans
  `Directory.Packages.props`, nulle part ailleurs.

Lancer un build local demande une identité de paquet : passer par `winapp run` avec
`src/SpaceNotch.App/Package.appxmanifest` (commande exacte dans `docs/development/building.md`).
Arguments utiles : `--settings`, `--tour` (visite filmée), `--demo`. Journal :
`%LocalAppData%\SpaceNotch\logs\spacenotch.log` (à lire en premier pour déboguer).

## Architecture

```
src/SpaceNotch.Core               état, activités, machine à états, animation, Lang — aucune dépendance Windows
src/SpaceNotch.Platform.Windows   interop Win32 (NativeMethods), média, audio, affichage, notifications
src/SpaceNotch.Features           les fonctionnalités, une par dossier de domaine (IslandFeatureBase)
src/SpaceNotch.Infrastructure     configuration (AppSettings), journalisation, greffons
src/SpaceNotch.App                fenêtres, scènes XAML, composition, réglages
tests/                            tests du cœur, greffon de test, tests du greffon d'exemple
samples/                          greffon d'exemple, écrit comme le ferait un tiers
```

- `IslandWindow` est une classe partielle découpée par responsabilité
  (`IslandWindow.*.cs`, visite filmée dans `Windows/Tour/`). Chercher dans tous les partiels avant
  d'ajouter un champ ou une méthode ; `IslandWindow.xaml.cs` dépasse 3 000 lignes.
- Une fonctionnalité publie des `IslandActivity` ; le gestionnaire d'activités décide de ce qui est
  montré (ADR-007). L'hôte rétablit le fil d'interface, pas la fonctionnalité (ADR-011).
- Machine à états de la notch : `docs/state-machine.md`, `SpaceNotch.Core/Machine/NotchMachine.cs`.
- Ajouter une fonctionnalité : les points de branchement sont nombreux (`FeatureKeys`, `AppSettings`,
  `IslandWindow`, `SettingsWindow`) et faciles à oublier.

## Conventions

- **Tout est en français** : commentaires, documentation XML, ADR, messages de commit et titres de
  PR. Les textes affichés sont bilingues via `Lang.T("français", "English")`, sans « (s) » pour les
  pluriels (accorder selon le nombre).
- Les commentaires expliquent **pourquoi** (le défaut corrigé, la mesure, la contrainte), pas ce que
  fait la ligne. Garder la densité de commentaires du code voisin.
- Aucun identifiant de fonctionnalité en clair hors de `FeatureKeys` ; aucune constante Win32 en
  clair hors de `NativeConstants`.
- Aucun timer périodique dans une fonctionnalité : événements système d'abord (ADR-004,
  `docs/performance.md`). Une fonctionnalité désactivée libère réellement ses écouteurs.
- P/Invoke : `[LibraryImport]` avec `EntryPoint` explicite quand le nom C# diffère du nom natif
  (un `NativeGetTickCount` sans `EntryPoint` a déjà fait tomber l'application).
- Une décision d'architecture s'écrit quand elle est prise (ADR), avec une entrée dans
  `docs/decisions/README.md`.

## Preuves

- Une demande sans critère d'acceptation : en fixer un, et le prouver (test, capture d'écran).
- Un test non lancé n'est jamais « réussi ». Une fluidité s'annonce avec la cadence, le trou max et les
  images longues de `RuntimeDiagnostics` (`docs/performance.md`), sinon « non mesurée ».
- Fin de tâche : ce qui a changé et pourquoi, commandes lancées avec leur résultat, vérifié à l'écran
  ou non, limites restantes.

## Instructions de PR

- Titre de PR en français.
- Avant de proposer la PR : build Release sans avertissement et `dotnet test SpaceNotch.sln -c Release`
  verts (c'est ce que lance la CI). Le détail du processus est dans `CONTRIBUTING.md`.

## Pièges connus

- `src/NotchFlow.*` et `tests/NotchFlow.*` sont des restes de l'ancien nom (NotchFlow → SpaceNotch).
  Ignorés par git, ils ne contiennent que `bin/obj` : ne pas les lire, ne pas les modifier.
- Position ou taille de la notch à l'écran : passer par la forme, pas par `_appWindow.Position/Size`.
  Accrochée en haut, la fenêtre peut être plus grande que la notch (toile, `IslandScreenBounds()`).
