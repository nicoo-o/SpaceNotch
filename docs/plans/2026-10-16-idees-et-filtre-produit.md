# Idées de développement et filtre de nécessité — SpaceNotch

> **Statut : propositions à tester, pas décisions de livraison.** Ce document prolonge l’[audit produit d’octobre](../audit/2026-10-audit-produit.md). Il propose une méthode pour empêcher qu’une idée séduisante devienne une fonctionnalité sans besoin établi. Aucun élément ci-dessous n’autorise à lui seul une modification du produit.

## Résumé

SpaceNotch a déjà un cœur suffisamment riche : média, recherche, retours système, notifications, minuteur, téléchargements, étagère et Pixel. La priorité n’est pas de lui ajouter un deuxième produit. Les propositions classées « proche » améliorent plutôt la découvrabilité, le contrôle des interruptions, la confiance, la prévisibilité et les usages existants. Les idées ambitieuses restent des paris à éprouver, non une feuille de route engagée.

**Recommandation initiale :** valider d’abord trois hypothèses à faible coût — l’onboarding apprend-il les gestes essentiels, les utilisateurs comprennent-ils et contrôlent-ils les interruptions, et une seule entrée Minuteur peut-elle couvrir les cas de focus ? Ne construire aucune idée ambitieuse avant d’avoir démontré un problème utilisateur et un avantage clair sur les fonctions présentes. Pour les piliers susceptibles d’être mis en avant publiquement, mesurer d’abord ce qui est réellement distinctif : une grande promesse n’est pas en soi une preuve de demande ni une feuille de route.

---

## 1. Méthode : décider avant de développer

### Étape 1 — La fiche de l’idée

Tout demandeur décrit l’idée en une phrase et répond à ces questions :

- **Pour qui ?** Débutant Windows, utilisateur général, personne qui utilise le clavier, développeur, etc.
- **Dans quelle situation ?** Un événement précis, pas « rendre l’application meilleure ».
- **Quel résultat manque aujourd’hui ?** Décrire ce que fait l’utilisateur et où il échoue ou perd du temps.
- **Qu’est-ce qui existe déjà ?** Fonction, réglage, raccourci ou greffon susceptible de couvrir le besoin.
- **Comment constater le problème ?** Observation, entretiens, retours, mesure de fréquence ; indiquer quand il s’agit seulement d’une intuition.

Si la personne, le moment d’usage ou la douleur ne sont pas identifiables, l’idée retourne à la liste d’attente pour clarification : pas de prototype produit.

### Étape 2 — Les portes éliminatoires

Une idée doit franchir **toutes** les portes avant d’être notée :

| Porte | Question | Échec : décision |
|---|---|---|
| Besoin | Existe-t-il un problème observable pour une population identifiée ? | Rejeter ou recueillir des preuves ; ne pas construire sur « ce serait sympa ». |
| Bonne surface | La notch est-elle meilleure qu’un raccourci, qu’un réglage Windows ou qu’une fenêtre dédiée ? | Rejeter si elle encombre une surface contextuelle pour un usage rare ou long. |
| Cohérence | L’idée respecte-t-elle la même matière, les états Compact/Preview/Expanded, le repos calme et l’identité Pixel ? | Reconcevoir ; rejeter si elle transforme l’ensemble en dashboard ou ajoute de l’animation décorative. |
| Consentement | Capteur, presse-papier, réseau, compte ou modèle : les données et le déclenchement sont-ils explicites et proportionnés ? | Aucun test avec données réelles ni développement avant d’avoir un accord et un comportement privé par défaut. |
| Recouvrement | Peut-on d’abord simplifier, réunir ou mieux exposer une fonction existante ? | Reporter l’ajout tant que ce recouvrement n’est pas compris. |
| Fin de vie | La fonction s’arrête-t-elle proprement, rend-elle la main, et peut-elle être désactivée sans résidu ? | Pas de mise en production si l’arrêt, l’annulation ou la désactivation n’est pas définissable. |

Ces portes sont des garde-fous, pas des points bonus : un score élevé ne les compense pas.

### Étape 3 — Score de tri sur 8

Après les portes, noter **quatre critères de 0 à 2**, en citant la preuve ou en marquant le jugement comme hypothèse :

| Critère | 0 | 1 | 2 |
|---|---|---|---|
| **Besoin** | Cas inventé ou exceptionnel | Besoin plausible, preuve indirecte | Problème répété, retours ou observation directe |
| **Adéquation** | Notch inadaptée | Contexte utile mais ouverture ou détour nécessaire | Information/action immédiatement utile dans la surface contextuelle |
| **Levier** | Nouvelle complexité, peu de bénéfice | Améliore un scénario ou plusieurs | Simplifie une fonction, un geste ou plusieurs parcours existants |
| **Différenciation** | Copie générique, aucun avantage SpaceNotch | Usage cohérent sans avantage démontré | Renforce la forme native, locale, événementielle ou l’identité propre du produit |

**Règle de score :**

- **6–8 / 8 :** autoriser un test léger, pas encore l’implémentation complète.
- **4–5 / 8 :** conserver en réserve ; rechercher d’abord les preuves manquantes.
- **0–3 / 8 :** écarter pour l’instant.
- **Tout zéro sur Besoin ou Adéquation :** pas de développement même si le total atteignait autrement le seuil.

