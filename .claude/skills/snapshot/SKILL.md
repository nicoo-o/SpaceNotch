---
name: snapshot
description: Capture le haut de l'écran pendant que SpaceNotch tourne (tools/ui-snapshot/capture.ps1) puis lit les images, pour vérifier à l'œil un changement d'interface de la notch — forme, animation d'ouverture, scène.
argument-hint: "[nom-de-la-capture] [--tour|--demo|--settings]"
---

# Capture de la notch

Arguments : $ARGUMENTS (nom par défaut : `capture` ; mode par défaut : démarrage normal)

1. **Construire** la configuration qui sera lancée :
   `dotnet build SpaceNotch.sln -c Release -p:Platform=x64`.
2. **Lancer** l'app avec son identité de développement (un build non empaqueté ne démarre pas
   directement) — commande `winapp run ... --detach` de `docs/development/building.md`, en passant
   le mode demandé dans `--args` (`--tour`, `--demo`, `--settings`). Si une instance tourne déjà,
   demander à l'utilisateur avant de la fermer.
3. **Attendre** que la notch se pose (5 s au démarrage normal, 8 s avec `--demo`), puis capturer
   dans le scratchpad :
   ```powershell
   ./tools/ui-snapshot/capture.ps1 -OutDir <scratchpad>/shots -Name <nom> -Count 4 -IntervalMs 700
   ```
   Pour une animation, rapprocher les images (`-IntervalMs 150`, `-Count 8`).
4. **Lire** chaque PNG avec l'outil Read et décrire ce qu'on voit : forme et coins, centrage sous
   la caméra, texte coupé ou trop petit, zones grises ou décalées pendant la transition, contraste.
   Comparer au comportement attendu (`docs/state-machine.md`, `docs/ux/`).
5. **Fermer** l'instance lancée à l'étape 2 (et seulement celle-là), puis montrer à l'utilisateur
   les images utiles.

Une capture ne prouve que ce qu'elle montre : si l'état voulu n'est pas à l'écran, le dire plutôt
que conclure.
