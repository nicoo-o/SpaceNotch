# Audit produit de SpaceNotch — octobre 2026

> **Objet :** évaluer le produit dans son ensemble — sa raison d’être, ses fonctions, son expérience et sa direction — et non répéter l’audit de code existant.  
> **Périmètre :** revue documentaire et inspection ciblée des fonctionnalités, des réglages et des vues. Aucun code ni aucune modification déjà présente dans le dépôt n’ont été modifiés pour cet audit.

## Verdict en une phrase

**SpaceNotch possède une signature visuelle et technique inhabituelle pour un outil Windows, mais son ambition s’est étendue plus vite que sa promesse ne s’est simplifiée : il faut maintenant protéger le cœur, rendre les fonctions avancées réellement optionnelles et consacrer davantage d’effort à la confiance, à la découvrabilité et à la finition vérifiée sur de vrais PC qu’à l’ajout de nouvelles fonctions.**

Ce n’est pas un projet qui manque d’idées ou d’architecture. C’est un produit qui doit décider ce que l’utilisateur doit comprendre en premier et ce qu’il n’a pas besoin de voir.

## 1. La promesse : distincte, mais tirée dans trois directions

Le dépôt décrit SpaceNotch à la fois comme :

1. **une notch contextuelle calme**, qui apparaît pour une activité et retourne au repos ;
2. **un compagnon vivant**, Pixel, présent même quand rien ne se passe ;
3. **un poste de commande**, avec recherche, presse-papier, étagère, calendrier, agents, partage, assistant et réglages approfondis.

Ces rôles ne sont pas tous incompatibles, mais ils réclament la même surface minuscule et la même attention. Le document de direction d’octobre choisit le compagnon vivant comme identité, tout en précisant que l’application n’est pas un tableau de bord ni un outil réservé aux développeurs. C’est un choix défendable — Pixel différencie SpaceNotch — mais il faut en accepter le coût : une présence et une activité de fond plus difficiles à concilier avec les promesses de calme, de discrétion et d’autonomie.

**Recommandation de positionnement :** présenter SpaceNotch comme **un compagnon contextuel pour Windows**. La notch et ses activités font le travail utile ; Pixel lui donne une personnalité, sans transformer l’application en tableau de bord ni faire de l’animation la valeur d’usage principale. Garder le choix actuel de Pixel, mais mesurer et expliquer son coût énergétique sur batterie, prévoir un réglage accessible et ne pas assimiler « CPU bas » à « coût nul ».

### Ce qui rend le produit reconnaissable

- Forme attachée au bord, épaules concaves, noir OLED, absence de bord inférieur marqué et atmosphère en dissolution : une grammaire visuelle cohérente, exprimée dans le [langage visuel](../ux/design-language.md) et les [décisions de forme](../decisions/ADR-017-notch-attachee.md).
- Passages d’une activité compacte à une surface ouverte, pochette qui grandit et mouvement hypnotique destiné à signifier un travail réel, plutôt qu’une collection de fenêtres indépendantes.
- Pixel donne un visage propre au produit et aux états d’agents. Cette identité ne dépend pas d’une marque tierce ; le choix est explicité dans [ADR-029](../decisions/ADR-029-compagnon-vivant-pixel-identite.md).
- Application native, sans WebView, locale par défaut, sans compte ni télémétrie : ce sont de vrais choix de produit, pas seulement des détails d’implémentation.

### Ce qui brouille la promesse

- La liste de fonctions devient longue au point que la notch peut sembler être un « tout-en-un » plus qu’un compagnon simple.
- Plusieurs fonctions sont des intégrations expertes, rares ou dépendantes d’une configuration externe : Discord RPC, Spotify, Claude, Phi Silica, hooks d’agents, partage QR, OCR. Elles n’ont pas le même public que le média, le volume ou le minuteur.
- L’interface dépend encore de gestes peu visibles. Une fonction peut être techniquement complète tout en restant pratiquement introuvable.
- La voix marketing parle de calme et de silence, tandis que Pixel, les annonces et certaines activités répétées sont présents en continu. Il faut traiter cette tension comme un compromis produit mesurable, pas la résoudre par une formule.

## 2. Revue des fonctionnalités : conserver le cœur, réduire les recouvrements