Le score est un filtre qualitatif structuré, pas une mesure scientifique. Il ne mesure ni l’effort ni le risque : ils sont écrits séparément pour empêcher une bonne note de cacher un coût de maintenance ou de sécurité.

### Étape 4 — Test léger, seuil et arrêt

Chaque proposition retenue doit annoncer **avant son test** :

1. **Hypothèse** : résultat attendu, formulé de manière falsifiable.
2. **Risque principal** : confusion, interruption, confidentialité, accessibilité, dépendance ou coût au repos.
3. **Test le moins cher** : entretien court, maquette, scénario papier, mesure d’usage existant, prototype jetable ou test de compatibilité — selon le besoin. Pas de sous-système de production pour valider une intuition.
4. **Seuil de réussite** : valeur observable et dénominateur prévu. Exemples : proportion de participants qui trouvent l’action sans aide, tâches complétées sans erreur, diminution d’un détour, absence d’accès réseau avant opt-in.
5. **Condition d’arrêt** : signal qui ferait abandonner ou reformuler l’idée.
6. **Décision après test** : poursuivre vers conception détaillée, recueillir plus de preuves, reformuler ou fermer la piste.

**Règle de passage :** score 6–8 + portes franchies + seuil de test atteint sont nécessaires pour envisager une conception de livraison. Ils ne constituent pas une autorisation implicite de modifier le code, publier ou activer une collecte.

### Fiche courte réutilisable

```text
Idée :
Public et situation :
Problème actuel / preuves :
Doublon à traiter avant ajout :
Portes : besoin [oui/non] · bonne surface [oui/non] · cohérence [oui/non]
         · consentement [oui/non] · recouvrement [oui/non] · fin de vie [oui/non]
Score : besoin __/2 · adéquation __/2 · levier __/2 · différenciation __/2 = __/8
Hypothèse :
Risque principal :
Test léger :
Seuil pour poursuivre :
Condition d’arrêt :
Décision après test :
```

---

## 2. Idées proches — améliorer avant d’élargir

Les notes ci-dessous sont des **premières estimations de discussion**, pas des validations. Le dépôt établit l’existence des fonctionnalités ; il n’établit pas leur fréquence réelle d’utilisation. Les tests proposés servent à vérifier le besoin avant toute conception détaillée.

### P01 — Parcours de première utilisation en trois gestes

- **Classement proposé :** améliorer l’existant · proche · **6/8** (besoin 2, adéquation 2, levier 1, différenciation 1).
- **Hypothèse :** une présentation centrée sur ouvrir/fermer, recherche et réglages permet à un nouvel utilisateur d’agir sans aller lire le README.
- **Risque :** transformer l’onboarding en séquence forcée et allonger le premier démarrage.
- **Test léger :** maquette ou test modéré avec six personnes qui découvrent le produit ; leur demander de chercher, fermer et retrouver l’aide sans instruction.
- **Seuil :** au moins 5/6 trouvent ces trois actions sans aide ; si deux personnes ou plus sont bloquées au même geste, corriger ce geste avant d’ajouter des pages.
- **Arrêt :** si la plupart des participants préfèrent une découverte silencieuse et trouvent déjà les actions, ne pas ajouter d’étapes ; améliorer les indices contextuels.

### P02 — Politique d’interruptions compréhensible

- **Classement proposé :** simplifier l’existant · proche · **7/8** (2, 2, 2, 1).
- **Hypothèse :** quelques choix lisibles — laisser passer, attendre, silence — résolvent mieux les conflits d’activités que l’exposition de nombreux réglages isolés.
- **Risque :** créer une seconde machine de règles complexe ou masquer un appel urgent.
- **Test léger :** scénario papier avec musique, téléchargement, appel, minuteur et réunion ; demander à des utilisateurs ce qu’ils pensent que la notch va faire et ce qu’ils voudraient.
- **Seuil :** au moins 80 % prédisent le comportement choisi sur les scénarios retenus ; zéro scénario critique où l’appel ou l’action ouverte disparaît sans explication.
- **Arrêt :** si les profils ne sont pas compris ou ne changent aucune décision, ne pas ajouter de profils ; clarifier d’abord les priorités actuelles.

### P03 — Unifier Minuteur, chronomètre et focus

- **Classement proposé :** fusionner · proche · **7/8** (2, 2, 2, 1).
- **Hypothèse :** une seule entrée « Minuteur » avec modes Minuteur, Chronomètre et Focus réduit la confusion tout en préservant les scénarios existants.
- **Risque :** casser les raccourcis, habitudes ou états persistés du Pomodoro.
- **Test léger :** maquette de la tuile et des modes ; demander à des utilisateurs de lancer 10 minutes, un chronomètre puis un focus avant réunion.
- **Seuil :** au moins 5/6 choisissent le mode correct sans explication et ne confondent pas pause, fin et remise à zéro.
- **Arrêt :** si les utilisateurs décrivent des besoins réellement distincts et fréquents, conserver des entrées séparées mais clarifier leur libellé plutôt que fusionner de force.

