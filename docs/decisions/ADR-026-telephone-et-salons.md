# ADR-026 — Téléphone, salons vocaux, paroles et miroir : un service, une porte

- **Statut** : accepté (vague 6d, octobre 2026)
- **Contexte** : la vague 6d fait entrer dans la notch ce qui se passe hors du PC ou
  dans d'autres applications : un appel ou une livraison sur le téléphone (T1, T2), le
  salon vocal Discord (T3), les paroles et la bibliothèque Spotify (T4), la webcam
  avant une réunion (W5). Chaque service a sa porte d'entrée et ses règles. Cet ADR les
  fixe une par une.

## T1, T2 — Lien avec Windows : lire, jamais parler au téléphone

- SpaceNotch ne se connecte pas au téléphone. Il lit les notifications que Windows
  reçoit déjà de **Lien avec Windows** (et des applications de livraison), avec l'accès
  aux notifications déjà accordé (ADR-023). **Aucune capacité nouvelle.**
- Un appel (« Appel entrant », « Incoming call »…) ouvre une carte verte. **Répondre**
  ouvre Lien avec Windows (`ms-phone:calling`) : c'est lui qui décroche. La notch
  compte ensuite la durée dans une pastille. Raccrocher ouvre Lien avec Windows : la
  notch ne raccroche pas à la place du téléphone.
- Une livraison ou un VTC (Uber Eats, Deliveroo, Uber, Bolt, colis…) est reconnu par
  le nom du service, puis par des mots-clés d'étape (en préparation, en route,
  arrivé). L'heure d'arrivée est lue dans le texte (« dans 12 min », « à 19:42 »). Une
  promotion sans étape est ignorée.
- Les notifications comprises ne s'affichent pas une seconde fois.

## T3 — Discord : le RPC local, avec l'application de l'utilisateur

- Le client de bureau Discord écoute sur le tube `\\.\pipe\discord-ipc-N`. Le salon
  vocal se lit par `GET_SELECTED_VOICE_CHANNEL` et les évènements `VOICE_STATE_*` et
  `SPEAKING_*`. Le micro se coupe par `SET_VOICE_SETTINGS`.
- Ces commandes exigent un jeton OAuth2 avec `rpc`, `rpc.voice.read` et
  `rpc.voice.write`. Discord réserve ces permissions aux applications qu'il admet,
  ou à leurs testeurs. SpaceNotch n'embarque donc **ni identifiant ni secret**.
  L'utilisateur crée sa propre application sur discord.com/developers, avec
  l'adresse `http://localhost`. Il colle son identifiant et son secret dans les
  réglages. Discord affiche ensuite une fenêtre d'accord, une seule fois.
- Le secret et le jeton sont gardés dans le coffre de Windows (`PasswordVault`). Seul
  l'échange du code contre un jeton sort de la machine, vers `discord.com`.
- Une identicône par personne : allumée quand elle parle, estompée sinon.

## T4 — Paroles (LRCLIB) et Spotify (API Web, PKCE)

- **Paroles synchronisées** : désactivées par défaut. Une fois allumées, une requête par
  morceau part vers `lrclib.net`, base libre et sans clé. Elle porte l'artiste, le
  titre, l'album et la durée, rien d'autre. La réponse est gardée en mémoire (30
  morceaux).
- **Spotify** : « J'aime » et la file d'attente passent par l'API Web, avec
  l'application que l'utilisateur déclare chez Spotify, à l'adresse
  `http://127.0.0.1:43117/spotify/callback`. La connexion se fait en **PKCE** : il n'y
  a aucun secret. Seul le jeton de renouvellement est gardé, dans le coffre ; le
  jeton d'accès reste en mémoire. Le lecteur de Windows ne donne pas l'identifiant
  Spotify du morceau : la notch le cherche par titre et artiste.
- **Portées demandées** : `user-library-read`, `user-library-modify` (« J'aime »),
  `user-read-playback-state` (« Ensuite : … ») et, depuis la v1.12.0,
  `user-modify-playback-state` pour le bouton « file » du mode paroles, qui ajoute le
  morceau à la file de lecture. Un compte connecté avant la v1.12.0 doit se
  reconnecter une fois : sans cette portée, Spotify refuse l'ajout et la notch
  affiche « échec ».
- **Mode paroles** : quand un morceau a des paroles, la carte média les montre en
  grand (la ligne chantée, la suivante en gris, qui remontent). Un clic sur les
  paroles rend les contrôles ; le bouton « Paroles » y revient. Le choix est gardé
  d'un morceau à l'autre.

## W5 — Le miroir : la webcam, et elle seule

- Au survol de **Rejoindre** sur la carte d'une réunion, la webcam s'affiche dans la
  notch (image retournée, comme un miroir), avec l'état du micro. Cet état est lu
  par CoreAudio sans ouvrir le micro. La caméra est rendue dès que le pointeur s'en va.
  Rien n'est enregistré ni envoyé.
- La caméra demande la capacité **`webcam`** dans le manifeste d'identité, et Windows
  demande l'accord à la première utilisation. **Pas de capacité `microphone`** : lire
  si le micro est coupé n'en demande pas.
- Le miroir peut être coupé dans les réglages.

## Conséquences

- Une seule capacité ajoutée : `webcam`.
- Trois services en ligne, chacun désactivé tant que l'utilisateur ne l'a pas voulu :
  LRCLIB, Spotify et l'échange de jeton Discord.
- Si Discord change son RPC (il l'a déjà restreint), la carte vocale ne s'affiche plus
  et rien d'autre ne casse.