**Échelle de lecture :** « cœur » = usage attendu et forte adéquation ; « secondaire » = utile, mais doit rester discret ou opt-in ; « avancé » = public plus étroit, configuration ou risque supérieur ; « à simplifier » = le besoin existe, mais le regroupement ou la présentation actuelle mérite révision.

| Fonction ou groupe | Verdict | Avis produit |
|---|---|---|
| **Média et contrôles de lecture** | **Garder — cœur** | Excellent usage de la forme : état, pochette, contrôles dans une surface qui se déploie naturellement. C’est le meilleur exemple de la promesse « une activité, une matière ». Les paroles et fonctions Spotify enrichies restent secondaires et explicitement connectées seulement si l’utilisateur les active. Continuer à rendre le cas simple robuste avant d’ajouter des commandes. |
| **Volume et luminosité** | **Garder — cœur système** | Retours courts et familiers ; leur durée limitée convient à une notch. Attention au positionnement : le HUD de volume accompagne les changements audio mais ne remplace pas l’indicateur natif de Windows. L’audit de direction le relève déjà ; ne pas le promettre comme remplacement tant que Windows continue à montrer son propre HUD. La luminosité doit signaler clairement qu’elle dépend du matériel et peut être indisponible sur écran externe. |
| **Notifications** | **Garder, améliorer l’arbitrage** | Les groupes par application et le résumé de notifications retenues répondent à un vrai besoin. C’est néanmoins une source de distraction et de données sensibles : accès demandé au bon moment, filtrage compréhensible et contrôle de chaque source priment sur les résumés générés. Le silence de réunion, le mode Ne pas déranger et les appels ne doivent jamais se contredire. |
| **Appels, microphone et caméra** | **Garder — sécurité et contexte** | Savoir qu’un micro ou une caméra est utilisé a une utilité concrète et une valeur de confiance. La distinction entre appel connu et captation est une bonne approche ; éviter toute formulation qui surinterprète l’usage d’un capteur. Le miroir de webcam doit rester explicitement activé par l’utilisateur et ne jamais s’allumer par surprise. |
| **Bluetooth et branchement du chargeur** | **Garder, rester bref** | Une connexion, une déconnexion ou une batterie faible mérite un bref signal. Une annonce à chaque branchement avec niveau de batterie est utile mais ne justifie pas un tableau permanent de périphériques. Montrer seulement les changements pertinents et la donnée que Windows expose réellement. |
| **Téléchargements** | **Garder — bonne adéquation** | La progression et l’action « Ouvrir/Afficher » sont parmi les meilleurs usages d’une surface compacte, et la découverte via le dossier Téléchargements évite une extension de navigateur. Ne pas transformer chaque écriture en animation ou arrivée bruyante : garder le regroupement, les mises à jour de progression limitées et un état final actionnable. |
| **Étagère de fichiers** | **Garder, clarifier le modèle** | Le dépôt et la reprise par glisser-déposer sont une interaction signature. Mais une étagère qui conserve des chemins en mémoire, sans persistance, n’est pas une étagère durable : dire clairement sa durée de vie et ce qui se passe si le fichier est déplacé ou l’application fermée. Faire du dépôt une action réversible et vérifier le chemin avant l’ouverture ou le partage. |
| **Partage PC → téléphone par QR** | **Secondaire / avancé, garder si la sécurité est expliquée** | Le partage local évite un service cloud et s’accorde avec la promesse privée. Il crée cependant un serveur réseau temporaire, donc un niveau de risque et une configuration que la plupart des utilisateurs ne supposent pas en voyant un QR. Expliquer le réseau local, la durée, le téléchargement unique et l’arrêt du partage ; ne pas l’activer tant qu’une action explicite n’a pas été faite. |
| **Recherche et lanceur** | **Garder — cœur différenciant** | La recherche d’applications, réglages, fichiers et calculs est utile même quand aucune activité n’arrive. C’est toutefois un produit dans le produit : classement, favoris, récents, commandes et recherche web doivent avoir une hiérarchie intelligible. Protéger le focus et la navigation clavier ; l’action administrative devrait être rare, délibérée et clairement signalée. |
| **Menu rapide, tuiles et présentation initiale** | **Garder, simplifier la découverte** | Le menu est le filet de sécurité pour les gestes et les réglages. L’onboarding doit enseigner quelques gestes essentiels, pas une encyclopédie. Les quatre raccourcis au clic, le clic droit et les raccourcis clavier doivent être cohérents entre eux et enseignés au moment utile. Une fonction qui n’est accessible que par combinaison de touches cachée n’est pas réellement découvrable. |
| **Minuteur, Pomodoro, focus et rendez-vous** | **Fusionner les concepts, garder les scénarios** | Minuteur et Pomodoro partagent modèle de compte à rebours, pause, reprise, fin et affichage. Deux fonctions voisines créent de la confusion alors que la direction produit a déjà envisagé « Focus 25 min » comme mode du minuteur. Recommander **une entrée Minuteur**, avec compte à rebours, chronomètre et session de focus comme modes. Le calage avant réunion est une valeur ajoutée ; il doit rester une option claire, pas un comportement magique. |
| **Notes et rappels** | **Garder, cadrer** | La note rapide et les rappels en langage naturel sont cohérents avec une surface d’action personnelle. Ils ne doivent pas se faire passer pour un gestionnaire de tâches complet. Rendre l’enregistrement visible, les échéances vérifiables et le report/effacement simple ; préserver l’usage local et éviter les interprétations silencieuses de dates ambiguës. |
| **Presse-papier et actions sur copie** | **Presse-papier opt-in ; fusionner les actions** | Historique en mémoire, désactivé par défaut, et exclusion des secrets : posture de confidentialité solide. Les actions IA sur copie recouvrent ce domaine mais augmentent fortement le risque de surprise. Un seul espace « Presse-papier » avec historique facultatif et actions explicitement choisies est plus compréhensible que deux écouteurs indépendants. Ne jamais lire ni envoyer de contenu sans consentement explicite, particulièrement après une copie d’un gestionnaire de mots de passe. |
| **Canal local et agents IA** | **Garder — avancé / développeurs** | Le tube local à l’utilisateur et le consentement explicite sur une demande d’agent forment une intégration originale, plus crédible qu’une connexion cloud permanente. Cela ne concerne pas le public général ; le garder dans les réglages avancés et le présenter comme une intégration optionnelle. Documenter l’information transmise et le comportement lorsque la notch est absente ou qu’une demande expire. |
| **Assistant IA** | **Optionnel et au geste** | L’approche « règles locales d’abord », modèle absent par défaut, Phi Silica ou clé utilisateur et actions déclenchées explicitement est plus cohérente avec la vie privée que l’IA ambiante. Limiter les boutons aux contextes où l’utilisateur a une intention (recherche, texte sélectionné) et rendre visible où la demande est traitée. Ne pas construire un chatbot général dans la notch : ce serait un changement de produit. |
| **Discord vocal, téléphone et livraisons** | **Avancé ; garder l’isolation, réduire la vitrine** | Appel et livraison via notifications existantes couvrent des scénarios compréhensibles, mais la reconnaissance par noms et mots-clés peut produire des faux positifs. Discord RPC exige l’application développeur et des autorisations particulières : c’est une fonction de niche. Placer l’intégration Discord en avancé et désactivée par défaut ; faire des fonctions téléphone/livraison des interprétations modestes, toujours corrigeables, plutôt que des promesses de compatibilité universelle. |
| **Calendrier et réunion** | **Garder si la disponibilité est honnête** | Le rappel avant réunion et l’action « Rejoindre » apportent un bénéfice clair. La qualité dépend de l’accès au calendrier et des liens détectés ; expliciter les sources et l’indisponibilité. Le miroir caméra au survol de « Rejoindre » est une idée contextuelle mais à risque de surprise : préférer un clic volontaire ou une préférence évidente, pas un simple survol. |
| **Moniteur CPU** | **Réexaminer l’exposition par défaut** | Une alerte rare de saturation peut aider à diagnostiquer un poste, mais ce n’est pas une fonction centrale d’une notch et elle impose un échantillonnage périodique. L’alerte propose de fermer un processus : même avec confirmation, c’est une action à conséquences. Recommander de la ranger dans Avancé, de l’éteindre par défaut jusqu’à démonstration de sa valeur, et de maintenir un échantillonnage partagé et mesuré. |
| **Météo** | **Optionnelle, discrète** | Pratique comme information de repos, mais sans rapport direct avec les activités du PC et dépendante d’un service distant et d’une ville configurée. Le comportement « pas de ville, pas de réseau » est bon. La garder derrière une activation ou dans le contenu de Pixel, sans en faire un argument central ni réveiller la notch régulièrement. |
| **Mises à jour** | **Garder — confiance essentielle** | La vérification d’une version est importante pour la sécurité ; l’installation qui attend le consentement est le bon choix tant que l’artefact n’est pas signé de façon fiable. Expliquer nettement les demandes d’administrateur, les vérifications et les limites de signature. Le clic utilisateur n’est pas une signature cryptographique : ne pas présenter le SHA publié avec la même release comme une preuve d’éditeur. Voir [ADR-031](../decisions/ADR-031-rien-ne-s-installe-sans-l-utilisateur.md). |
| **Pixel et mouvement hypnotique** | **Garder l’identité, contrôler l’intensité** | Pixel est distinctif ; le mouvement hypnotique a une sémantique utile quand une activité travaille. Leur association est forte si l’un ne parasite pas l’autre. Fournir une intensité réellement lisible (y compris mouvement réduit), mesurer batterie et temps de veille, et réserver la grille aux états qui progressent vraiment. Pas de mouvement permanent supplémentaire pour décorer. |
| **Greffons** | **Garder comme levier d’écosystème, pas comme fonction grand public** | Contrat étroit, chargement approuvé et carte générique sont de bons fondements. Les greffons ne sont pas isolés comme des processus séparés ; l’approbation et la signature ne rendent pas le code tiers inoffensif. Rester prudent sur le langage de sécurité, améliorer le guide et les diagnostics d’installation, et n’élargir les interfaces que devant des besoins tiers démontrés. |