### P04 — Une seule porte claire pour le presse-papier

- **Classement proposé :** simplifier avec opt-in conservé · proche · **6/8** (2, 1, 2, 1).
- **Hypothèse :** regrouper historique, signal Copié et actions facultatives sous un seul réglage compréhensible évite deux surveillances que l’utilisateur pourrait croire séparées.
- **Risque :** modifier les attentes de confidentialité ; actions IA pourraient être prises à tort pour automatiques.
- **Test léger :** comparer deux descriptions de réglage avec un scénario de copie ; demander quelles données sont lues, stockées et envoyées.
- **Seuil :** 100 % des personnes testées comprennent qu’aucune capture ou requête IA ne se fait avant activation et geste explicites.
- **Arrêt :** si cette compréhension ne peut être assurée par une seule porte, conserver des réglages séparés et mieux expliquer leur indépendance.

### P05 — Mode avancé sans options mortes ni surprises

- **Classement proposé :** améliorer les réglages · proche · **7/8** (2, 2, 2, 1).
- **Hypothèse :** trois à cinq profils de haut niveau puis les réglages fins en Avancé rendent l’app plus accessible sans retirer le contrôle expert.
- **Risque :** cacher une option réellement nécessaire ou conserver dans le profil un choix que l’utilisateur a fait explicitement.
- **Test léger :** tri de cartes des réglages existants et maquette de navigation ; inclure les novices et une personne habituée aux réglages détaillés.
- **Seuil :** 80 % des réglages les plus courants sont retrouvés en moins de 30 secondes, et tous les participants savent où se trouvent les réglages avancés.
- **Arrêt :** si les regroupements ne réduisent pas le temps de recherche ou si les profils surprennent, ne pas remanier toute la fenêtre ; corriger seulement les cartes problématiques.

### P06 — Contrôle global de présence et de mouvement

- **Classement proposé :** améliorer le contrôle utilisateur · proche · **6/8** (2, 2, 1, 1).
- **Hypothèse :** un contrôle rapide et mémorisable permettant d’apaiser la notch sans désactiver toutes ses fonctions réduit les interruptions en réunion ou en concentration.
- **Risque :** dupliquer Ne pas déranger, le plein écran, le réglage de mouvement et l’état de Pixel.
- **Test léger :** maquette de quatre options existantes et test de compréhension ; ne pas ajouter de nouveau mode avant d’avoir distingué silence, visibilité et animation.
- **Seuil :** au moins 5/6 identifient correctement quel choix coupe les sons, les animations, les notifications ou toute présence.
- **Arrêt :** si les utilisateurs assimilent les options ou si les états existants couvrent le scénario, mieux nommer/rassembler ceux-ci sans ajouter un réglage.

### P07 — Compatibilité honnête pour les intégrations

- **Classement proposé :** documentation et robustesse · proche · **6/8** (2, 1, 2, 1).
- **Hypothèse :** prérequis et états indisponibles explicités réduisent les erreurs de configuration pour Discord, calendrier, micro/caméra, partage et modèles.
- **Risque :** afficher trop de détails techniques à l’utilisateur général.
- **Test léger :** inventaire des états de chaque intégration, puis test de tâches où un prérequis manque (permission refusée, application absente, matériel non compatible).
- **Seuil :** 100 % des intégrations testées expliquent une indisponibilité et proposent une action réaliste ou un état final clair.
- **Arrêt :** si le message devient un texte technique que les testeurs ne comprennent pas, proposer une explication courte avec détails secondaires au lieu d’ajouter une page d’aide.

### P08 — Mode « Présentation / jeu » prévisible

- **Classement proposé :** consolider comportements existants · proche · **6/8** (2, 2, 1, 1).
- **Hypothèse :** un seul profil explicite qui règle visibilité et interruptions est plus prévisible que plusieurs automatismes séparés.
- **Risque :** masquer une activité importante ou modifier silencieusement les réglages normaux.
- **Test léger :** prototype sur scénarios plein écran, réunion et fenêtre agrandie, avec les signaux existants de Windows.
- **Seuil :** aucun événement non désiré n’ouvre l’interface dans les scénarios choisis, et un appel critique suit une règle confirmée par l’utilisateur.
- **Arrêt :** si les règles de présence actuelles couvrent déjà les cas, ne pas ajouter un mode global ; corriger les exceptions seulement.

### P09 — Étagère aux états et retours réversibles

- **Classement proposé :** approfondir la fonction signature · proche · **6/8** (1, 2, 2, 1).
- **Hypothèse :** expliquer si l’étagère est temporaire et offrir annuler après retrait évite la surprise liée aux chemins qui disparaissent.
- **Risque :** introduire une persistance de chemins ou une promesse de conservation non voulue.
- **Test léger :** maquette des états session, fichier déplacé/supprimé, retirer et annuler ; entretien sur la signification d’« étagère ».
- **Seuil :** 5/6 comprennent ce qui survit à la fermeture et ne pensent pas que SpaceNotch copie ou déplace le fichier source.
- **Arrêt :** si une étagère éphémère est préférable et comprise, documenter ce fait et ne pas ajouter de stockage persistant.

