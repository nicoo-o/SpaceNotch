---
name: new-feature
description: Crée une nouvelle fonctionnalité SpaceNotch (module de src/SpaceNotch.Features) avec tous ses points de branchement — clé, classe, préférence, réglages, enregistrement dans la fenêtre — et son test, sur le modèle d'un module existant.
disable-model-invocation: true
argument-hint: <Domaine>/<Nom> — ce que la fonctionnalité montre dans la notch
---

# Nouvelle fonctionnalité SpaceNotch

Demande : $ARGUMENTS

Une fonctionnalité oubliée à un seul de ces endroits compile mais ne marche pas : pas
d'interrupteur dans les réglages, préférence jamais lue, ou classe jamais démarrée. Faire
**toutes** les étapes, dans l'ordre, puis vérifier.

## 0. Cadrer avant d'écrire

- Relire un module de référence proche et **en copier la forme** :
  - événementiel simple : `src/SpaceNotch.Features/Power/ChargeFeature.cs` ;
  - avec réglage et source de données : `src/SpaceNotch.Features/Weather/WeatherFeature.cs` ;
  - avec action utilisateur : `src/SpaceNotch.Features/Power/SystemMonitorFeature.cs`.
- La donnée vient d'un **événement système**, pas d'une scrutation : aucun timer périodique dans une
  fonctionnalité (ADR-004, `docs/performance.md`). Si une scrutation est inévitable, le dire à
  l'utilisateur avant d'écrire.
- Un accès Win32/WinRT va dans `src/SpaceNotch.Platform.Windows`, derrière un type que la
  fonctionnalité reçoit dans son constructeur (et que le test peut passer à `null` ou simuler).
- Choix de présentation qui engage l'architecture (nouvelle scène, nouvelle priorité) : proposer
  un ADR (skill `/adr`).

## 1. La clé — `src/SpaceNotch.Core/Features/FeatureKeys.cs`

`public const string <Nom> = "feature.<nom-en-kebab>";` avec un `<summary>` en français. C'est une
clé de persistance : elle ne change plus une fois publiée.

## 2. La classe — `src/SpaceNotch.Features/<Domaine>/<Nom>Feature.cs`

- `public sealed class <Nom>Feature : IslandFeatureBase`
- `public const string FeatureKey = FeatureKeys.<Nom>;` et des `ActivityId` constants.
- Constructeur `(IActivityManager activities, IEventBus events, <dépendances>, bool isEnabled = true)`
  qui appelle `base(FeatureKey, "<Nom lisible>", activities, events, isEnabled)`.
- `OnStartAsync` s'abonne, `OnStopAsync` **se désabonne** (une fonctionnalité désactivée libère
  réellement ses écouteurs).
- Publier avec `PublishActivity(new IslandActivity { ... })`, retirer avec `RemoveActivity`.
  Textes via `Lang.T("français", "English")`, sans « (s) ».
- Une méthode publique d'entrée (comme `ChargeFeature.Report`) appelable directement : c'est ce que
  le test et la visite filmée utilisent.
- `<summary>` de classe en français : ce que la notch montre, et pourquoi ce choix.

## 3. La préférence — `src/SpaceNotch.Infrastructure/Config/AppSettings.cs`

Chercher `FeatureKeys.Monitor` : il apparaît à chaque endroit à compléter.
- propriété `public bool Show<Nom> { get; set; } = <défaut>;`
- `IsFeatureEnabled` : `FeatureKeys.<Nom> => Show<Nom>,`
- `HasFeaturePreference` : `FeatureKeys.<Nom> => true,`
- `BindFeature` : `case FeatureKeys.<Nom>: Show<Nom> = enabled; ...`

## 4. Les réglages — `src/SpaceNotch.App/Windows/SettingsWindow.xaml.cs`

Ajouter la ligne `FeatureKeys.<Nom> => ("<Glyphe>", Lang.T(...titre...), Lang.T(...description...))`
à côté de celle de `FeatureKeys.Monitor`. Le glyphe doit exister dans `Views/GlyphCatalog.cs`.

## 5. L'enregistrement — `src/SpaceNotch.App/Windows/IslandWindow.xaml.cs`

- Construire la fonctionnalité avec `_settings.IsFeatureEnabled(<Nom>Feature.FeatureKey)`.
- L'ajouter à la liste passée à `IslandFeatureRegistry` (là où figurent `_chargeFeature`,
  `_monitorFeature`…). Un champ seulement si la fenêtre ou la visite doit l'appeler.

## 6. Le test — `tests/SpaceNotch.Core.Tests/<Nom>FeatureTests.cs`

Sur le modèle de `PowerFeaturesTests.cs` : `new ActivityManager()`, `new EventBus()`, dépendance
Windows à `null`, appel de la méthode d'entrée, puis assertions sur
`activities.GetActiveActivities()` (titre, scène, payload, durée, actions). Couvrir au moins :
l'apparition, la disparition, et la fonctionnalité désactivée (`isEnabled: false`) qui ne publie
rien. Noms de tests en anglais descriptif, comme le reste de la suite.

## 7. Vérifier

```bash
dotnet build SpaceNotch.sln -c Release -p:Platform=x64
dotnet test SpaceNotch.sln -c Release
```

Puis, si la notch change visuellement, ajouter un passage à la visite (`Windows/Tour/`) et
contrôler avec `/snapshot`. Terminer par un résumé des fichiers touchés, étape par étape.
