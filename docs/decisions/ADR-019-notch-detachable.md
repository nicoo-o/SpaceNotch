# ADR-019 — Une notch qu'on peut arracher au bord, et qui se partage pour ce qui compte

**Statut** : Accepté — amende ADR-017

## Contexte

ADR-017 interdit la capsule flottante : au repos, la notch descend du bord de l'écran. Deux
demandes de l'utilisateur la prolongent sans la contredire :

1. pouvoir **décoller la notch du haut de l'écran en la tirant** à la souris, avec un état
   « fluide, qui rebondit » pendant qu'on la déplace, parfait dès la première version ;
2. que la notch **se partage** quand deux choses importantes coexistent — mais seulement
   pour celles-là.

Réponses de l'utilisateur aux questions interactives : forme **goutte qui s'étire** à
l'arrachement ; **ressort + étirement léger** pendant le déplacement ; arrachement **en
tirant avec résistance** ; au lâcher, **lancer + aimants** ; ouverture **vers l'espace
libre** ; **toujours accrochée au redémarrage** ; sont importants : **téléchargement,
appel, enregistrement, priorité critique** ; partage par **petite bulle** accrochée au
bord, qui échange sa place avec la notch quand on la touche, et qui **suit** une notch
détachée.

## Recherche

- Apple, WWDC 2018 « Designing Fluid Interfaces » : l'objet suit la main, hérite de sa
  vitesse, se projette comme un défilement (décroissance exponentielle
  `v/1000 · r/(1 − r)`, r = 0,998) et rejoint la cible la plus proche **du point projeté**
  — c'est l'image dans l'image de FaceTime. Ressorts interruptibles ; amortissement ≈ 0,8
  quand l'élan est en jeu.
- Résistance élastique d'UIScrollView : `x·d·c / (d + c·x)`, c = 0,55.
- Déformation des surfaces d'interface : 2 à 5 % au plus, liée à la vitesse, à aire
  constante (squash & stretch) ; au-delà, l'objet devient un jouet.
- Arrachement d'onglets (Chrome, Firefox) : une « magnétisation » verticale avant de
  détacher ; les rapports de bugs montrent qu'un seuil trop bas arrache par accident.
- Tolérance du clic : ~6 à 10 px avant qu'un appui devienne un glisser ; validation au
  relâcher, jamais à l'appui.

## Décision

1. **Deux états d'accroche** (`NotchAttachment`) : `Attached`, la forme de référence, et
   `Floating`, qui n'existe que par un geste de l'utilisateur. La géométrie de repos reste
   `TopAttached` (le test d'ADR-017 tient toujours) ; la notch revient accrochée au
   démarrage, à un changement d'écran, et quand le réglage est retiré.
2. **Le clic est validé au relâcher.** Un appui qui bouge de moins de 6 DIPs reste un clic.
3. **Tirage** : la notch accrochée s'allonge vers le bas avec la résistance d'UIScrollView
   (asymptote 90 DIPs), à aire à peu près constante. Relâchée avant **40 DIPs**, elle
   remonte d'un ressort. Réglage désactivé : elle s'allonge et remonte, sans jamais céder.
4. **Arrachement en goutte** (`GooBridge`) : la trace reste au bord et se résorbe, un fil en
   sablier relie la trace à la pastille, s'amincit, se rompt à 55 % et ses deux pointes
   rentrent dans leurs formes (340 ms). Les flancs du fil sont des **congés** : arcs
   d'ellipse horizontaux aux bouts, verticaux à la taille. Toutes les pièces sont opaques,
   de même teinte et dans le même sens : leur superposition est leur union.
5. **Pastille** : les épaules restent au bord ; les quatre coins s'arrondissent, et le
   congé glisse vers le cercle quand le rayon atteint la demi-hauteur — une vraie pastille,
   pas un rectangle arrondi. Elle **naît d'un pop** (0,9 → 1, ressort rebondissant).
6. **Déplacement** : la pastille suit la main par un ressort (réponse 0,22 s, amortissement
   0,62 — un rebond visible à l'arrêt) et s'étire de 5 % au plus dans le sens de sa course,
   à aire constante ; le contenu, lui, ne se déforme jamais. Aux bords de l'écran, elle
   résiste au lieu de sortir.
7. **Lâcher** : vitesse mesurée sur 80 ms (nulle si la main s'est arrêtée 60 ms avant) ;
   sous 300 DIPs/s, elle **reste où elle a été posée** ; sinon le point projeté choisit un
   **aimant** (4 coins, milieux gauche, droit et bas) ; le haut, dans la bande centrale, la
   **raccroche** : elle vole au bord et la goutte l'aspire à l'envers. La vitesse de la main
   est transmise au ressort (amortissement 0,8) : aucun raccord visible.
8. **Ouverture flottante vers l'espace libre** : vers le bas dans la moitié haute, vers le
   haut dans la moitié basse, décalée seulement si elle toucherait un bord.
9. **Matière flottante** : seule l'ombre reste, tout autour ; la dissolution, le halo et la
   pluie appartiennent au bord et disparaissent.
10. **Bulle** (`SplitPresentation`) : si une activité importante n'est pas présentée — rôle
    `Download`, `Call`, `Recording` ou priorité `Critical` — elle prend une mini-notch
    accrochée au bord, épaule contre épaule, à droite de la notch (à gauche faute de place).
    Deux activités ordinaires ne partagent jamais : la seconde reste dans la pile. Un
    recouvrement temporaire (volume, notification) ne prend jamais de bulle. Toucher la
    bulle **échange les rôles** (creux puis rebond). Ouverte, la notch montre l'activité en
    grand et la bulle se retire ; elle revient à la fermeture. Détachée, la bulle devient un
    disque qui **suit** la pastille avec un ressort plus mou.
11. **Appels et enregistrements** viennent de l'indicateur de confidentialité de Windows
    (`CapabilityAccessManager\ConsentStore`, notifié par `RegNotifyChangeKeyValue`, sans
    scrutation) : micro tenu par une application d'appel = appel ; sinon enregistrement.
12. **Coût** : tout est intégré image par image seulement pendant le geste ; au repos, aucun
    écouteur de rendu. Réduction des animations : ni goutte, ni ressort, ni étirement — la
    notch va directement où elle doit aller.

## Conséquences

- ADR-017 reste la règle du repos et du démarrage ; la forme flottante est un état voulu,
  jamais un défaut.
- Le moniteur est figé pendant le geste et tant que la notch flotte ; passer la pastille sur
  un autre écran n'est pas pris en charge.
- La bulle est une fenêtre à part : l'espace entre elle et la notch laisse passer les clics.
- Un greffon déclare `Role` pour qu'une activité mérite la bulle.

## Alternatives écartées

- **Métaballes par flou + seuil alpha** : exige un effet GPU (Win2D) et un rendu permanent
  pendant l'arrachement ; les congés analytiques donnent la même lecture, testés dans le cœur.
- **Déplacer la fenêtre par le glisser système** (`WM_NCLBUTTONDOWN`) : aucun ressort,
  aucun élan, aucune goutte.
- **Mémoriser la position flottante** : écarté par l'utilisateur ; la notch accrochée est la
  forme de référence.
- **Deux moitiés égales** pour le partage : illisible ; la bulle garde une hiérarchie claire.