### P10 — Maîtriser la promesse visuelle et les animations

- **Classement proposé :** validation d’expérience · proche · **5/8** (1, 2, 1, 1).
- **Hypothèse :** sur les machines réelles, la forme compacte reste lisible, le mouvement indique réellement le travail et la disparition n’est pas interprétée comme une panne.
- **Risque :** évaluer seulement les machines et sensibilités de l’équipe.
- **Test léger :** scénario court `--demo` sur plusieurs DPI et taux de rafraîchissement ; captures avant/après et commentaires de 5 observateurs non contributeurs.
- **Seuil :** aucune erreur géométrique ou lecture impossible sur les configurations retenues ; au moins 4/5 identifient correctement l’état signalé par les animations.
- **Arrêt :** si les observateurs interprètent couleur/mouvement de façon variable, privilégier un signe textuel ou un pictogramme constant et réduire la sémantique animée.

### P11 — Parcours Narrateur et clavier dans chaque scène

- **Classement proposé :** qualité et inclusion · proche · **8/8** (2, 2, 2, 2).
- **Hypothèse :** un parcours clavier et Narrateur de bout en bout révèle les scènes réellement inaccessibles que l’inspection du XAML ne suffit pas à identifier.
- **Risque :** confondre présence d’un nom Automation avec une interaction utilisable ; régressions de focus propres aux overlays.
- **Test léger :** script manuel par scénario : ouvrir la notch, naviguer, exécuter l’action, fermer, retrouver l’application de départ ; répéter avec Narrateur et mouvement réduit.
- **Seuil :** tous les parcours centraux terminent sans souris ni perte de focus, avec annonces compréhensibles, sans répétition à chaque seconde.
- **Arrêt :** ne pas fermer l’initiative sur un score ; corriger les blocages critiques, puis vérifier à nouveau sur les scènes et contrôles touchés.

### P12 — Réduire la friction de confiance à l’installation

- **Classement proposé :** distribution et confiance · proche · **8/8** (2, 2, 2, 2).
- **Hypothèse :** expliquer les demandes Windows, les permissions et les mises à jour avant l’action réduit les abandons sans cacher les avertissements.
- **Risque :** présenter comme « sécurisé » un exécutable non signé ou une empreinte provenant de la même release.
- **Test léger :** observation de quelques personnes qui installent en environnement de test ; noter les abandons, questions et interprétations de chaque dialogue.
- **Seuil :** toutes les demandes de permission sont correctement expliquées avant leur affichage ; 5/6 personnes terminent le parcours et comprennent ce que l’installation change.
- **Arrêt :** si l’obstacle est intrinsèquement la signature/l’UAC et ne peut être résolu par la copie, ne pas masquer l’avertissement ; faire de la distribution/signature le travail prioritaire.

### P13 — Centre d’activité local et historique éphémère

- **Classement proposé :** nouvelle capacité · exploratoire proche · **4/8** (1, 1, 1, 1).
- **Hypothèse :** retrouver une activité expirée sans maintenir une file permanente répond à une vraie perte de contexte.
- **Risque :** contredire la règle « la notch retourne au repos » et conserver des notifications privées.
- **Test léger :** scénario de storyboard après un téléchargement terminé et une notification manquée ; demander aux utilisateurs ce qu’ils chercheraient et quelle durée de conservation ils jugent acceptable.
- **Seuil :** au moins 4/6 rapportent une perte réelle récente et choisissent une durée limitée ; test de confidentialité compris par tous.
- **Arrêt :** si le besoin est rare ou la conservation inquiète, écarter le centre et améliorer seulement l’action de fin/notification native.

### P14 — Règles locales simples par type d’activité

- **Classement proposé :** approfondir l’arbitrage · exploratoire proche · **5/8** (1, 2, 1, 1).
- **Hypothèse :** choisir une fois « afficher brièvement / attendre / ne pas montrer » par grande famille est plus utile que régler une longue liste de priorités.
- **Risque :** devenir un gestionnaire d’automatisations généraliste et introduire des états incohérents.
- **Test léger :** maquette avec seulement quatre familles (système, média, notifications, travail) et quelques cartes de situation.
- **Seuil :** 5/6 configurent un comportement attendu en moins de 2 minutes et peuvent expliquer où va une activité mise en attente.
- **Arrêt :** si plus de quatre règles sont nécessaires au test, ou si les gens ne savent pas prédire le résultat, abandonner la personnalisation et améliorer le défaut global.

### P15 — Mode de concentration local, sans calendrier obligatoire

- **Classement proposé :** nouvelle présentation de fonctions existantes · ambitieux · **5/8** (1, 2, 1, 1).
- **Hypothèse :** un seul profil « Concentration » combinant minuteur de focus, silence des notifications et animation apaisée est plus simple que l’activation séparée des fonctions.
- **Risque :** dupliquer le mode Ne pas déranger, l’agenda et les réglages d’animation, ou faire taire un événement critique.
- **Test léger :** maquette d’une action « Démarrer le focus » avec aperçu précis de ce qui sera mis en silence ; rejouer les mêmes scénarios d’appel et réunion que P02.
- **Seuil :** les utilisateurs décrivent correctement ce qui est silencé et ce qui passera ; aucune surprise critique pendant le test.
- **Arrêt :** si les participants interprètent différemment le périmètre, ne pas implémenter de profil ; conserver le minuteur et commandes explicites séparés.