### Les regroupements les plus rentables

1. **Minuteur** devient l’entrée unique pour compte à rebours, chronomètre et focus.
2. **Presse-papier** regroupe historique opt-in, signal « Copié » et actions sur copie, sans activer une surveillance cachée.
3. **Notifications** restent la source ; les cartes d’appels/livraisons spécialisées s’annoncent comme interprétations de ces notifications, pas comme des connexions autonomes au téléphone.
4. **Volume et luminosité** sont présentés ensemble comme retours système, sans prétendre masquer les interfaces natives.
5. **Pixel et grille hypnotique** ont des rôles distincts : personnalité au repos d’un côté, état de travail d’une activité de l’autre.

## 3. Design et expérience : excellente direction matérielle, complexité d’usage à maîtriser

### Cohérence visuelle : très forte sur le papier

TopAttached, les épaules concaves, le bas sans trait et l’atmosphère sont une signature lisible et défendable. Le mouvement hypnotique possède une fonction sémantique — signaler un travail en cours — et sa réduction en image fixe garde l’information. Les décisions correspondantes sont documentées dans [ADR-017](../decisions/ADR-017-notch-attachee.md) et [ADR-018](../decisions/ADR-018-mouvement-hypnotique.md).

Le risque n’est pas que le noir OLED ou la grille soient « trop modernes ». Le risque est que chaque nouvelle fonction apporte sa propre couleur, son propre geste, son propre état et sa propre carte. Le principe d’une matière unique n’est crédible que si les fonctions avancées réutilisent une grammaire commune au lieu d’étendre la palette et le nombre de panneaux.

