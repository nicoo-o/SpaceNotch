# ADR-029 — Le compagnon vivant : Pixel devient l'identité de SpaceNotch et l'avatar des agents

**Statut** : Proposé (direction choisie par l'utilisateur le 2026-10-06 ; à livrer)

## Contexte

SpaceNotch se décrit comme une notch calme : « Alive, never busy », « Made to disappear »
(README), et la vision de [SpaceNotch 2.0](../ux/spacenotch-2.0.md) §1 écrit « ni une collection de
widgets, ni un dashboard ». Le §15 verrouille deux règles : **« aucune animation permanente »**
(Animation) et **« silence au repos »** (Performance).

Le code raconte autre chose :

- **Pixel**, les yeux de la notch au repos, est actif par défaut (`AppSettings.cs:569`, `ShowPixel =
  true`). Une migration le réactive une fois chez ceux qui l'avaient éteint (`AppSettings.cs:833-836`).
  Il suit le curseur toutes les 80 ms (`IslandWindow.Pixel.cs:17`), cligne, bâille, et relit CPU,
  batterie et réseau toutes les 2 s (`IslandWindow.PixelLife.cs:19, 109`). Aucun ADR ne le décrit.
- **Clawd**, le personnage de Claude Code, représente les agents dans la notch (ADR-024).
  Il est en style « fidèle » par défaut (`AppSettings.cs:513`), et aucun style ne le désactive
  (`Clawd.cs:20-30`). ADR-024 (l. 75) le justifiait par « SpaceNotch est gratuit ». Or les
  [règles de marque d'Anthropic](https://www.anthropic.com/legal/trademark-guidelines)
  n'accordent aucune exception aux projets gratuits. C'est un constat, pas un avis juridique.

L'enquête de direction du 2026-10-06 (`docs/plans/2026-10-06-direction.md`) a présenté trois
positionnements : A, la notch calme ; B, le compagnon vivant ; C, la notch des développeurs.
L'utilisateur a choisi **B**.

## Décision

1. **Pixel est l'identité de SpaceNotch.** Actif par défaut, gardé tel qu'aujourd'hui
   (regard, clignements, humeurs, lecture de l'état du système). Il est assumé dans le README et
   dans le [langage visuel](../ux/design-language.md).
2. **Pixel est aussi l'avatar des agents.** Il remplace Clawd. Les yeux réfléchissent, demandent,
   fêtent la fin ou signalent une erreur, avec les quatre humeurs que Clawd portait (ADR-024).
   Clawd, ses trois styles et la mention « non affilié à Anthropic » sont retirés.
3. **Le §15 est rouvert sur deux lignes**, et seulement celles-là :
   - « Animation : ressort + morph ; aucune animation permanente » devient « ressort + morph ;
     **Pixel vit au repos** ; rien d'autre n'anime le repos » ;
   - « Performance : silence au repos » devient « silence au repos, **hors Pixel** ».
   Sur demande explicite de l'utilisateur, Pixel n'a **pas de budget de CPU** au repos. Seul le
   mouvement réduit de Windows le calme.
4. Les autres décisions du §15 restent verrouillées.

## Justification

- Choix de l'utilisateur, après exposé des conséquences des trois directions.
- Pixel existe déjà, actif par défaut chez tous ceux qui l'ont gardé. La décision met la
  documentation d'accord avec le produit, au lieu d'éteindre ce qui le distingue.
- Une seule mascotte, propre au projet, supprime l'exposition liée à la marque d'Anthropic.
  Elle réutilise le code de Pixel (humeurs, regard) au lieu d'inventer un second personnage.

## Conséquences

- **README, README.fr et `design-language.md`** sont à réécrire autour de Pixel.
  « Alive, never busy » et « Made to disappear » ne décrivent plus le repos tel qu'il est.
- **ADR-024** est en partie remplacée : la partie Clawd ne vaut plus. L'index le signale.
- **Code à retirer ou remplacer** : `ClawdView`, `Clawd.cs`, `ClawdStyle`, le réglage
  « Apparence › Clawd », l'étape « claude code » de la visite (`IslandWindow.Tour.Handoffs.cs:82`)
  et la mention dans les Réglages. Les humeurs passent à Pixel.
- **Plus difficile.** Le repos a désormais un coût de fond. Les mesures « au repos » de
  `performance.md` et du README doivent dire « Pixel compris », car le chiffre de 0,1 % ne
  tient plus. Sur batterie, le coût n'est pas mesuré (feuille de route n° 43). C'est un risque
  accepté.
- **Accessibilité** : le mouvement réduit doit vraiment calmer Pixel, ce qui est à vérifier.
  Un réglage « intensité de Pixel » est une piste, pas une obligation.
- La migration qui réactive Pixel une fois (`AppSettings.cs:833-836`) est cohérente avec ce
  choix, mais elle ne doit jamais repasser outre un choix explicite de l'utilisateur.

## Alternatives écartées

- **A · La notch calme**, avec Pixel désactivé par défaut et les fonctions développeurs en module
  optionnel : écartée par l'utilisateur. Elle aurait gardé le §15 intact, mais retiré ce qui rend
  SpaceNotch reconnaissable.
- **C · La notch des développeurs et agents IA** : écartée, car elle va contre la cible
  non technicienne. Le canal local et les hooks restent, sans être la vitrine.
- **Vivant mais sobre** (Pixel qui s'endort, budget de 0,5 % au repos) : proposé, écarté par
  l'utilisateur au profit de « vivant sans limite ». C'est réversible plus tard, par une
  option d'intensité.
- **Garder Clawd avec la mention**, ou **demander l'autorisation à Anthropic** : écartées au
  profit d'une mascotte propre. Il n'y a ni dépendance à une autorisation tierce, ni friction
  pour la signature (SignPath), le Store ou winget.
- **Une seconde mascotte originale** pour les agents : écartée, car elle ferait deux
  personnages à faire vivre et à garder cohérents.
