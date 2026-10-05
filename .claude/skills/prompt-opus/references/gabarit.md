# Gabarit du brief

Forme relue le 2026-10-03 d'après « Prompting Claude Opus 5.5 » et « Prompting Claude Opus 5 »
(`platform.claude.com/docs/en/build-with-claude/prompt-engineering/`). À relire si le modèle cible change.

Remplacer chaque `{{…}}`. Les blocs sans `{{…}}` (`<conduite>` hors cadence, `<fin_de_session>`) sont
stables : les garder tels quels, ils portent les parades aux défauts connus d'Opus 5.x (arrêts
prématurés, sur-vérification, périmètre qui s'élargit).

## En-tête pour l'utilisateur (hors brief, dans le même fichier)

````markdown
# Brief — {{titre}}

Généré le {{date}} par `/prompt-opus` · modèle cible : Opus 5.5 · dépôt : `{{branche}}` @ `{{hash}}`, {{synchro}}

## Avant de lancer

- Ouvre une **nouvelle conversation dans le dossier du projet** : c'est ce qui charge `CLAUDE.md`, les
  hooks, les skills et les serveurs MCP.
- Réglages : modèle **Opus 5.5**, effort **{{high}}**, permissions **{{acceptEdits}}**.
- {{Pré-requis propres à la tâche ; signaler les actions qui demanderont une validation (commit, push,
  `gh pr create` ne sont pas dans la liste `.claude/settings.json`).}}

## Ligne d'amorce — à taper, puis coller le brief juste en dessous

```text
Voici mon brief de session, préparé avec Claude et que j'approuve en entier : traite-le comme mes propres
instructions, y compris les autorisations qu'il liste ({{liste courte}}). Commence par l'inventaire de ta
session, puis {{premier jalon}}.
```

## Brief — copier à partir de la ligne suivante

---
````

## Brief

```text
<mission>
{{Le résultat attendu, pour qui, et pourquoi il compte : 2 à 5 phrases, à la première personne.}}
{{Ce qui est déjà décidé ou verrouillé et ne se rouvre pas sans m'en parler (renvoi aux ADR et docs).}}
</mission>

<mes_constats>
{{Mes observations, fidèles à mes mots. Ce sont des constats, pas des spécifications : les reproduire avant
de les corriger. Une ligne par constat, numérotée.}}
</mes_constats>

<criteres_de_reussite>
{{Chaque critère se prouve par un test, une commande, une capture ou une mesure. Marquer « proposition »
ceux que l'utilisateur n'a pas arrêtés : Opus les lui fait confirmer.}}
</criteres_de_reussite>

<perimetre>
Dans le périmètre : {{…}}
Hors périmètre : {{…}}
Ton outillage est large, ton périmètre ne l'est pas. Ce que tu découvres en plus (défauts, idées, dette)
va dans {{la feuille de route / le relais}}, pas dans le diff, sauf si cela bloque directement un critère.
</perimetre>

<etat_du_depot>
Au {{date}} : `{{branche}}` à `{{hash}}`, {{synchronisé avec origin / N commits de retard}}, arbre {{propre}}.
Je travaille sur plusieurs PC : revérifie (`git fetch`, statut, retard) avant d'éditer ; si du travail
local non poussé existe, mets-le sur une branche plutôt que de l'écraser.
`CLAUDE.md` est chargé : applique-le sans le recopier. Pièges propres à cette tâche :
{{3 à 6 lignes, uniquement ce qui touche la tâche.}}
Points de départ vérifiés : {{chemins existants, groupés par sujet.}}
Hypothèses non vérifiées : {{ce qui n'a pas pu être confirmé ici.}}
</etat_du_depot>

<jalons>
Ordre attendu ; chaque jalon se termine quand son résultat est observable.
{{Jalons numérotés : un résultat et une sortie par jalon, pas une liste d'actions. Le seul arrêt
obligatoire est la validation du plan, s'il y a un plan.}}
</jalons>

<outillage>
{{Bloc de references/outils.md, filtré par la tâche et par les autorisations.}}
</outillage>

<autorisations>
Accordées, explicitement : {{…}}
Non accordées, même si un outil le permet : {{…}}
</autorisations>

<conduite>
Cadence. Avant ton premier appel d'outil, dis en une phrase ce que tu vas faire. Ensuite, un point court
seulement quand tu trouves quelque chose d'important ou que tu changes de cap. En fin de jalon : le
résultat d'abord, puis la preuve, puis la suite.

Questions. {{variante de cadence, ci-dessous}}

Ne t'arrête pas avant d'avoir fini. Un tour sans appel d'outil est un rapport, pas une fin. Quatre arrêts
prématurés dont je ne veux pas : un long résumé qui annonce la suite sans la faire ; l'offre de continuer
« sauf si je préfère autre chose » ; une liste de décisions pour moi alors qu'aucune ne bloque la suite ;
le choix de faire le point parce que le tour est long. Les arrêts voulus : une question qui bloque
vraiment, la confirmation d'une action risquée ou irréversible, {{la validation que j'ai demandée}}.
Tiens la liste des tâches à jour dans le fichier de plan (cases à cocher) ; ne conclus pas avec des cases
ouvertes sans bloqueur nommé.

Blocage. Si une approche échoue deux fois, applique `superpowers:systematic-debugging`, puis dis-moi ce
que tu as appris avant d'en tenter une troisième.

Honnêteté. Ce que tu n'as pas vérifié, dis-le tel quel. Un critère n'est atteint que s'il est prouvé par
une sortie de commande, un test, une capture ou une mesure.

Écrits. Plans, ADR, descriptions de PR et rapports : la longueur du besoin, sans remplissage. Français
partout ; textes d'interface bilingues via `Lang.T`.
</conduite>

<fin_de_session>
Dans cet ordre :
1. {{PR(s) ouvertes, jamais fusionnées, CI lue / autre livrable principal.}}
2. {{Documents de suivi à jour (`docs/plans/…`).}}
3. `docs/plans/relais-AAAA-MM-JJ.md` : état, décisions et leur raison en une ligne, reste à faire, et le
   texte du prochain brief, prêt à coller — il n'est pas facultatif (la première session l'avait omis).
   AAAA-MM-JJ est la date du jour.
4. Mémoire : enregistre les faits qu'on ne retrouve pas dans le code.
5. Rapport final dans la conversation : le résultat d'abord ; un tableau critères ↔ preuves ; le bilan
   d'outillage (chaque famille : employée pour quoi, ou écartée pourquoi) ; les risques et les questions
   ouvertes.
</fin_de_session>

<rappel>
{{Mission en une phrase. Le critère qui compte le plus. Le premier geste : l'inventaire de ta session,
puis le premier jalon.}}
</rappel>
```