### Compact / aperçu / ouvert : bon modèle, hiérarchie à éprouver

Le principe d’une information brève en Compact et de commandes dans la vue ouverte est adapté à l’écran supérieur. Il faut garantir, par essais d’usage, que :

- la forme compacte répond à « qu’est-ce qui se passe ? » sans faire défiler de texte trop dense ;
- l’aperçu apporte une information supplémentaire, pas simplement un autre état animé ;
- l’ouverture est toujours volontaire pour les activités ordinaires ;
- une activité ne change pas sous un contrôle pendant que l’utilisateur interagit ;
- bulle et pile ne se disputent pas l’attention ni la place ;
- une activité qui s’achève n’abandonne pas une scène vide ou un compte à rebours sans action valable.

Le projet a déjà formalisé plusieurs de ces règles dans le [plan UX 2.0](../ux/spacenotch-2.0.md) et les [règles du système d’activités](../activity-system.md). La prochaine étape utile n’est pas une autre refonte conceptuelle : c’est de rejouer ces critères sur les usages réellement fréquents, avec matériel et utilisateurs.

### Interactions : élégantes, mais trop faciles à oublier

Les gestes (survol, clic, molette, balayage, double-clic, Alt, Ctrl + molette, tirer/détacher, raccourcis globaux) donnent de la profondeur à l’outil, mais leur accumulation accroît la charge de mémorisation. Le tutoriel et le coach de gestes doivent enseigner un geste quand il devient pertinent, proposer une voie clavier équivalente et permettre de revoir l’aide. Les mêmes gestes ne doivent pas faire des choses surprenantes selon l’activité affichée.

