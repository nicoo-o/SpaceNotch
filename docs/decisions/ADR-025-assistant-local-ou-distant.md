# ADR-025 — Assistant : règles locales d'abord, modèle sur l'appareil ou Claude au choix

- **Statut** : accepté (vague 6c, octobre 2026)
- **Contexte** : la vague 6c ajoute de l'intelligence à la notch : le résumé des
  notifications retenues (I1), une commande en langage naturel dans le lanceur (I2), des
  actions sur un texte copié (I3). Un modèle de langage aide, mais il coûte : une
  capacité de manifeste, une dépendance, et — s'il est distant — du texte qui quitte la
  machine. Il faut décider ce qui marche sans lui, quel modèle, et ce qu'on lui envoie.

## Décision 1 — Tout fonctionne sans modèle

- Le résumé est d'abord fait de règles (`NotificationDigest`) : une notification qui te
  nomme, qui te pose une question ou qui dit « urgent » remonte ; une notification
  automatique (« no-reply », « newsletter ») descend. Trois lignes importantes au plus,
  le reste groupé par application.
- La grammaire (`NaturalCommand`) comprend sans réseau « rappelle-moi d'appeler Paul à
  17 h », « dans 20 min », « demain matin », « ne pas déranger jusqu'à 15 h »,
  « minuteur 10 min », « volume 30 ». Une échéance plus lointaine que sept jours est
  refusée plutôt que devinée.
- Le rappel tiré d'un texte copié (« avant vendredi » → jeudi 17 h) est aussi fait de
  règles. Seuls traduire, résumer et répondre demandent un modèle ; sans lui, ces
  boutons n'apparaissent pas.
- Le réglage par défaut est **Aucun**.

## Décision 2 — Deux modèles possibles, choisis dans les réglages

- **Windows (sur l'appareil)** : Phi Silica, par `Microsoft.Windows.AI.Text.LanguageModel`
  du Windows App SDK. Rien ne quitte la machine. Disponible seulement sur les PC Copilot+ ;
  ailleurs, les réglages le disent et les règles répondent. Cette API exige la capacité
  `systemAIModels` dans le manifeste d'identité (`packaging/identity/AppxManifest.xml`,
  espace de noms `systemai`). C'est la seule capacité ajoutée, et elle ne donne accès
  qu'aux modèles fournis par Windows.
- **Claude** : par le SDK officiel `Anthropic` (NuGet), avec la clé de l'utilisateur. Le
  modèle par défaut est `claude-opus-5-5`, avec un effort bas (`effort: low`) : les
  réponses attendues sont courtes. Le modèle peut être changé dans les réglages (nom
  validé : lettres, chiffres, `-`, `.`, `_`, 64 caractères au plus).
- La clé est rangée dans le coffre de Windows (`PasswordVault`, ressource
  `SpaceNotch.Assistant.Claude`), **jamais** dans `settings.json`. « Retirer » l'efface.

## Décision 3 — Ce qui est envoyé, et comment

- Un modèle n'est appelé que sur un geste : la sortie du silence (I1, une phrase de
  synthèse au-dessus du résumé déjà affiché), Entrée sur « Demander à … » (I2), un clic
  sur Traduire / Résumer / Répondre (I3). Rien n'est envoyé en arrière-plan.
- Le texte envoyé est le strict nécessaire : les notifications retenues (application,
  expéditeur, début du texte), la phrase tapée, ou le texte copié. Ce qu'un gestionnaire
  de mots de passe marque n'est jamais lu (`ExcludeClipboardContentFromMonitorProcessing`).
- Le texte est **encadré comme donnée** dans l'invite (`<notifications>`, `<copied>`) et
  la consigne dit de ne pas suivre d'instruction qui s'y trouverait.
- La réponse est validée avant usage : une phrase nettoyée et bornée pour le résumé ; un
  JSON dont le genre, la date (≤ 7 jours) et le niveau sont vérifiés pour I2 — une
  réponse invalide donne « Pas compris », jamais une action. Une réponse ne déclenche
  jamais rien d'autre que ce que la grammaire locale saurait faire.
- Les actions sur copie (I3) sont une fonctionnalité **désactivée par défaut** : tant
  qu'elle n'est pas allumée, le presse-papier n'est pas lu pour elles.

## Conséquences

- Le paquet grossit du SDK `Anthropic` (et de ses dépendances) ; il n'est chargé qu'au
  premier appel à Claude.
- Un refus de Claude (`stop_reason: refusal`), une erreur réseau ou une clé invalide
  donnent « Pas de réponse » dans la notch et une ligne dans le journal, sans la clé.
- Les rappels sont écrits dans `%LOCALAPPDATA%\SpaceNotch\reminders.json` (50 au plus).