### P16 — Pixel contextuel mais apaisable

- **Classement proposé :** approfondir l’identité · ambitieux · **4/8** (1, 1, 1, 1).
- **Hypothèse :** Pixel peut être affectueux et identifiable sans être ressenti comme surveillance ou activité continue intrusive.
- **Risque :** contredire une décision de direction déjà assumée (ADR-029) ou enlever la personnalité à ceux qui l’aiment.
- **Test léger :** comparateur non fonctionnel entre intensités Calme/Normal/Expressif et mouvement réduit, avec avis de personnes sensibles aux animations ; aucune modification automatique du réglage.
- **Seuil :** les testeurs trouvent l’état agent lisible dans les trois modes, et les personnes ayant besoin de réduire les animations perçoivent effectivement une pose calme.
- **Arrêt :** si le calme rend l’état ambigu, ne pas supprimer Pixel ; ajouter un indicateur statique explicite ou documenter précisément le comportement existant.
- **Réserve :** l’absence de budget CPU au repos est un choix explicite de l’auteur ; cette idée ne permet pas de renverser seule ADR-029.

### P17 — Indicateur intelligent de disponibilité des fonctions

- **Classement proposé :** réduire la confusion des intégrations · ambitieux · **5/8** (1, 2, 1, 1).
- **Hypothèse :** indiquer « prêt », « permission requise », « appareil non pris en charge » pour les fonctionnalités avancées éviterait des options muettes.
- **Risque :** faire une page d’état technique trop complexe et lier l’interface à une télémétrie non souhaitée.
- **Test léger :** prototype pour trois cas réels : luminosité indisponible, notifications non autorisées, Discord pas configuré.
- **Seuil :** 5/6 trouvent l’action de réparation sans assistance et savent distinguer indisponible, désactivé et en erreur.
- **Arrêt :** si un diagnostic n’offre aucune action que l’utilisateur peut entreprendre, remplacer l’état détaillé par une explication courte ou ne rien afficher.

### P18 — Écosystème de greffons plus facile à vérifier

- **Classement proposé :** investissement développeurs · ambitieux · **4/8** (1, 1, 1, 1).
- **Hypothèse :** un outil local qui valide manifestes, signatures, droits et cycle de vie réduit le coût d’essai des greffons sans exécuter ceux qui échouent.
- **Risque :** donner un faux sentiment d’isolation alors qu’un assemblage est chargé dans le processus de l’application.
- **Test léger :** entretiens avec 3 auteurs potentiels et exécution manuelle du processus d’installation d’un greffon de démonstration ; aucun nouveau gestionnaire intégré.
- **Seuil :** au moins deux auteurs terminent la validation plus facilement et identifient clairement ce qui n’est pas sandboxé.
- **Arrêt :** si aucun public tiers ne demande le flux ou si l’isolation reste mal comprise, améliorer uniquement la documentation et les exemples existants.

### P19 — Extension multi-écrans et modes de placement plus intelligents

- **Classement proposé :** nouvelle ergonomie · ambitieux · **4/8** (1, 1, 1, 1).
- **Hypothèse :** les configurations multi-écrans et barre des tâches en haut représentent une friction assez fréquente pour justifier d’autres décisions automatiques.
- **Risque :** rendre le placement imprévisible, contredire TopAttached ou provoquer le déplacement de la notch sans consentement.
- **Test léger :** recueillir des cas concrets et mesurer l’échec des scénarios de placement déjà documentés ; storyboard de recommandation sans déplacement automatique.
- **Seuil :** au moins 4/6 utilisateurs multi-écrans ont une difficulté reproductible non couverte par les options existantes, et choisissent la recommandation proposée sans hésitation.
- **Arrêt :** si le besoin est déjà couvert par les bords/écrans actuels ou que les participants préfèrent le choix explicite, ne pas automatiser.

### P20 — Rapport de diagnostic partageable, local et expurgé

- **Classement proposé :** supportabilité · ambitieux · **5/8** (1, 1, 2, 1).
- **Hypothèse :** un bouton qui prépare localement un rapport expurgé réduit les allers-retours de support tout en laissant l’utilisateur revoir le contenu.
- **Risque :** exposer chemins, noms d’applications, extraits de notifications ou informations de machine ; le journal peut contenir des données personnelles.
- **Test léger :** produire un modèle papier du rapport à partir de journaux fictifs et demander aux testeurs d’identifier les données avant export.
- **Seuil :** aucun champ sensible ne sort sans aperçu et accord explicites ; tous les participants savent supprimer le rapport ou annuler.
- **Arrêt :** s’il est impossible d’expurger et de justifier chaque champ, ne pas construire l’export ; garder la procédure manuelle de lecture du journal.

### P21 — Météo plus lisible, sans transformer la notch en application météo