La règle de qualité à appliquer : **chaque action fréquente a un contrôle visible ou une indication mémorisable ; chaque geste spécialisé a une solution clavier et une explication accessible.** Les contrôles Narrateur et clavier doivent être évalués à l’exécution, pas seulement d’après leur présence dans le XAML.

### Réglages : puissance réelle, coût de choix élevé

Le nombre de paramètres de géométrie, ressort, transparence, bords, écran et Pixel est remarquable pour les passionnés, mais trop élevé pour la majorité des nouveaux utilisateurs. Le mode « avancé » est la bonne structure ; il faut poursuivre en définissant des choix simples qui ont un résultat prévisible, masquer les paramètres redondants et éviter de demander à l’utilisateur de résoudre des décisions d’architecture.

Une stratégie recommandée : **Calme / Naturel / Expressif** pour le mouvement, **Automatique / Sombre / Clair** pour l’apparence, **écran principal / écran courant / choisi** pour le placement. Les réglages de forme détaillés restent avancés, accompagnés d’un aperçu instantané. « Automatique » doit réellement suivre Windows ; toute option affichée doit avoir un effet observable et expliqué.

## 4. Santé produit : forces, risques et points à clarifier

### Forces

- Direction visuelle distinctive plutôt qu’un assemblage de contrôles Windows.
- Respect de la vie privée pris en compte dans la conception du presse-papier, du canal local et des appels IA.
- Politique d’activité et cycle de vie qui évitent l’empilement illimité et les notifications permanentes.
- Architecture du cœur testable sans interface et séparation documentée entre fonctionnalités, plateforme et rendu.
- Capacité de dire non aux idées qui diluent le produit : pas de WebView, pas de cloud requis, pas d’ornement animé par défaut pour la grille.

### Risques prioritaires

1. **Confiance et installation.** Application non signée, avertissements Windows et demandes UAC répétées créent une barrière avant même que la qualité de l’interface puisse convaincre. C’est un enjeu de produit et de distribution, non une fonctionnalité à repousser.
2. **Surcharge attentionnelle.** Pixel au repos, HUD, notifications, appels, minuteur, mise à jour et activités prioritaires peuvent transformer une surface censée être calme en flux d’interruptions. Les politiques et réglages doivent donner à l’utilisateur le contrôle simple de ce qui le réveille.
3. **Trop de surface avancée.** Une vingtaine de domaines de fonctionnalités et de nombreux réglages rendent la page d’accueil difficile à expliquer. Une fonction terminée mais introuvable ou coûteuse à configurer n’est pas un succès utilisateur.
4. **Écart entre documentation et vérité vécue.** Les audits et plans sont riches, mais des affirmations de performance, de remplacement du HUD ou de compatibilité doivent rester exactes. Les documents eux-mêmes reconnaissent des mesures non faites sur vrai PC et des limites d’exécution WinUI hors Windows.
5. **Finition/accessibilité à démontrer.** Les constats de l’audit technique d’octobre sur clavier, Narrateur, noms accessibles, réglages et contraste sont sérieux. Leur correction décrite dans le même rapport ne vaut pas preuve complète que toutes les scènes fonctionnent dans une session réelle avec Narrateur, clavier, texte agrandi et contraste élevé.
6. **Intégrations fragiles par nature.** Détection de téléchargements, interprétation d’appels/livraisons, Discord RPC et API système dépendent d’autres applications, pilotes, langues ou permissions. Les erreurs doivent être compréhensibles et réversibles ; promettre « tous les navigateurs » ou « chaque appel » exige des tests de compatibilité continus.

### Points à clarifier avant d’ajouter de nouveaux domaines

- Quel événement mérite vraiment d’interrompre l’activité actuelle, et lequel peut attendre ?
- Quelles fonctions doivent être actives dès l’installation, et lesquelles doivent demander un opt-in explicite ?
- L’étagère est-elle temporaire (session) ou persistante ? Son libellé et son comportement doivent raconter la même chose.
- Qu’est-ce que l’utilisateur peut désactiver en une seule action : Pixel, mouvement, notifications, ou tout mode de présence ?
- L’interface permet-elle de retrouver chaque fonction autrement que par un geste non découvert ?
- Quel niveau de preuve accompagne « privé », « léger », « accessible » et « compatible » ?

