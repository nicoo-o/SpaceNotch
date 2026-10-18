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
5. **Fermer** l'instance lancée à l'étape 2 (et seulement celle-là : `Stop-Process -Id <PID>`), puis
   montrer à l'utilisateur les images utiles.

## Arbre UIA — noms accessibles et cibles (facultatif)

`winapp ui` lit l'arbre UI Automation de la vraie fenêtre, sans injecter d'entrée. Essayé le 2026-10-18
(winapp 0.6.1, `--demo`) : l'arbre existe, avec noms, `AutomationId` et rectangles. À lancer pendant
l'étape 3, avec le PID qu'affiche `winapp run` :

```powershell
$winapp = "$HOME/.nuget/packages/microsoft.windows.sdk.buildtools.winapp/0.6.1/tools/win-x64/winapp.exe"
& $winapp ui inspect -a <PID> --depth 4            # id, type, nom, rectangle
& $winapp ui inspect -a <PID> -w <HWND> -i         # éléments interactifs seulement
& $winapp ui get-property IslandBody -a <PID> -w <HWND>
```

- L'app a deux fenêtres (« SpaceNotch » et « SpaceNotch Atmosphere ») : passer `-w <HWND>` pour viser la
  première.
- À vérifier : chaque contrôle interactif a un `Name` non vide ; sa cible fait au moins 32 DIP (les
  rectangles sont en pixels physiques : diviser par l'échelle d'affichage) ; le nom suit le contenu
  (corps de la notch : « AirPods Pro, Connecté »).
- `-i` sans contrôle interactif (repos, `--demo` sans survol) répond « Found 0 elements » : ce n'est pas
  une erreur, c'est l'état.
- Ne pas employer `click`, `hover`, `drag` ni `send-keys` sans l'accord de l'utilisateur : ils injectent
  de vraies entrées et déplacent sa souris.

Une capture ne prouve que ce qu'elle montre : si l'état voulu n'est pas à l'écran, le dire plutôt
que conclure.