- **Classement proposé :** améliorer l’existant · proche · **6/8** (2, 2, 1, 1).
- **Hypothèse :** afficher au repos une synthèse compacte — température, condition compréhensible et évolution prochaine — est plus utile que l’icône animée et la température seules.
- **Risque :** charger la surface de chiffres peu fiables, ajouter des requêtes fréquentes, ou faire croire à une précision supérieure à celle de la prévision.
- **Test léger :** comparer trois maquettes (température seule ; température + condition ; température + tendance) avec six personnes ; inclure état jour/nuit et données périmées/hors ligne.
- **Seuil :** au moins 5/6 choisissent correctement la carte correspondant à leur question (« dois-je prendre un parapluie ? ») en moins de 5 secondes ; chacun distingue observation actuelle et prévision.
- **Arrêt :** si l’ajout de tendance ne change pas une décision ou rend le compact illisible, garder la carte actuelle et améliorer uniquement le libellé/état de fraîcheur.
- **Garde-fou :** ville saisie explicitement, pas de localisation automatique, pas d’alerte météo sans consentement séparé ; conserver le comportement sans ville = aucun appel réseau.

### P22 — Espace développeur qui complète le canal local

- **Classement proposé :** regrouper les outils avancés · ambitieux · **5/8** (1, 1, 2, 1).
- **Hypothèse :** une porte d’entrée « Développeur » qui rend visibles le canal local, les hooks installés, les builds et les diagnostics aide les utilisateurs techniques sans exposer ces contrôles dans le parcours grand public.
- **Risque :** dupliquer le canal `--progress`/`--hook`, devenir un cockpit technique, ou laisser croire qu’un canal local permet à la notch d’exécuter des commandes.
- **Test léger :** inventaire de ce qui existe déjà puis maquette de navigation avec cinq développeurs ; leur demander de lancer un build de démonstration, trouver les logs et retirer les hooks sans aide.
- **Seuil :** 4/5 terminent les trois tâches en moins de deux minutes et distinguent clairement suivi d’activité, réglage de diagnostic et exécution de code.
- **Arrêt :** si la maquette n’est qu’un regroupement visuel de réglages déjà trouvables, ne pas créer un nouveau mode ; améliorer les raccourcis et la documentation existants.
- **Garde-fou :** pas de shell intégré, pas de commande arbitraire lancée par une activité, pas de télémétrie distante ; l’approbation d’un greffon ne doit pas être décrite comme une sandbox.

### P23 — Aperçu de batterie avec autonomie estimée

- **Classement proposé :** nouvelle information système · proche · **5/8** (1, 2, 1, 1).
- **Hypothèse :** le niveau et une estimation prudente du temps restant aident un utilisateur de portable à agir sans ouvrir les paramètres Windows.
- **Risque :** estimation volatile ou trompeuse, réveils supplémentaires, et signal qui se superpose au HUD de charge existant.
- **Test léger :** maquette compacte et examen des API/mesures Windows disponibles ; comparer les décisions des testeurs avec pourcentage seul et avec estimation, sur un jeu de scénarios synthétiques.
- **Seuil :** 5/6 comprennent que l’autonomie est estimée (pas garantie) et choisissent la vue enrichie ; la valeur reste masquée si Windows ne fournit pas un état fiable.
- **Arrêt :** si la durée estimée ne change pas la décision ou varie de manière déconcertante, n’ajouter aucun suivi périodique ; garder uniquement les événements de branchement/débranchement.

### P24 — Changer de sortie audio depuis la notch

- **Classement proposé :** approfondir le HUD audio · exploratoire · **4/8** (1, 1, 1, 1).
- **Hypothèse :** basculer rapidement entre casque, haut-parleurs et écran HDMI est un besoin assez fréquent pour justifier une action dans la notch.
- **Risque :** dupliquer l’interface native Windows, afficher une liste longue ou provoquer une coupure audio inattendue.
- **Test léger :** observer six utilisateurs lorsqu’ils changent de sortie dans Windows ; leur présenter ensuite une maquette d’un sélecteur limité aux appareils actifs.
- **Seuil :** au moins 4/6 déclarent le changement assez fréquent et réussissent la tâche plus vite dans la maquette ; ils anticipent clairement quelle sortie sera activée.
- **Arrêt :** si l’usage est rare, si la liste est ambiguë ou si l’OS est déjà aussi rapide, ne pas construire de sélecteur.

### P25 — Pause temporaire et visible du presse-papier

- **Classement proposé :** renforcer une fonction sensible · proche · **6/8** (2, 1, 2, 1).
- **Hypothèse :** une commande « Suspendre 15 min » ou « jusqu’à réactivation » rend l’historique plus rassurant quand l’utilisateur manipule des secrets.
- **Risque :** faire croire à tort que le presse-papier est suspendu alors qu’une autre fonction (actions sur copie ou application tierce) lit toujours les données.
- **Test léger :** storyboard pour historique et actions sur copie séparés ; demander ce que l’état « pause » arrête, et tester avec données fictives clairement balisées.
- **Seuil :** tous les participants peuvent nommer précisément les sources suspendues ; les deux parcours arrêtent réellement leurs écouteurs respectifs et l’indicateur de pause est visible.
- **Arrêt :** si l’application ne peut garantir l’arrêt de toutes les sources ou si la portée prête à confusion, ne pas utiliser un contrôle global ; garder des opt-ins indépendants avec états visibles.

