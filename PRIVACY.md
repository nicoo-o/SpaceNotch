# Confidentialité · Privacy

**Français** · [English](#english)

SpaceNotch n'a ni compte, ni télémétrie, ni publicité. Rien n'est envoyé pour mesurer son usage.
Voici chaque connexion que l'application peut faire, et quand.

| Service | Quand | Ce qui part |
|---|---|---|
| GitHub (`api.github.com`, `github.com`) | **par défaut**, toutes les 6 h, sauf si les mises à jour sont « Désactivées » | une demande de la dernière version, avec le numéro de ta version ; puis le téléchargement de l'installeur |
| Open-Meteo (`geocoding-api.open-meteo.com`, `api.open-meteo.com`) | seulement si tu as indiqué une ville pour la météo | le nom de la ville, puis ses coordonnées |
| LRCLIB (`lrclib.net`) | seulement si les paroles sont activées | l'artiste, le titre, l'album et la durée du morceau qui joue |
| Spotify (`accounts.spotify.com`, `api.spotify.com`) | seulement si tu connectes ton compte Spotify | ce que l'autorisation Spotify demande, avec ta propre application développeur |
| Discord | seulement si tu l'actives | une connexion **locale** à l'application Discord de ton PC, rien sur Internet |
| Recherche sur le Web | seulement quand tu lances une recherche Web depuis la notch | ta recherche, envoyée par ton navigateur au moteur choisi (Bing, Google ou DuckDuckGo) |
| Assistant | désactivé par défaut | règles locales ou modèle sur l'appareil ; avec ta propre clé Claude, ta demande part chez Anthropic |
| Partage vers le téléphone | seulement quand tu partages un fichier | un échange **sur ton réseau local**, le temps de l'envoi |

**Sur ton PC, et nulle part ailleurs :**
- les réglages, dans `%AppData%\SpaceNotch` ;
- le journal, dans `%LocalAppData%\SpaceNotch\logs` (il peut contenir des chemins de fichiers : relis-le avant de le partager) ;
- les notes et les rappels ;
- l'historique du presse-papier, qui est désactivé par défaut, ne garde rien sur le disque et ignore les mots de passe.

**Notifications Windows.** Si tu l'autorises, SpaceNotch lit tes notifications pour les afficher dans
la notch. Elles ne quittent pas ton PC.

---

## English

SpaceNotch has no account, no telemetry and no ads. Nothing is sent to measure how it is used. Here is
every connection the app can make, and when.

| Service | When | What is sent |
|---|---|---|
| GitHub (`api.github.com`, `github.com`) | **by default**, every 6 h, unless updates are “Off” | a request for the latest release, with your version number; then the installer download |
| Open-Meteo | only if you set a city for the weather | the city name, then its coordinates |
| LRCLIB (`lrclib.net`) | only if lyrics are turned on | the artist, title, album and length of the playing track |
| Spotify | only if you connect your Spotify account | what Spotify's authorisation asks for, with your own developer app |
| Discord | only if you turn it on | a **local** connection to the Discord app on your PC, nothing over the Internet |
| Web search | only when you run a web search from the notch | your query, sent by your browser to the engine you chose (Bing, Google or DuckDuckGo) |
| Assistant | off by default | local rules or an on-device model; with your own Claude key, your request goes to Anthropic |
| Share to phone | only when you share a file | a transfer **on your local network**, for the time of the send |

**On your PC, and nowhere else:**
- settings, in `%AppData%\SpaceNotch`;
- the log, in `%LocalAppData%\SpaceNotch\logs` (it can contain file paths: read it before sharing it);
- notes and reminders;
- clipboard history, which is off by default, keeps nothing on disk and skips passwords.

**Windows notifications.** If you allow it, SpaceNotch reads your notifications to show them in the
notch. They never leave your PC.
