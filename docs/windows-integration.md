# Intégration Windows

## Cible

Windows 11 23H2 minimum (`10.0.26100`). Aucun chemin de compatibilité Windows 10 n'est maintenu :
le projet ne veut pas de code legacy pour une base qui n'apporte pas les matériaux et les API
utilisés.

## Fenêtres : deux surfaces, pas une

C'est le point technique le moins évident et le plus structurant du projet.

| Surface | Rôle | Styles étendus |
|---|---|---|
| `IslandWindow` | Interactive : survol, clic, molette, glisser-déposer | `WS_EX_TOOLWINDOW \| WS_EX_NOACTIVATE` |
| `AtmosphereWindow` | Décorative : halo, ombre, dissolution | `+ WS_EX_TRANSPARENT` |

### Pourquoi deux fenêtres

Pour que la marge du halo laisse passer les clics, il faudrait une surface clic-traversante **par
zone**. WinUI 3 ne l'offre pas :

- `SetLayeredWindowAttributes(LWA_COLORKEY)` est **inopérant** dans une fenêtre WinUI 3 ;
- `HTTRANSPARENT` ne franchit **pas** la frontière entre threads ;
- un `SetWindowRgn` découperait le rendu en même temps que la zone cliquable.

Il ne reste donc qu'une voie : deux fenêtres, chacune homogène. La surface interactive fait
*exactement* la taille de l'Island — elle ne capture jamais un clic hors de son corps — et la surface
décorative, marquée `WS_EX_TRANSPARENT`, laisse tout passer sans traiter le moindre message.

`WindowChrome.PlaceAbove` garantit que la surface interactive reste au-dessus de la décorative
lorsque les deux sont `topmost`.

### Focus clavier sans vol de focus

L'Island porte `WS_EX_NOACTIVATE` : elle ne vole jamais le focus, ce qui est indispensable pour un
overlay. Mais une fenêtre qui ne peut pas être activée ne reçoit **aucun** message clavier — l'Island
serait alors inaccessible au clavier.

`WindowChrome.SetKeyboardCapture` rend le focus possible **pendant** que l'utilisateur désigne
l'Island au pointeur, et le retire aussitôt qu'il s'en éloigne. Le clavier fonctionne quand on
interagit ; l'Island reste sans effet sur la fenêtre active le reste du temps.

### Coins et DWM

`DWMWCP_DONOTROUND` désactive les coins arrondis que Windows 11 applique par défaut : la géométrie de
l'Island est définie par sa propre surface, pas par DWM. Les styles étendus sont appliqués avec un
`SWP_FRAMECHANGED` — un changement de style n'est pris en compte par DWM qu'après un passage de cadre
explicite.

## Multi-moniteur

`DisplayEnumerator` énumère les moniteurs et expose leurs **zones de travail** en DIPs. Trois
placements sont supportés : écran principal, écran contenant le pointeur, écran explicitement désigné
(`IslandDisplayMode`).

`ScreenChangeWatcher` observe les messages de changement d'environnement (`WM_DISPLAYCHANGE`,
session, DPI). Les messages arrivent **en rafale** — un changement de résolution en produit
plusieurs — et sont donc coalescés par un unique minuteur à usage unique : un seul recalcul suffit.

## DPI

Intégré dès le premier commit, pas « corrigé plus tard pour le 4K ».

- `MonitorDpi` lit `GetDpiForWindow` et `GetDpiForMonitor`.
- `DisplayInfo` porte le DPI **réel du moniteur**, pas une valeur globale.
- `ApplyGeometry` convertit les DIPs avec l'échelle du **moniteur cible**.
- `DpiHelper` centralise les conversions ; il est testé sans machine Windows graphique.

Conséquence vérifiable : sur une configuration 100 % + 150 %, l'Island se place correctement sur les
deux écrans — c'est précisément le bug qui a dû être corrigé en v2.0.3 du projet de référence.

## Média

`WindowsMediaSessionManager` utilise la session média globale (`GlobalSystemMediaTransportControlsSessionManager`) :
pochette, titre, artiste, application source, état de lecture, position, durée, et commande de
transport (`TryPlayAsync`, `TryPauseAsync`, `TrySkipNextAsync`, `TrySkipPreviousAsync`,
`TryChangePlaybackPositionAsync`).

`AlbumPalette` extrait la couleur dominante de la pochette pour alimenter `ActivityTint`. La teinte
est désaturée avant application : l'intention est un halo ambiant discret, jamais un dégradé saturé.

## Audio

`CoreAudioVolumeListener` s'abonne à `IAudioEndpointVolume` pour les changements de volume — par
rappel, pas par scrutation.

## Luminosité

`BrightnessService` utilise l'API de luminosité (`DeviceIoControl` /
`IOCTL_VIDEO_SET_DISPLAY_BRIGHTNESS` selon le matériel). Toute mise en œuvre dépend du pilote : le
service se déclare indisponible proprement si la machine ne l'expose pas, au lieu d'échouer.

## Presse-papier

`ClipboardMonitor` utilise `AddClipboardFormatListener` / `RemoveClipboardFormatListener`. C'est un
abonnement **réel** : il est posé à l'activation de la fonctionnalité et retiré à sa désactivation.
`WM_CLIPBOARDUPDATE` est routé par le registre vers la fonctionnalité propriétaire.

Aucune donnée n'est observée tant que l'utilisateur n'a pas explicitement activé la fonctionnalité.

## Notifications

`WindowsNotificationListener` utilise `UserNotificationListener`, qui exige une **permission
explicite**. Sans cette permission, la fonctionnalité se déclare indisponible au lieu de boucler en
tentant de la redemander.

## Bluetooth

`BluetoothWatcher` observe l'état des périphériques par événements. L'état de charge n'est pas exposé
uniformément par l'API : le service publie ce que l'appareil rapporte et rien de plus.

## Démarrage automatique

`StartupRegistration` écrit dans la clé `Run` du registre de l'utilisateur courant, avec une
signature exacte de la commande. Une inscription n'est appliquée que sur action explicite, et son
échec est journalisé — jamais avalé.

## Modes de fond

| Mode | Chemin | Dépendance système |
|---|---|---|
| `Transparent` | Backdrop transparent du compositeur | Réglage « effets de transparence » |
| `Blurred` | Pinceau de fond hôte (**pas** Desktop Acrylic) | Aucune |
| `Opaque` | Surface portée par la vue | Aucune |

Le mode `Blurred` passe délibérément par le pinceau de fond du compositeur plutôt que par Desktop
Acrylic : Acrylic est **désactivé par Windows en mode économie d'énergie**, alors que ce pinceau reste
sous notre contrôle. En mode `Auto`, le choix dépend de `SystemVisualState.TransparencyEffectsEnabled`
et retombe sur `Opaque` — un repli déterministe, jamais une surface invisible.

## Accessibilité

`SystemVisualState` lit depuis le registre les préférences système : effets de transparence,
réduction des animations, contraste élevé. Les transitions par ressort deviennent des fondus quand
Windows le demande. C'est le comportement demandé par l'utilisateur, pas un repli dégradé.

## Interop : nommage

Toutes les déclarations P/Invoke vivent dans `NativeMethods`, toutes les constantes dans
`NativeConstants`. Aucune constante numérique n'apparaît en clair dans le code appelant.