## 5. Feuille de route recommandée

### P0 — Consolider confiance et usages fondamentaux

| Initiative | Pourquoi maintenant | Indicateur de réussite |
|---|---|---|
| **Réduire la friction d’installation et clarifier les permissions** | SmartScreen, signature et élévation concernent chaque nouvel utilisateur ; c’est la plus grosse barrière à l’essai. Continuer les voies de signature/distribution prévues, expliquer chaque permission avant la demande et ne pas confondre somme de contrôle et preuve d’éditeur. | Installation et première mise à jour compréhensibles sans documentation externe ; motif d’UAC expliqué avant l’invite ; échec récupérable. |
| **Parcours de première utilisation centré sur 3 actions** | Montrer découvrir (survol/clic), ouvrir la recherche et retrouver les réglages ; enseigner les gestes spécialisés seulement lorsqu’ils deviennent utiles. | Un nouvel utilisateur sait ouvrir, fermer et retrouver l’aide sans consulter le README. |
| **Établir un mode simple d’interruptions** | Les utilisateurs ont besoin de contrôler le flux plus que d’ajouter une activité. Définir silencieux, normal et priorité/urgence, avec DND et réunions cohérents. | Aucun HUD ou notification récurrente ne surprend ; une priorité critique ne masque jamais une action en cours sans politique explicite. |
| **Consolider Minuteur et focus** | Retirer un doublon conceptuel, simplifier les tuiles et réduire les explications. | Un seul point d’entrée couvre chronomètre, compte à rebours, session focus et fin de session. |

### P1 — Prouver la qualité sur les vraies configurations

| Initiative | Pourquoi | Indicateur de réussite |
|---|---|---|
| **Campagne de validation Windows réelle** | Densité visuelle et comportement des fenêtres ne sont pas entièrement validables par tests Core. | Scénarios vérifiés sur 100/150/200 % DPI, plusieurs écrans et mélanges de DPI, barre des tâches déplacée, thème clair/sombre, contraste élevé, mouvement réduit et plein écran. Captures datées et problèmes tracés. |
| **Parcours clavier/Narrateur par scène** | Le produit est un overlay et ne reçoit pas naturellement le focus ; chaque scène doit prouver sa navigabilité et ses annonces. | Recherche, musique, minuteur, notifications, presse-papier, étagère, menu et réglages utilisables sans souris, avec annonces non répétées. |
| **Mesurer le coût de Pixel sur batterie** | La mesure CPU seule ne décrit ni réveils, ni consommation énergétique, ni confort sur portable. | Mesures reproductibles, PC branché et sur batterie, Pixel calme/normal/expressif, mouvement réduit, pointeur immobile/mobile ; documentation alignée sur les résultats. |
| **Table de compatibilité et erreurs compréhensibles** | Les intégrations ont des dépendances externes. | Pour chaque intégration avancée : prérequis visibles, état indisponible expliqué, reprise propre, test des applications et versions prises en charge. |

### P2 — Développer ce qui approfondit le produit, pas ce qui l’élargit

| Idée | Gain espéré | Garde-fou |
|---|---|---|
| **Règles locales de présentation par activité** | « Toujours afficher », « attendre », « ne pas déranger », durée et priorité réglés une fois par type de source. Plus d’agence utilisateur, moins d’interruptions. | Interface courte avec quelques profils ; pas un moteur de règles généraliste. |
| **Étagère temporaire avec états de fichiers explicites** | Rendre dépôt, reprise, déplacement/suppression et partage sûrs et prévisibles. | État clair (session ou persistant), chemin vérifié, action annuler, aucune suppression du fichier source implicite. |
| **Mini-centre d’activité et d’historique éphémère** | Retrouver une notification, fin de téléchargement ou événement manqué sans transformer la notch en barre de notifications permanente. | Conservation locale, durée courte réglable, séparation des données sensibles et contrôle d’effacement facile. Valider le besoin avant d’archiver davantage. |
| **Modes de présentation par contexte** | Profils prédéfinis « Travail », « Présentation/jeu », « Concentration » qui combinent visibilité, interruptions et mouvement. | Utiliser les signaux Windows déjà disponibles ; aucun calendrier/compte distant obligatoire ; transitions prévisibles et désactivables. |
| **Kit de validation des greffons** | Faciliter les greffons utiles et rendre leur comportement plus prévisible. | Exemple versionné, tests de cycle de vie/arrêt, diagnostic de permissions et limites clairement documentées ; pas de promesse d’isolation au niveau processus. |