### P26 — Heure des rendez-vous dans plusieurs fuseaux horaires

- **Classement proposé :** améliorer calendrier et réunions · exploratoire · **4/8** (1, 1, 1, 1).
- **Hypothèse :** les personnes qui organisent des réunions internationales veulent éviter de convertir mentalement l’heure locale et celle de l’interlocuteur.
- **Risque :** complexifier une carte compacte et afficher une conversion incorrecte lors d’un changement d’heure ou d’une réunion sans fuseau déterminé.
- **Test léger :** entretien avec six personnes qui ont eu récemment une réunion internationale ; maquette d’une seule heure locale et d’un seul fuseau secondaire, sans code.
- **Seuil :** au moins 4/6 racontent une erreur ou un détour fréquent et choisissent une présentation à deux heures sans difficulté de lecture.
- **Arrêt :** si le problème est occasionnel ou si les utilisateurs se fient déjà à leur calendrier, ne pas ajouter de conversion à la notch.

### P27 — Capsules de workflow contextuelles

- **Classement proposé :** nouvelle capacité d’orchestration · pilier ambitieux · **6/8** (besoin 1, adéquation 1, levier 2, différenciation 2).
- **Hypothèse :** des sessions courtes, démarrées explicitement — « préparer ma réunion », « travailler sur ce projet » — qui réunissent seulement les activités SpaceNotch pertinentes (rendez-vous, minuteur, notes/fichiers choisis, progression d’agent) aident à passer à l’action puis à retrouver le contexte sans garder un tableau de bord ouvert.
- **Risque :** transformer la notch en lanceur de workflows opaque, déclencher une action ou exposer un fichier sans intention claire, ou promettre un contrôle de Windows Focus que les interfaces documentées ne permettent pas.
- **Test léger :** storyboard cliquable de deux capsules (réunion et projet) avec six utilisateurs ; comparer aux parcours manuels et à un simple lancement d’espace de travail. Aucun code d’orchestration ni accès aux fichiers réels pendant le test.
- **Seuil :** au moins 5/6 terminent la tâche sans aide, peuvent expliquer ce qui a été ouvert/activé et savent terminer ou annuler la capsule ; au moins 4/6 préfèrent le parcours coordonné au lancement groupé seul.
- **Arrêt :** si les utilisateurs n’y voient qu’un raccourci de lancement, si une étape surprend, ou si la valeur dépend d’une API non prise en charge, reformuler ou abandonner plutôt que bâtir un moteur d’automatisation.
- **Garde-fous :** capsule toujours démarrée et terminée par l’utilisateur ; aperçu des actions avant démarrage ; aucune lecture implicite de fichiers, commande shell, approbation d’agent ou modification de Ne pas déranger ; fermeture qui arrête les écouteurs propres à la capsule. [PowerToys Workspaces](https://learn.microsoft.com/en-us/windows/powertoys/workspaces) couvre déjà le lancement et le placement d’applications ; la différenciation à prouver est le contexte de travail de bout en bout, pas les dispositions de fenêtres. L’[API publique Windows Focus](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/applifecycle/focus-session) documente la lecture et les changements d’état, pas une commande de démarrage/arrêt.

### P28 — Écosystème d’activités tiers, ouvert et digne de confiance

- **Classement proposé :** faire grandir l’API de greffons en écosystème · pilier ambitieux · **6/8** (besoin 1, adéquation 1, levier 2, différenciation 2).
- **Hypothèse :** un contrat d’activité stable, des exemples et tests de compatibilité, puis un catalogue public facultatif d’extensions qui réutilisent la même matière Compact/Preview/Expanded et Pixel permettent à des auteurs tiers d’ajouter des usages que l’équipe ne peut pas maintenir elle-même.
- **Risque :** surface de support et compatibilité disproportionnées, chaîne d’approvisionnement mal comprise, ou fausse promesse de bac à sable ; l’API actuelle charge les assemblages de greffons dans le processus de SpaceNotch.
- **Test léger :** entretiens et prototype de flux avec cinq auteurs potentiels, sans créer de catalogue ni installer de greffon ; faire réaliser un greffon de démonstration avec la documentation actuelle, puis comparer avec un exemple et un validateur conceptuels.
- **Seuil :** au moins 3/5 terminent un greffon de démonstration et au moins 2/5 souhaitent le maintenir ou le partager ; 5/5 comprennent qu’un greffon approuvé n’est pas isolé et que son code peut s’exécuter dans le processus de l’application.
- **Arrêt :** si les auteurs n’ont pas de cas concret, si la découverte exige compte/télémétrie, ou si l’isolation attendue ne peut être expliquée honnêtement, s’en tenir à l’API et aux exemples documentés, sans catalogue.
- **Distinction avec P18 :** P18 teste un outil local de validation technique des greffons ; P28 explore une chaîne plus large (contrat, compatibilité, documentation et découverte communautaire). P28 ne présume ni marché, ni hébergement, ni signature suffisante pour rendre le code sûr.
- **Sources et limites :** la [documentation actuelle des greffons](../plugin-api.md) confirme l’API étroite et ses limites (pas de vues personnalisées, préférences partagées ou rechargement à chaud). Le [catalogue de Flow Launcher](https://www.flowlauncher.com/plugins/) montre qu’un répertoire communautaire existe dans un outil voisin, mais ne prouve ni l’intérêt des auteurs pour SpaceNotch ni la viabilité d’un tel catalogue ici.

---

## 3. Classement provisoire

### À tester en premier

1. **P11 — clavier/Narrateur** : priorité qualité et inclusion ; test obligatoire, pas un pari facultatif.
2. **P12 — confiance à l’installation** : forte barrière à l’adoption, test terrain simple.
3. **P01 — onboarding en trois gestes** : mesure directement la découvrabilité.
4. **P02 — interruptions** : établit si la douleur est un réglage global ou des règles par activité.
5. **P03 — Minuteur/Focus** : vérifier la compréhension avant fusion technique.
6. **P21 — Météo** : observer d’abord l’usage de l’aperçu actuel, puis comparer les trois densités sans activer de localisation automatique.

### À conserver derrière preuve supplémentaire

P04–P10 améliorent les fonctions existantes mais doivent d’abord être comparées aux comportements et réglages actuels. P13–P20 et P22–P28 ne passent pas à la conception sans seuil de test : leur note actuelle n’est pas une approbation. P21 peut rejoindre la première série de tests seulement si l’aperçu météo actuel est réellement utilisé ; le document ne dispose pas encore de mesure d’usage. P27 est le meilleur candidat parmi les nouveaux piliers à prototyper, à condition de prouver qu’il apporte plus qu’un lanceur d’applications ; P28 reste une option d’écosystème dépendant d’un intérêt concret d’auteurs tiers.

### Promesses à éprouver avant toute communication publique

- **P27 :** « Des sessions de travail qui réunissent le bon contexte dans votre notch » — à employer seulement si un test confirme que les capsules font plus que lancer des applications.
- **P28 :** « Des activités créées par la communauté, dans l’expérience native de SpaceNotch » — à employer seulement après validation d’un parcours d’auteur réel, et avec une explication visible du modèle de confiance des greffons.
- Ces formulations sont des pistes de positionnement, pas des revendications de fonctions livrées, de demande mesurée, d’intégration universelle ou de sécurité garantie. Les piliers actuels — compagnon Pixel, activités contextuelles et approche locale — peuvent être présentés comme direction du produit, sans prétendre qu’ils sont exclusifs aux concurrents.

### À écarter explicitement tant que le besoin n’est pas établi

- Widgets permanents ou dashboard de petites vues.
- Assistant IA conversationnel toujours actif.
- Compte cloud, synchronisation obligatoire ou télémétrie utilisateur.
- Nouveau domaine d’intégration qui ajoute une configuration experte sans réduire une friction existante.
- Animation décorative sans information d’état ou d’interaction correspondante.

## 4. Ce que cette méthode n’autorise pas

- Un score ne constitue pas une preuve d’usage ni une décision d’architecture.
- Un test léger ne remplace ni les tests logiciels ni une validation de sécurité/confidentialité.
- Une idée de « proche » n’est pas automatiquement pour la prochaine release.
- Un test d’utilisateur ne donne pas l’autorisation d’envoyer ou collecter ses données sans consentement.
- Une hypothèse marquée ambitieuse ne doit pas se glisser dans le périmètre sous prétexte qu’elle est différenciante.
- Chaque changement de code ultérieur garde son propre critère d’acceptation, son plan d’implémentation et sa validation.

## 5. Références produit

- [Audit produit d’octobre 2026](../audit/2026-10-audit-produit.md)
- [Direction de SpaceNotch du 6 octobre](2026-10-06-direction.md)
- [Plan directeur UX 2.0](../ux/spacenotch-2.0.md)
- [ADR-017 — notch attachée](../decisions/ADR-017-notch-attachee.md)
- [ADR-018 — mouvement hypnotique](../decisions/ADR-018-mouvement-hypnotique.md)
- [ADR-029 — Pixel, compagnon vivant](../decisions/ADR-029-compagnon-vivant-pixel-identite.md)
- [ADR-031 — aucun install sans accord utilisateur](../decisions/ADR-031-rien-ne-s-installe-sans-l-utilisateur.md)

### Sources de recherche concurrentielle pour P27–P28

Ces sources étayent les capacités existantes ou les possibilités de la plateforme ; elles ne remplacent pas une enquête représentative auprès des utilisateurs de SpaceNotch.

- [Microsoft PowerToys — Workspaces](https://learn.microsoft.com/en-us/windows/powertoys/workspaces)
- [Microsoft — Focus sous Windows](https://support.microsoft.com/en-us/windows/experience/focus-stay-on-task-without-distractions-in-windows)
- [Windows App SDK — FocusSessionManager](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/applifecycle/focus-session)
- [PowerToys — demande de Workspaces entre bureaux virtuels](https://github.com/microsoft/PowerToys/issues/46109) · [demande initiale](https://github.com/microsoft/PowerToys/issues/34578)
- [Flow Launcher — répertoire des greffons](https://www.flowlauncher.com/plugins/)
