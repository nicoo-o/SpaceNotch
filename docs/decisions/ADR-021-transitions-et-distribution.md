# ADR-021 — Transitions entre les états, et un .exe produit par GitHub

**Statut** : Accepté

## Contexte

L'utilisateur demande des transitions plus soignées entre les états de la notch, et un
exécutable Windows téléchargeable depuis GitHub.

Réponses aux questions interactives : contenu qui change en **flou + fondu** ; transitions à
améliorer : **ouvrir / fermer**, **changement d'activité**, **aperçu au survol**, **apparition
et échange de la bulle** ; distribution : **un .exe unique et un dossier zippé** ; déclencheurs :
**bouton manuel** et **Release sur tag** ; première version **v1.0.0** ; envoi par **Pull
Request vers main**.

## Recherche

- Dynamic Island : une seule ligne de temps pour la forme et le contenu ; le texte change par un
  flou, les ajouts et retraits par des fondus ; en mouvement réduit, il ne reste que le fondu.
- WinUI 3 n'a pas de filtre de flou sur un élément ; l'acrylique « dans l'application » floute ce
  qui est derrière lui dans la même fenêtre, sur le GPU.
- Distribution d'une application WinUI 3 non empaquetée : `WindowsAppSDKSelfContained`,
  `SelfContained`, `PublishSingleFile`, `IncludeAllContentForSelfExtract`, et
  `EnableMsixTooling=true` — sans lui, le fichier de ressources n'est pas renommé en
  `resources.pri` et l'application publiée plante au démarrage (WindowsAppSDK#3718).

## Décision

1. **Voile de flou** : un tracé à pinceau acrylique (sans teinte), exactement dans la
   silhouette, posé sur le contenu quand il change et dissipé en 260 ms par le compositeur ;
   retiré de l'arbre ensuite. Joué pour un titre qui change (pas pour une mesure qui défile),
   un changement d'activité, l'ouverture et la fermeture. Sans acrylique ou en mouvement
   réduit, il ne reste que le fondu.
2. **Ouvrir / fermer** : à l'ouverture, le contenu attend 90 ms que la forme ait pris
   l'essentiel de sa place, puis arrive flou et se précise ; à la fermeture, la forme compacte
   revient sous le voile. La forme s'écrase de 3 % au plus quand elle rebondit, à aire
   constante.
3. **Changement d'activité** : la forme respire — elle se resserre à 90 × 94 % pendant 110 ms
   avec le ressort vif, puis s'élargit vers sa nouvelle forme — et le contenu change sous le
   voile.
4. **Aperçu** : ressort d'effleurement plus vif (réponse × 0,72) ; le contenu suit la forme
   qui s'avance (3 DIPs, vers le bas ou vers l'intérieur).
5. **Bulle en goutte** (`BubbleBridge`) : elle sort du flanc de la notch, reliée par un fil qui
   part des corps — jamais des épaules —, s'amincit et se rompt (380 ms). Elle y rentre pour
   disparaître (220 ms). L'échange passe par la goutte : la bulle rentre, les rôles
   s'inversent quand elles ne font qu'une, l'autre activité ressort.
6. **Distribution** : workflow `Release` — bouton manuel (fichiers dans les Artifacts) et tag
   `v*` (Release GitHub). Il teste, publie `SpaceNotch.exe` (fichier unique) et
   `SpaceNotch-win-x64.zip`, puis lance l'exécutable en démonstration 12 s pour attraper un
   plantage au démarrage (avertissement, journal joint).

## Conséquences

- Le voile coûte une passe acrylique pendant 260 ms, puis rien.
- L'exécutable n'est pas signé : SmartScreen avertit au premier lancement.
- Le bouton « Run workflow » n'apparaît qu'une fois le workflow sur la branche par défaut.