### À ne pas poursuivre pour l’instant

- Ajouter une galerie de widgets ou des cartes permanentes : cela ferait dériver la notch vers un tableau de bord.
- Ajouter un assistant conversationnel toujours actif, un compte cloud ou une synchronisation obligatoire : contraire au modèle local et inutile pour la promesse centrale.
- Ajouter des animations ou mascottes juste pour décorer ; la grille et Pixel ont déjà des rôles identifiables.
- Ajouter encore des intégrations expertes avant de réduire leur friction de configuration et de vérifier celles qui existent.
- Exposer plus de réglages techniques quand les choix courants ne sont pas encore suffisamment simples et vérifiables.

## 6. Filtre de décision pour toute nouvelle fonctionnalité

Avant d’accepter une nouvelle idée, demander :

1. **Quel besoin immédiat satisfait-elle ?** Le besoin existe-t-il souvent, et la notch est-elle le bon endroit pour cela ?
2. **Quel est le coût d’attention ?** Faut-il interrompre, remplacer, partager la bulle, attendre ou rester silencieux ?
3. **Quel est le consentement nécessaire ?** Capteur, presse-papier, réseau, compte, modèle ou données personnelles : qu’est-ce qui est lu et quand ?
4. **Que reste-t-il au repos ?** Écouteur, minuterie, CPU, réseau, consommation de batterie ; une fonction inactive doit cesser le travail non nécessaire.
5. **Quelle est sa version compacte et son état ouvert ?** L’information principale reste-t-elle lisible sans devenir un petit tableau de bord ?
6. **Comment l’utiliser sans souris ?** Clavier, Narrateur, réduction du mouvement, texte agrandi et contraste élevé.
7. **Que fait-on quand cela échoue ?** Indisponibilité, données périmées, permission refusée, service absent, annulation et retour arrière.
8. **Quel autre concept peut-elle simplifier ou remplacer ?** Si la réponse est « aucun », exiger une preuve d’usage avant de créer un domaine de plus.

Une idée qui ne satisfait pas les critères de confidentialité, de compréhension et d’arrêt au repos n’est pas prête, même si son prototype est séduisant.

## 7. Limites de cet audit

- Les implémentations et documents ont été inspectés, ainsi que les fichiers XAML pertinents, mais les captures du README n’ont pas pu être ouvertes avec l’accès aux fichiers disponible dans cette session. **Ce rapport ne prétend donc pas certifier le rendu visuel réel, l’animation, le contraste perçu ou le comportement sous Windows.**
- Les constats tirés des audits déjà présents sont référencés comme constats documentés, pas comme tests répétés ici. Voir l’[audit technique d’octobre](2026-10-audit.md), l’[audit de septembre](2026-09-audit.md) et le [plan directeur UX 2.0](../ux/spacenotch-2.0.md).
- Aucun entretien utilisateur, mesure d’adoption, analyse de fréquence réelle des fonctionnalités ou mesure de batterie n’a été effectué pour ce rapport. Les priorités d’usage sont des jugements produit fondés sur les scénarios décrits dans le dépôt ; elles devront être confirmées par retours utilisateurs et essais terrain.
- Aucun test, build ou audit d’accessibilité à l’exécution n’a été lancé : ce livrable est une revue de produit documentaire, et non une validation de release.

## Conclusion

**Garder le cœur, arrêter d’élargir la vitrine.** Média, recherche, HUD, notifications maîtrisées, minuteur/focus, téléchargements et Pixel forment déjà un produit défendable. La prochaine version de SpaceNotch gagnera davantage à inspirer confiance, à expliquer ses gestes, à réduire les doublons et à prouver l’expérience sur plusieurs écrans et avec les technologies d’assistance qu’à ajouter des intégrations supplémentaires.

L’objectif n’est pas d’enlever toute personnalité ni toute expérimentation : c’est de faire en sorte que chaque fonction avancée paraisse être une extension naturelle de la même notch, soit invisible lorsqu’elle n’est pas utile, et laisse toujours à l’utilisateur le contrôle de son attention et de ses données.