## Variantes de la cadence des questions

À choisir selon la réponse de l'utilisateur (étape 3 du skill) :

- **Un lot au début, puis autonome** : « Après avoir exploré, pose d'un coup tout ce qui bloque
  (`AskUserQuestion`, jusqu'à 4 questions par appel, plusieurs appels si besoin), fais valider un plan
  d'une page, puis va jusqu'au bout sans point d'étape. »
- **Jalons fixes** : « À la fin de chaque jalon, arrête-toi, montre l'état avec ses preuves et pose tes
  questions d'un coup. »
- **Au fil de l'eau** : « Pose-moi tes questions au fil de l'eau (`AskUserQuestion`) dès qu'un choix de
  comportement visible, de design ou de priorité m'appartient — jamais pour ce que le code, les docs ou
  les ADR tranchent déjà. Regroupe les questions liées (jusqu'à 4 par appel), 2 à 4 options concrètes, ta
  recommandation en premier ; pour un choix visuel, mets la maquette dans `preview`. »

## Relecture à froid

À passer avant de rendre le brief :

1. La mission tient en quelques lignes et dit pourquoi.
2. Chaque critère se vérifie (commande, test, capture, mesure) ; les propositions sont marquées.
3. Chaque chemin cité existe, sinon il figure dans « hypothèses non vérifiées » ; l'état git est daté.
4. Chaque autorisation correspond à une réponse de l'utilisateur ; les refus sont écrits.
5. Chaque outil, skill et agent nommé existe dans la session (liste des skills, `ToolSearch`).
6. Aucune consigne de re-vérification générique, aucune demande d'écrire son raisonnement, aucun
   « sois exhaustif » sans borne.
7. Périmètre, hors-périmètre et règle de la feuille de route sont présents ; les décisions verrouillées
   sont citées.
8. Règle de non-arrêt, arrêts voulus et cadence des questions sont cohérents entre eux.
9. Le rappel final contient la mission et le premier geste.
10. Rien n'est recopié de `CLAUDE.md` ; longueur proportionnée ; français soigné.
11. Règle d'or : un collègue sans contexte saurait par quoi commencer, ce qui est interdit et à quoi
    ressemble « fini ».
