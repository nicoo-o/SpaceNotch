# Plan de la session « fluidité, UX, feuille de route » — jalons 3 et 4

> **Pour les agents qui exécutent :** sous-skill requis : superpowers:executing-plans (exécution
> dans cette session). Les étapes se suivent par cases à cocher (`- [ ]`).

**But :** corriger, preuves à l'appui, le repos trop large, la copie qui encombre la notch et les
défauts de fluidité désignés par la mesure ; puis concevoir et livrer un accès aux états
compréhensible sans mode d'emploi.

**Architecture :** une branche et une PR par sujet. Ordre des PR :
1. `chore/constats-jalon-1` → `main` : la mesure `--frames` et les constats (déjà poussée) ;
2. `fix/repos-etroit` et `fix/copie-discrete`, depuis `main` ;
3. `fix/fluidite-transitions`, depuis `chore/constats-jalon-1` avec `fix/copie-discrete`
   fusionnée localement (la visite filmée n'est plus polluée par la carte du presse-papier) ; la
   PR indique ses deux dépendances ;
4. `fix/matiere-masquee` (F5, CPU), PR séparée ;
5. `feat/acces-aux-etats-*`.

Le comportement se teste en Core (xUnit, `ActivityManager` et `EventBus` réels) ; le visible se
prouve par captures DPI-aware (rafales de 16 ms) et par la mesure `--frames`.

**Pile :** C# / .NET 10, WinUI 3 (Windows App SDK 2.5), xUnit.

**Spécification :** le brief `docs/prompts/2026-10-03-fluidite-ux-feuille-de-route.md`
(branche `chore/prompt-opus`), les [constats](2026-10-03-constats.md) et la
[feuille de route](2026-10-04-feuille-de-route.md). Décisions de l'utilisateur du 2026-10-04 :
copie = signal bref puis pile ; signal 2,5 s, rafale 1 s ; réécriture < 500 ms = une entrée ;
repos = corriger le piège de l'encoche et la cohérence heure ≥ repos.

## Contraintes globales

- Tout en français (commentaires, commits, PR) ; textes affichés via `Lang.T("…", "…")`, sans « (s) ».
- `dotnet build SpaceNotch.sln -c Release -p:Platform=x64` sans avertissement ; `dotnet test SpaceNotch.sln -c Release` vert ; jamais `--no-restore`.
- Aucun timer périodique dans une fonctionnalité (ADR-004) ; une fonctionnalité désactivée libère ses écouteurs.
- Presse-papier : rien sur disque, contenu complet jamais exposé hors de `ClipboardFeature`, mots de passe jamais lus (`TryReadTextForHistory`).
- Position et taille de la notch par la forme, jamais `_appWindow.Position/Size`.
- Les écritures dans `IslandWindow*.cs` sont faites par l'agent principal, jamais par un sous-agent.
- Décisions verrouillées de `docs/ux/spacenotch-2.0.md` §15 inchangées.

## Points d'attention (non couverts par les tests unitaires)

1. Une copie pendant que la notch est **ouverte** : le signal ne doit ni fermer ni remplacer la scène (file d'attente actuelle) — test dans la tâche C3.
2. Une copie pendant une **musique** présentée : le signal recouvre puis rend la musique, sans la perdre — test dans la tâche C3.
3. Écran avec encoche réelle (encoche « Ordinaire ») : repos, heure et assoupi couvrent tous l'encoche, et l'heure n'est jamais plus étroite que le repos — test dans la tâche R2.
4. Molette au repos avec seulement des entrées « en pile » : la première s'affiche, puis le repos revient seul — test dans la tâche C2.
5. Mouvement réduit : le signal de copie et les nouvelles transitions restent lisibles sans ressort — vérifié à l'œil dans les tâches C5 et F*.

---

## Sujet 1 — Repos étroit (`fix/repos-etroit`)

Critère : au repos, avec les yeux de Pixel, la notch fait 80 × 18 DIP ; elle ne s'élargit que
pour heure + météo (150 × 30) ; avec une encoche réelle, aucune forme du repos n'est plus étroite
que le repos. Preuve : tests Core, captures avant/après (encoche Aucune, Ordinaire).

### Tâche R1 : « Personnalisée » sans largeur choisie devient « Aucune »

`CameraCutout.WidthFor` ne change pas : les Réglages l'appellent pour afficher la largeur
(`SettingsWindow.xaml.cs:322, 440`), et la changer afficherait « 0 px ». Le piège se corrige
dans `AppSettings.Sanitize`, comme l'ancienne migration Gauche/Droite (`PhaseCTests.cs:55-63`).

**Fichiers :** `src/SpaceNotch.Infrastructure/Config/AppSettings.cs` (`Sanitize`, ~l. 968-977) ;
test dans `tests/SpaceNotch.Core.Tests/PhaseCTests.cs`.

- [ ] Étape 1 : test

```csharp
[Fact]
public void Personnalisee_sans_largeur_choisie_devient_aucune()
{
    // Choisie avant la v1.16 (sans effet visible), elle élargissait la notch à
    // 200 DIP après la mise à jour : sans largeur choisie, il n'y a rien à couvrir.
    var settings = new AppSettings { CutoutMode = CameraCutoutMode.Custom, CutoutWidth = 0 };
    settings.Sanitize();
    Assert.Equal(CameraCutoutMode.None, settings.CutoutMode);

    var chosen = new AppSettings { CutoutMode = CameraCutoutMode.Custom, CutoutWidth = 240 };
    chosen.Sanitize();
    Assert.Equal(CameraCutoutMode.Custom, chosen.CutoutMode);
    Assert.Equal(240, chosen.CutoutWidth, 6);
}
```

- [ ] Étape 2 : `dotnet test tests/SpaceNotch.Core.Tests -c Debug --filter "FullyQualifiedName~PhaseCTests"` → échoue (Custom reste Custom).
- [ ] Étape 3 : dans `Sanitize`, après le bornage de `CutoutWidth`, `if (CutoutMode == CameraCutoutMode.Custom && CutoutWidth <= 0) CutoutMode = CameraCutoutMode.None;`
- [ ] Étape 4 : relancer → vert, puis toute la suite.
- [ ] Étape 5 : commit « Encoche : Personnalisée sans largeur choisie redevient Aucune ».

### Tâche R2 : l'aperçu et l'assoupi du repos ne sont jamais plus petits que le repos

**Fichiers :** créer la méthode `IslandFootprint AtLeast(IslandFootprint floor)` dans
`src/SpaceNotch.Core/Scenes/IslandFootprint.cs` ; tester dans
`tests/SpaceNotch.Core.Tests/TopAttachedGeometryTests.cs` ; appliquer dans
`IslandWindow.xaml.cs` (`ResolvePreviewFootprint`, ~l. 1782) et
`IslandWindow.RestFace.cs` (`DozingFootprint`, ~l. 97).

**Produit :** `IslandFootprint.AtLeast(IslandFootprint floor) → IslandFootprint` (maximum
dimension par dimension).

- [ ] Étape 1 : test

```csharp
[Fact]
public void Une_forme_au_moins_aussi_grande_qu_une_autre_prend_le_maximum_de_chaque_dimension()
{
    var clock = new IslandFootprint(150, 30);
    var coveredRest = CameraCutout.Cover(IslandFootprint.Idle, CameraCutout.DefaultWidth);

    // Avec une encoche réelle, l'heure ne doit pas rétrécir sous le repos.
    Assert.Equal(new IslandFootprint(200, 30), clock.AtLeast(coveredRest));
    Assert.Equal(clock, clock.AtLeast(IslandFootprint.Idle));
}
```

- [ ] Étape 2 : lancer → échoue (méthode absente).
- [ ] Étape 3 : `public IslandFootprint AtLeast(IslandFootprint floor) => new(Math.Max(Width, floor.Width), Math.Max(Height, floor.Height));`
- [ ] Étape 4 : vert ; puis dans l'App : `RestWeatherPreview() ?? …` devient `(RestWeatherPreview() ?? IslandFootprint.PreviewOf(_tier, _restFootprint)).AtLeast(_restFootprint)` ; `DozingFootprint()` renvoie `CoverCamera(…)` de sa forme. Build Release.
- [ ] Étape 5 : preuve visible — build de dev, encoche « Ordinaire » puis « Aucune » : mesurer repos, survol (heure) et assoupi avec le script de mesure DPI-aware ; repos 80 × 18 avec Aucune ; heure ≥ repos dans les deux cas.
- [ ] Étape 6 : commit « Repos : l'heure et l'assoupi couvrent l'encoche comme le repos ».

### Tâche R3 : réglage d'encoche compréhensible

**Fichiers :** `src/SpaceNotch.App/Windows/SettingsWindow.xaml` (carte déplacée de « Général »
vers « Écrans »), `SettingsWindow.xaml.cs` (`OnCutoutChanged`, ~l. 682).

- [ ] Étape 1 : déplacer la carte dans la section Écrans ; description bilingue « Seulement si l'écran de ton portable a une encoche pour la caméra. » / « Only if your laptop screen has a camera notch. »
- [ ] Étape 2 : choisir « Personnalisée » sans largeur enregistrée écrit `CutoutWidth = CameraCutout.DefaultWidth` (ce qui est affiché est ce qui s'applique).
- [ ] Étape 3 : build Release ; capture de la fenêtre de réglages ; recherche « encoche » toujours trouvée.
- [ ] Étape 4 : commit, `/code-review` (high), `winui-reviewer`, PR « Repos : la notch reste étroite, l'encoche ne s'applique que si on la choisit ».

---

## Sujet 2 — Copie discrète (`fix/copie-discrete`)

Critère : une rafale de copies ne produit qu'un signal ; 2,5 s après la dernière copie, retour
au repos ; la copie reste une entrée de la pile, atteinte par la molette, jamais présentée
d'office ; plus de carte « Presse-papier · N éléments » permanente. Preuve : tests de
fonctionnalité (ActivityManager et EventBus réels, rafale simulée avec horloge injectée) +
rafale réelle filmée sur l'app.

### Tâche C1 : politique « en pile seulement »

**Fichiers :** `src/SpaceNotch.Core/Presentation/ActivityPresentationPolicy.cs` (valeur
`Listed = 4`), `src/SpaceNotch.Core/Activities/ActivityManager.cs` (`EvaluateTop`,
`CyclePresentation`) ; tests dans un nouveau fichier `tests/SpaceNotch.Core.Tests/CopyQuietTests.cs`.

**Produit :** `ActivityPresentationPolicy.Listed` — « dans la pile, jamais présentée d'office ».

- [ ] Étape 1 : tests

```csharp
private static IslandActivity Entry(string id, ActivityPresentationPolicy? policy = null, ActivityPriority priority = ActivityPriority.Normal, DateTimeOffset? createdAt = null)
    => new() { Id = id, FeatureId = "test", SceneKey = IslandSceneCatalog.Card, Title = id, Priority = priority, Policy = policy, CreatedAt = createdAt ?? DateTimeOffset.UtcNow };

[Fact]
public void Une_entree_en_pile_seulement_n_est_jamais_presentee_d_office()
{
    var manager = new ActivityManager();
    manager.PostActivity(Entry("copie", ActivityPresentationPolicy.Listed));

    Assert.Null(manager.CurrentActivity);
    Assert.Single(manager.GetActiveActivities());
}

[Fact]
public void La_molette_au_repos_montre_la_premiere_entree_en_pile()
{
    var manager = new ActivityManager();
    manager.PostActivity(Entry("copie", ActivityPresentationPolicy.Listed));

    Assert.True(manager.CyclePresentation(1));
    Assert.Equal("copie", manager.CurrentActivity?.Id);
}

[Fact]
public void Une_activite_ordinaire_passe_devant_une_entree_en_pile()
{
    var manager = new ActivityManager();
    manager.PostActivity(Entry("copie", ActivityPresentationPolicy.Listed, ActivityPriority.High));
    manager.PostActivity(Entry("musique"));

    Assert.Equal("musique", manager.CurrentActivity?.Id);
}

[Fact]
public void Depuis_le_repos_la_molette_commence_par_la_premiere_entree()
{
    // Dates explicites : deux `UtcNow` successifs peuvent être égaux.
    var manager = new ActivityManager();
    manager.PostActivity(Entry("ancienne", ActivityPresentationPolicy.Listed, createdAt: DateTimeOffset.UnixEpoch));
    manager.PostActivity(Entry("recente", ActivityPresentationPolicy.Listed, createdAt: DateTimeOffset.UnixEpoch.AddSeconds(1)));

    manager.CyclePresentation(1);
    Assert.Equal("recente", manager.CurrentActivity?.Id);
}

[Fact]
public void Une_epingle_choisie_par_l_utilisateur_reste_devant_un_signal_ordinaire()
{
    var manager = new ActivityManager();
    manager.PostActivity(Entry("minuteur"));
    manager.PostActivity(Entry("musique"));
    manager.PinPresentation("minuteur");

    manager.PostActivity(Entry("signal", ActivityPresentationPolicy.Temporary));
    Assert.Equal("minuteur", manager.CurrentActivity?.Id);
}
```

Hors de `ActivityManager`, deux lecteurs comptent les activités et doivent ignorer les entrées
`Listed` : la bulle et son « +N » (`SplitPresentation.cs:139-153`) et les onglets
(`IslandWindow.xaml.cs:2433`) — un test Core sur `SplitPresentation.BubbleFor`.

**Limite assumée** (décision de l'utilisateur : la copie vit dans la pile à points) : quand une
musique est présentée, la molette règle le volume (`IslandWindow.xaml.cs:3225`) ; la copie se
rejoint alors par Ctrl + molette (pile du presse-papier, `ClipboardFeature.ShowStack`), comme
aujourd'hui.

- [ ] Étape 2 : lancer `--filter "FullyQualifiedName~CopyQuietTests"` → échoue (valeur absente).
- [ ] Étape 3 : ajouter `Listed` (documentation XML : pourquoi) ; `EvaluateTop` ordonne seulement les activités dont `ActivityPolicies.Resolve(a) != Listed`, sauf l'épinglée ; `CyclePresentation` parcourt toutes les activités et, depuis le repos (`_currentActivity` null), va à la première (`delta > 0`) ou à la dernière (`delta < 0`) au lieu de sauter d'un cran, et accepte une seule entrée ; `SplitPresentation` ignore les `Listed`.
- [ ] Étape 4 : vert, puis toute la suite (`ActivityManagerTests`, `TortureTests` : les règles existantes ne changent pas).
- [ ] Étape 5 : commit « Activités : une entrée peut vivre dans la pile sans être présentée d'office ».

### Tâche C2 : une entrée en pile montrée par la molette rend la main seule

**Fichiers :** `ActivityManager.cs` (bail d'épingle), `ExpireOverdue`, `GetTimeUntilNextExpiration` ; tests dans `CopyQuietTests.cs`.

**Produit :** `ActivityManager.ListedPinLease = TimeSpan.FromSeconds(8)` ; l'épingle posée par
`CyclePresentation` sur une entrée `Listed` expire après ce bail (l'hôte arme déjà un minuteur
unique sur `GetTimeUntilNextExpiration`).

- [ ] Étape 1 : test avec horloge injectée

```csharp
[Fact]
public void Une_entree_en_pile_montree_par_la_molette_rend_la_main_apres_8_s()
{
    DateTimeOffset now = DateTimeOffset.UnixEpoch;
    var manager = new ActivityManager(() => now);
    manager.PostActivity(Entry("copie", ActivityPresentationPolicy.Listed));
    manager.CyclePresentation(1);

    now += TimeSpan.FromSeconds(7);
    Assert.Equal(TimeSpan.FromSeconds(1), manager.GetTimeUntilNextExpiration(now));
    manager.ExpireOverdue(now);
    Assert.Equal("copie", manager.CurrentActivity?.Id);

    now += TimeSpan.FromSeconds(2);
    manager.ExpireOverdue(now);
    Assert.Null(manager.CurrentActivity);
    Assert.Single(manager.GetActiveActivities());
}

[Fact]
public void Le_bail_ne_ferme_pas_une_entree_que_l_utilisateur_regarde()
{
    DateTimeOffset now = DateTimeOffset.UnixEpoch;
    var manager = new ActivityManager(() => now);
    manager.PostActivity(Entry("copie", ActivityPresentationPolicy.Listed));
    manager.CyclePresentation(1);

    // `spare` : l'activité ouverte ou survolée (IslandWindow.xaml.cs:2364).
    now += TimeSpan.FromSeconds(9);
    manager.ExpireOverdue(now, spare: "copie");
    Assert.Equal("copie", manager.CurrentActivity?.Id);
}
```

- [ ] Étape 2 : rouge. Étape 3 : `_pinnedUntil` posé par `CyclePresentation` quand la cible est `Listed`, effacé par tout autre `PinPresentation` ; `ExpireOverdue` lève l'épingle échue **avant** son retour anticipé quand rien n'expire (`ActivityManager.cs:304`), sauf si l'épinglée est `spare`, et notifie le changement ; `GetTimeUntilNextExpiration` tient compte de l'échéance. Étape 4 : vert + suite. Étape 5 : commit.

### Tâche C3 : la copie devient un signal bref et une entrée en pile

**Fichiers :** `src/SpaceNotch.Features/Clipboard/ClipboardFeature.cs` ; tests dans `CopyQuietTests.cs`.

**Consomme :** `ActivityPresentationPolicy.Listed` (C1). **Produit :**
`ClipboardFeature.SignalActivityId = "feature.clipboard.copied"`, `ClipboardFeature.Capture(string text)`
(chemin commun de `OnClipboardUpdated`, appelable par les tests et la visite), paramètre
optionnel `Func<DateTimeOffset>? now` au constructeur ; constantes `SignalDuration = 2,5 s`,
`BurstWindow = 1 s`, `RewriteWindow = 500 ms`.

- [ ] Étape 1 : tests (construire la fonctionnalité avec un vrai `ActivityManager`, un vrai `EventBus`, un `ClipboardMonitor` non démarré, et une horloge injectée ; appeler `Capture`)

```csharp
[Fact]
public void Une_rafale_de_copies_ne_produit_qu_un_signal_qui_compte()
{
    (ClipboardFeature feature, ActivityManager manager, Func<TimeSpan, DateTimeOffset> advance) = Build();

    feature.Capture("un");
    advance(TimeSpan.FromMilliseconds(300));
    feature.Capture("deux");
    advance(TimeSpan.FromMilliseconds(300));
    feature.Capture("trois");

    IslandActivity signal = manager.GetActiveActivities().Single(a => a.Id == ClipboardFeature.SignalActivityId);
    Assert.Contains("3", signal.Title, StringComparison.Ordinal);
    Assert.Equal(ClipboardFeature.SignalActivityId, manager.CurrentActivity?.Id);
}

[Fact]
public void Deux_secondes_et_demie_apres_la_derniere_copie_la_notch_revient_au_repos()
{
    (ClipboardFeature feature, ActivityManager manager, Func<TimeSpan, DateTimeOffset> advance) = Build();
    feature.Capture("texte");

    DateTimeOffset later = advance(TimeSpan.FromMilliseconds(2600));
    manager.ExpireOverdue(later);

    Assert.Null(manager.CurrentActivity);
    Assert.Contains(manager.GetActiveActivities(), a => a.Id == ClipboardFeature.ActivityId);
}

[Fact]
public void Une_reecriture_dans_les_500_ms_remplace_l_entree_au_lieu_d_en_ajouter_une()
{
    (ClipboardFeature feature, ActivityManager manager, Func<TimeSpan, DateTimeOffset> advance) = Build();
    feature.Capture("https://exemple.be/?utm_source=x");
    advance(TimeSpan.FromMilliseconds(120));
    feature.Capture("https://exemple.be/");

    Assert.Equal(1, feature.EntryCount);

    // La réécriture ne compte pas comme une copie de plus dans le signal.
    IslandActivity signal = manager.GetActiveActivities().Single(a => a.Id == ClipboardFeature.SignalActivityId);
    Assert.DoesNotContain("2", signal.Title, StringComparison.Ordinal);
}

[Fact]
public void Cinq_vraies_copies_a_300_ms_restent_cinq_entrees()
{
    // Constats du 2026-10-03 : rafale de 5 phrases à 300 ms. Des textes sans
    // rapport entre eux ne sont pas des réécritures, même rapprochés.
    (ClipboardFeature feature, ActivityManager manager, Func<TimeSpan, DateTimeOffset> advance) = Build();

    foreach (string text in new[] { "un", "deux", "trois", "quatre", "cinq" })
    {
        feature.Capture(text);
        advance(TimeSpan.FromMilliseconds(300));
    }

    Assert.Equal(5, feature.EntryCount);
    Assert.Contains("5", manager.GetActiveActivities().Single(a => a.Id == ClipboardFeature.SignalActivityId).Title, StringComparison.Ordinal);
}

[Fact]
public void Une_copie_pendant_la_musique_la_recouvre_puis_la_rend()
{
    (ClipboardFeature feature, ActivityManager manager, Func<TimeSpan, DateTimeOffset> advance) = Build();
    manager.PostActivity(new IslandActivity { Id = "media", FeatureId = "media", SceneKey = IslandSceneCatalog.Media, Title = "Morceau", Priority = ActivityPriority.Normal });

    feature.Capture("texte");
    Assert.Equal(ClipboardFeature.SignalActivityId, manager.CurrentActivity?.Id);

    manager.ExpireOverdue(advance(TimeSpan.FromSeconds(3)));
    Assert.Equal("media", manager.CurrentActivity?.Id);
}
```

Le cas « notch ouverte » (point d'attention 1) relève d'`ActivityPolicies.Decide` (un
`Temporary` de priorité normale est mis en file quand la notch est ouverte) : ajouter un test
`Decide(current: scèneOuverte, incoming: signal, NotchPresentation.Expanded) == Queue`.

- [ ] Étape 2 : rouge. Étape 3 :
  - `Publish()` publie l'entrée avec `Policy = Listed` ;
  - `Capture` publie aussi le signal (`Presentation = Signal`, `IconKey = "Clipboard"`, titre `Lang.T("Copié", "Copied")` puis ` · N` en rafale, `Policy = Temporary`, `Duration = SignalDuration`, **`CreatedAt = _now()`** — l'échéance se calcule depuis `CreatedAt`, `IslandActivity.cs:196`, comme `HudActivity.cs:65`) ;
  - le compteur repart à 1 si plus de `BurstWindow` sépare deux copies ;
  - réécriture = moins de `RewriteWindow` **et** texte dérivé du précédent (l'un préfixe de l'autre après `Trim`, ou même adresse sans paramètres de requête) : elle remplace l'entrée de tête et n'incrémente pas le compteur ;
  - un code couleur copié garde son activité couleur (épinglée 10 s, `ClipboardFeature.cs:248`) et ne publie pas le signal en plus ;
  - la lecture sécurisée (`TryReadTextForHistory`) reste dans `OnClipboardUpdated`, en amont de `Capture`.
  Aucun minuteur : l'expiration est celle du gestionnaire. Étape 4 : vert + suite complète. Étape 5 : commit « Presse-papier : un signal bref, puis la copie attend dans la pile ».
- Note : une fois la carte du presse-papier partie, la carte « actions sur copie » (12 s, seulement si le texte contient une échéance et sans modèle) redevient visible ; elle reste telle quelle (hors périmètre), notée dans la feuille de route.

### Tâche C4 : la visite et l'App suivent

**Fichiers :** `IslandWindow.QueueUndoHelp.cs` (pas de point au repos pour les seules entrées en
pile), `IslandWindow.Handoff.cs` (`PrepareHandoff`, l. 47 : pas de passage de Pixel pour le
signal de copie — geste et voyage dureraient jusqu'à 940 ms sur un signal de 2,5 s),
`Windows/Tour/IslandWindow.Tour.Handoffs.cs` (étape « presse-papier · la pile »). L'annonce au
Narrateur existe déjà (`IslandWindow.xaml.cs:1588`) : rien à ajouter.

- [ ] Étape 1 : `UpdateQueueDots` ne compte pas les entrées `Listed` quand rien n'est présenté ; `PrepareHandoff` ne joue pas d'arrivée pour `ClipboardFeature.SignalActivityId`.
- [ ] Étape 2 : build de dev, `--tour --frames` : la musique réapparaît aux étapes 70–72 (planche) ; rafale réelle de 5 copies filmée à 16 ms : un signal, retour au repos à ≈ 2,5 s.
- [ ] Étape 3 : `/code-review` (high), `winui-reviewer`, `/security-review` (presse-papier), PR « Copie : un signal discret, puis la pile ».

---

## Sujet 3 — Fluidité (`fix/fluidite-transitions`)

Base : `chore/constats-jalon-1` (mesure `--frames`, attribution des images lentes) avec
`fix/copie-discrete` fusionnée localement.

Seuils proposés (à valider par l'utilisateur, écran de mesure à 240 Hz) — référence de la visite
du 2026-10-03 entre parenthèses :

| Mesure | Avant | Cible |
|---|---|---|
| Forme vide visible (rafale 16 ms) : passage, départ, repli | 400–800 ms | ≤ 50 ms |
| Saut ou retour en arrière à l'ouverture au clic | 2 images | 0 |
| Images en retard pendant les transitions (visite) | 25 % | ≤ 15 % |
| Images d'animateur > 16,7 ms (visite) | 129 | ≤ 40 |
| Images d'animateur > 50 ms hors démarrage | 3 (max 88 ms) ; 40 > 33 ms | 0 |
| `Render()` > 16,7 ms (visite) | 56, max 84,5 ms | mesuré avant/après ; cible ≤ 33 ms au maximum si F4 le permet, sinon reporté avec sa cause |
| CPU au repos, notch visible (médiane de 3 × 20 s) | 0,31–1,02 % | ≤ 0,5 %, et pas pire qu'avant |

Critère : les quatre premières lignes tenues, les autres mesurées et reportées.

### Tâche F1 : le passage ne montre plus de forme vide

**Fichiers :** `src/SpaceNotch.App/Windows/IslandWindow.Handoff.cs` (`HandoffIn`, l. 106-128 ;
`After`, l. 292-311).

- [ ] Étape 1 (avant) : rafale de 16 ms sur un passage réel (copie, puis notification) ; mesurer la durée de forme vide (images sans contenu lisible) — référence 400 à 800 ms.
- [ ] Étape 2 : la vue d'arrivée n'est plus mise à opacité 0 pendant le geste de Pixel (240 à 520 ms, `Handoff.cs:107, 115`) : un fondu d'entrée de composition (`ContentTransition.Play`, sans dépassement) part dès `HandoffIn`, pendant que les pixels font leur geste et voyagent ; la recette `Read` ne remet plus le titre à largeur 0 (`Handoff.cs:320-321`) quand il est déjà visible — les yeux le parcourent sans le découper, sinon le titre apparaîtrait puis s'effacerait.
- [ ] Étape 3 : build Release, même rafale : forme vide ≤ 50 ms ; planche jointe à la PR.
- [ ] Étape 4 : commit « Passage : le contenu apparaît pendant que la notch s'ouvre ».

### Tâche F2 : au départ et au repli, le contenu sort avec la forme

**Fichiers :** `IslandWindow.xaml.cs` (`RenderOnce`, repli des scènes et des vues de repos,
l. 1075-1099), `IslandWindow.Handoff.cs` (`HandoffBack`), `Animations/ContentTransition.cs`.

- [ ] Étape 1 : `superpowers:systematic-debugging` — relever, sur une rafale de 16 ms, l'image où la scène disparaît et celle où la forme finit de rétrécir (référence : la scène disparaît à t = 0).
- [ ] Étape 2 : la vue sortante n'est plus repliée par le rendu : elle reçoit un fondu de sortie de 120 ms (opacité de composition, sans dépassement) et n'est repliée qu'à la fin ; une nouvelle cible pendant le fondu l'annule (génération).
- [ ] Étape 3 : rafale : contenu visible jusqu'à ce que la forme ait parcouru au moins 60 % de son retrait ; pas de chevauchement de deux textes lisibles.
- [ ] Étape 4 : commit.

### Tâche F3 : l'ouverture au clic ne saute plus

- [ ] Étape 1 : `superpowers:systematic-debugging` — reproduire (rafale de 16 ms, clic au repos → recherche) ; journaliser à chaque étape la position et la taille de la fenêtre (`GetWindowRect`) et la forme dessinée, pour trouver ce qui place la notch 440 px à gauche pendant deux images. Hypothèse à tester en premier : la toile (`IslandWindow.Canvas.cs:45-56`) est agrandie et recentrée dans l'image même où le contenu XAML garde l'ancienne taille (décalage = moitié de l'agrandissement : 600 DIP de lanceur depuis une toile de ~160 DIP). Seconde piste : `CaptureKeyboardForTyping` (retrait de `WS_EX_NOACTIVATE`, `SetForegroundWindow`) dans le même tick.
- [ ] Étape 2 : écrire la cause établie dans cette tâche, puis la correction et sa preuve (rafale sans saut). Si la cause est l'enveloppe de fenêtre (phase D), **arrêt et question à l'utilisateur** avant toute refonte.

### Tâche F4 : le coût par image du ressort de forme

- [ ] Étape 1 : trace des images lentes : la ligne `[IMAGES] image lente` nomme déjà l'abonné ; ajouter, derrière `--frames`, un chronométrage des sous-étapes de `ApplyGeometry` (placement de fenêtre, mise en page, géométries) pour les seules images > 16,7 ms.
- [ ] Étape 2 : la visite désigne la sous-étape dominante parmi celles relevées dans le code : résolution d'écran Win32 (`ResolveDisplay`), géométries neuves (silhouette, reflet sans cache `IslandGeometryFactory.cs:84-90`, voile), mise en page du corps, `PositionAround` du halo (seconde mise en page, surfaces de composition), déplacement de la bulle. Correction ciblée : mettre en cache par transition ce qui ne change pas pendant la course (écran, reflet, halo figé).
- [ ] Étape 3 : visite `--tour --frames` avant / après ; tableau des chiffres dans la PR.
- [ ] Étape 4 : `/code-review` (high), `winui-reviewer`, PR « Fluidité : passages, repli et ouverture sans saut ».

Chaque tâche F se vérifie aussi en mouvement réduit (Windows › Effets d'animation désactivés) :
une capture par transition touchée, jointe à la PR.

### Tâche F5 (proposée, PR séparée `fix/matiere-masquee`) : la matière s'arrête quand la notch est masquée

Cause probable des 26–37 % d'un cœur observés sur la 1.17.1 : `SetIslandVisible(false)` et
`SuspendLife` (`IslandWindow.xaml.cs:2233-2258`) n'arrêtent ni la grille hypnotique
(`HypnoticSurface`), ni la respiration et la pluie (`AtmosphericSurface`), ni Clawd, ni la boucle
de l'accueil.

- [ ] Étape 1 : reproduire — build de dev, activité « travail en cours » (grille) puis notch masquée (plein écran réel) ; CPU sur 20 s et `[IMAGES]`.
- [ ] Étape 2 : à `SetIslandVisible(false)` et au verrouillage, mettre la matière au repos (préréglage None, respiration et pluie coupées, Clawd et accueil arrêtés) ; reprendre au retour par le rendu.
- [ ] Étape 3 : même mesure : CPU ≤ 0,5 % notch masquée ; commit.
Hors de ce sujet sans accord explicite : bascule de la machine à états (E), découpage
d'`IslandWindow` (F), enveloppe de fenêtre complète (D).

---

## Jalon 4 — Accès aux états et UI/UX

Critère : une personne qui n'a lu aucun mode d'emploi atteint chacun des 15 états de la liste des
[constats](2026-10-03-constats.md#liste-des-états-à-atteindre-constat-4). Preuve : scénarios
écrits (un par état : point de départ, consigne en langage courant, état attendu), rejoués avec
computer-use sur le build de dev. Les limites de computer-use (Pixel non réveillé, Échap) sont
contournées par des scénarios au clic.

### Tâche U1 : trois directions comparées sur maquettes (aucun code produit)

- [ ] Étape 1 : maquettes interactives (`show_widget`) de trois directions, chacune montrée sur
  trois moments — premier lancement, repos, activité présente :
  - **A. Le geste enseigné en contexte** : des conseils (TeachingTip Fluent, 3 à 5 mots) apparaissent
    au moment où le geste devient utile (première musique → « Molette · volume »), un par session.
  - **B. Un point d'entrée visible** : au survol, l'aperçu montre une rangée de pastilles
    (Recherche · Minuteur · Presse-papier · Note · …) ; le clic droit reste un raccourci, plus le
    seul chemin.
  - **C. La notch ouverte comme tableau de bord minimal** : un clic au repos ouvre un panneau de
    quatre tuiles (recherche, minuteur, presse-papier, note) au lieu de la recherche seule.
- [ ] Étape 2 : questions à l'utilisateur (`AskUserQuestion`, aperçus) — direction retenue, et ce
  qui est verrouillé par `spacenotch-2.0.md` §15 (le clic ouvre, le survol n'ouvre jamais).
- [ ] Étape 3 : ADR de la direction retenue (`/adr`).

### Tâche U2 et suivantes : incréments, une PR chacun

Définies après le choix de U1, au format de ce plan (tests Core d'abord, captures, relecture).
Premier incrément attendu, quelle que soit la direction : rendre l'aide des gestes et la
présentation joignables depuis la notch elle-même (menu rapide → « Gestes »).

### Scénarios d'accès (rejoués avant et après)

Un fichier `docs/plans/2026-10-04-scenarios-acces.md`, une ligne par état : « Sans rien savoir,
j'essaie de… » · geste tenté en premier par un néophyte · résultat avant · résultat après.

---

## Clôture

- [ ] PR ouvertes (jamais fusionnées), CI lue par `ccd_pr`.
- [ ] Constats et feuille de route à jour.
- [ ] Relais `docs/plans/relais-AAAA-MM-JJ.md` avec le brief suivant.
- [ ] Réglages de mesure rétablis : `ShowChannel` true, `HideOverFullscreen` true ; `CutoutMode` reste « Aucune » (décision de l'utilisateur) ; SpaceNotch 1.17.1 relancée si elle tournait au départ (elle ne tournait pas).
- [ ] Mémoire : faits non retrouvables dans le code.