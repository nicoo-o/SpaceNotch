# Construction et développement

## Prérequis

- Windows 11 23H2 ou supérieur
- .NET SDK 10 (voir `global.json` pour la version attendue)
- Windows App SDK (restauré automatiquement via NuGet)

Le SDK peut être installé localement sans droits administrateur :

```bash
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$HOME/.dotnet:$PATH"
```

## Construire

```bash
dotnet build -c Debug     # développement
dotnet build -c Release   # référence ; les avertissements sont des erreurs
```

`Directory.Build.props` porte les réglages communs, notamment
`JsonSerializerIsReflectionEnabledByDefault=false` **pour tous les projets**. Cette déclaration est
volontaire : elle aligne l'environnement des tests sur celui de l'application, dont l'élagage
désactive la sérialisation JSON par réflexion.

## Tester

```bash
dotnet test
```

Les tests du cœur s'exécutent sans machine Windows graphique : `NotchFlow.Core` ne référence aucune
bibliothèque Windows. C'est ce qui permet à la suite de tourner en une fraction de seconde.

## Lancer

```bash
EXE="src/NotchFlow.App/bin/x64/Release/net10.0-windows10.0.26100.0/win-x64/NotchFlow.App.exe"
"$EXE"
```

L'application démarre sans console. Deux façons d'observer ce qu'elle fait :

- **menu de la zone de notification** → *Diagnostics* (état, mémoire, images rendues, temps de repos) ;
- **journal** → `%LocalAppData%\NotchFlow\logs\notchflow.log`, avec rotation à 2 MB.

## Mesurer

Le protocole complet est décrit dans [performance.md](../performance.md). Le point qui compte :
**laisser 18 secondes de stabilisation** avant toute mesure. Une mesure qui inclut le démarrage
attribue au repos le coût de l'initialisation — l'erreur est facile à commettre et donne un chiffre
pessimiste qui n'a rien à voir avec le régime établi.

## Conventions

### Commits

```
feat:      nouvelle fonctionnalité
fix:       correction
refactor:  réorganisation sans changement de comportement
perf:      performance
docs:      documentation
test:      tests
build:     outillage, dépendances
```

### Branches

`main` (stable) ← `develop` (intégration) ← `feature/*`.

### Étiquettes d'issues

`area:ui`, `area:core`, `area:media`, `area:windows`, `area:performance`, `area:accessibility`,
`area:plugins` ; `priority:p0`, `priority:p1`, `priority:p2` ; `type:bug`, `type:feature`,
`type:refactor`.

### C# : quelques règles que le projet applique

- Situation exposée publiquement → documentation XML en français, expliquant **pourquoi** la décision
  a été prise, pas ce que la ligne fait.
- Aucune constante numérique en clair : tout passe par `NativeConstants` ou une préférence.
- Aucun identifiant de fonctionnalité écrit en clair hors de `FeatureKeys`.
- Aucun timer périodique dans une fonctionnalité (voir [performance.md](../performance.md)).
- Une fonctionnalité désactivée libère réellement ses écouteurs système.

### Configuration locale

`%AppData%\NotchFlow\config.json`, écrit par sérialisation **générée à la compilation**
(`AppSettingsJsonContext`). Les énumérations sont écrites en clair (`"Dark"`, `"Auto"`) pour que le
fichier reste modifiable à la main. Les signaux `ReadFailed` / `WriteFailed` sont branchés sur le
journal : un échec de configuration se voit, il ne retombe pas silencieusement sur les valeurs par
défaut.

## Structure

```
src/NotchFlow.Core               état, activités, bus, animation, contrats — aucune dépendance Windows
src/NotchFlow.Platform.Windows   interop Win32, média, audio, affichage, notifications
src/NotchFlow.Features           les fonctionnalités concrètes
src/NotchFlow.Infrastructure     configuration, journalisation, greffons
src/NotchFlow.App                fenêtres, vues de scène, composition, réglages
tests/NotchFlow.Core.Tests       tests du cœur
tests/NotchFlow.TestPlugin       greffon de test, chargé par les tests
tests/NotchFlow.SamplePlugin.Tests  tests du greffon d'exemple
samples/                         greffon d'exemple, écrit comme le ferait un tiers
docs/                            cette documentation
```
