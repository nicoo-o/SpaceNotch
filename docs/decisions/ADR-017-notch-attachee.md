# ADR-017 — Une notch attachée au bord supérieur, jamais une capsule flottante

**Statut** : Accepté

## Contexte

L'identité visuelle de SpaceNotch repose sur une perception : la surface noire doit sembler
*descendre du bord de l'écran*, comme un morceau d'écran transformé. Une capsule posée quelques
pixels sous le bord se lit immédiatement comme un widget ou une popup, quelle que soit la qualité
de son rendu.

La version précédente tenait déjà le bord supérieur droit, mais laissait trois portes ouvertes :

- un réglage `TopOffset` (−40 à 200 DIP) qui décollait la forme du bord ;
- un rayon unique de 12 DIP, borné à la moitié de la hauteur comme un rectangle arrondi — trop
  sage pour les « grands arrondis organiques » voulus ;
- une ombre dessinée par un rectangle arrondi sur ses quatre coins, et un satellite en disque
  flottant à côté de la forme.

## Décision

1. **Un seul mode géométrique, `IslandGeometryMode.TopAttached`.** Un test vérifie que
   l'énumération n'en contient pas d'autre : ajouter une forme flottante oblige à relire cette
   décision.
2. **Invariant de silhouette** : aucun point au-dessus du bord, bord supérieur à y = 0 sur toute la
   largeur, forme qui descend jusqu'à sa hauteur. Vérifié par les tests.
3. **Épaules concaves** (8 DIP par défaut) : deux quarts de courbe tangents au bord de l'écran et
   au flanc. Le bord « coule » dans la notch.
4. **Deux rayons interpolés** : 26 DIP en compact, 34 ouvert, interpolés continûment selon la
   hauteur (smoothstep entre 34 et 120 DIP). Borne : `min((l − 2e)/2, h − e)` — la notch n'a
   qu'un bord libre, son congé peut occuper toute la hauteur sous les épaules.
5. **`TopOffset` toujours ramené à zéro** par `Sanitize` ; le réglage disparaît de l'interface.
6. **Une géométrie pour tout** : surface, reflet, zone de contenu, ombre (prolongée au-dessus du
   bord pour que son sommet soit plat) et atmosphère utilisent la même silhouette.
7. **Satellite flottant désactivé par défaut** ; la pile se signale par des points dans la notch.

## Justification

- La contrainte est nommée dans le type et dans les tests : elle ne peut pas être perdue par une
  refonte d'interface.
- Interpoler le rayon selon la hauteur plutôt que selon l'état évite tout saut de courbure au
  milieu du morphing — même principe que la dissolution (ADR-008).
- Les épaules sont la différence perceptible entre « collé au bord » et « découpé dans le bord ».

## Conséquences

- La largeur des formes inclut les épaules : le contenu est mesuré depuis les flancs (marge de la
  zone de contenu égale à l'épaule effective), et les scènes gagnent 16 DIP de largeur.
- Une configuration ancienne avec un décalage vertical est silencieusement recollée au bord.
- Le rayon réglé par l'utilisateur n'est plus « le » rayon tracé : c'est une borne d'interpolation.
- Un utilisateur qui préférait le satellite doit le réactiver.

## Alternatives écartées

- **Garder `TopOffset` borné à des valeurs négatives** : une valeur positive reste possible par
  édition du fichier ; le seul invariant fiable est zéro.
- **Rayon unique plus grand** : impossible sur la lèvre de veille (18 DIP), trop sage sur une
  scène de 140 DIP.
- **Coins supérieurs arrondis (convexes)** : c'est exactement la silhouette d'une capsule.
