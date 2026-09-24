# ADR-020 — Trois bords, plusieurs écrans, et une matière réglable

**Statut** : Accepté — prolonge ADR-017 et ADR-019

## Contexte

Après ADR-019, la notch détachée restait sur l'écran où on l'avait prise et ne se raccrochait
qu'en haut. L'utilisateur demande : passer d'un écran à l'autre, pouvoir l'accrocher aux autres
côtés de l'écran, savoir comment la raccrocher, et personnaliser le détachement et le style.

Réponses aux questions interactives : entre deux écrans, elle **passe mais résiste** ; bords
**haut, gauche et droite** (le bas est exclu, pour ne pas gêner la barre des tâches) ; sur un
côté, une **languette** qui s'ouvre vers l'intérieur ; **double-clic** pour raccrocher ;
au démarrage, **le dernier bord accroché** ; réglages : sensation du ressort, étirement,
distance d'arrachement, aimants et goutte, et pour le style couleur, transparence, épaules et
arrondis, ombre et contour, tailles de la bulle et de la languette.

## Recherche

- Windows, DPI par écran (Per-Monitor v2) : une fenêtre qui change d'écran reçoit
  `WM_DPICHANGED` ; les positions sont en pixels physiques, les tailles en DIPs doivent être
  reconverties à l'échelle du nouvel écran, sans quoi la fenêtre « oscille » entre deux écrans.
- Applications comparables : *Dynamic Edge* s'accroche en haut ou sur l'un des côtés comme une
  « lame » ; *Notchify* choisit l'écran et bascule entre île détachée et notch attachée.
- iOS, image dans l'image : les cibles de lâcher sont choisies sur le point projeté.

## Décision

1. **Un repère par bord** (`EdgeFrame`) : toute forme accrochée est calculée comme en haut —
   `u` le long du bord, `v` vers l'intérieur — puis tournée. La languette de gauche *est* la
   notch du haut, couchée : mêmes épaules, mêmes congés, même goutte. Les contours sont remis
   dans le sens horaire après la rotation, pour que leur superposition reste leur union.
2. **Languette** (`SideTab`) : 28 × 76 DIPs au repos (petite 24 × 60, grande 34 × 96), épaules
   de 10 DIPs, placée à la hauteur où elle a été lâchée et gardée hors de la barre des tâches.
   Au repos : l'icône ou la grille hypnotique et une jauge verticale, jamais de texte couché.
   Au survol, elle s'avance de 25 % ; ouverte, elle s'étend vers le centre avec le contenu à
   l'horizontale, les épaules passant de la largeur à la hauteur.
3. **Lâcher** : près d'un côté, hors des 12 % du haut et du bas réservés aux aimants de coin,
   la pastille s'y accroche à cette hauteur. Les milieux des côtés cessent alors d'être des
   aimants. Côtés et aimants se désactivent dans les réglages.
4. **Écrans** : la main qui pousse la pastille dans un écran voisin doit y entrer de 56 DIPs
   (ou dès l'entrée, résistance désactivée). La pastille change alors de repère : même position
   physique, vitesse convertie à la nouvelle échelle. Elle s'accroche aux bords de l'écran où
   elle se trouve.
5. **Raccrocher** : la lancer ou la lâcher près d'un bord ; **double-clic** (au délai du
   double-clic de Windows, borné à 200–400 ms) pour revenir au dernier bord ; menu de l'icône.
6. **Mémoire** : le bord, la position le long du côté et l'écran — repéré par son rectangle
   physique — sont enregistrés à chaque accroche ; au démarrage, la notch y revient. Un écran
   disparu ramène la notch en haut de l'écran principal.
7. **Réglages** : sensation (Souple 0,30 s / 0,55 ; Naturelle 0,22 s / 0,62 ; Ferme 0,16 s /
   0,75), étirement 0–10 %, distance d'arrachement 20–80 DIPs, aimants, goutte (sinon un simple
   pop), résistance entre écrans, bord d'accroche, côtés autorisés ; teinte (noir OLED,
   graphite, couleur libre), opacité 55–100 %, épaules de la languette 0–16, arrondi de la
   pastille ouverte 12–48, ombre 0–60 %, contour fin optionnel, tailles de la bulle et de la
   languette.

## Conséquences

- La notch reste une forme **attachée** au repos ; la languette l'est aussi, à un côté.
- L'atmosphère d'une languette n'est qu'une ombre, prolongée au-delà du bord, et ne déborde
  jamais sur l'écran voisin.
- Accrocher la notch par glisser choisit l'écran : le mode d'écran passe à « l'écran où elle a
  été accrochée ».
- Le bord du bas reste exclu ; l'ajouter demanderait de relire ADR-017.

## Alternatives écartées

- **Texte tourné à 90°** dans la languette : moins lisible ; le contenu attend l'ouverture.
- **Changer d'écran par le menu seulement** : l'utilisateur veut le geste.
- **Repérer l'écran par son handle** : un `HMONITOR` change à chaque démarrage.
