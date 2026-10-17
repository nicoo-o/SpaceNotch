# Pistes ambitieuses — SpaceNotch

> **Statut : hypothèses de produit à étudier, pas décisions de livraison.** Ce document complète la [feuille d’idées et son filtre](2026-10-16-idees-et-filtre-produit.md) sans la remplacer. Les deux pistes ci-dessous sont des projets distincts : elles ne doivent pas être regroupées en un lancement unique ni considérées comme promises.

## Pourquoi un document séparé

Ces propositions changent potentiellement le périmètre de SpaceNotch : l’une ajoute une relation distante entre le PC et le téléphone ; l’autre étend l’expérience visuelle à plusieurs surfaces de Windows. Chacune demande des choix d’architecture, de sécurité, de support et de positionnement plus importants qu’une fiche d’idée ordinaire. Les scores sont des estimations de discussion ; les sources établissent surtout des capacités et contraintes techniques, pas une demande mesurée auprès des utilisateurs SpaceNotch.

---

## A. Accès distant au PC et partage de fichiers avec un téléphone

### Promesse à tester

Depuis un téléphone, retrouver et télécharger de manière sûre un fichier autorisé sur son PC, même quand les deux appareils sont sur des réseaux différents — sans transformer SpaceNotch en service cloud de synchronisation généraliste.

### Proposition de périmètre par étapes

1. **Éprouver le besoin** avec des scénarios et maquettes : qui veut retrouver quels fichiers, à quelle fréquence, et pourquoi les méthodes existantes ne suffisent-elles pas ?
2. **Prototype distant en lecture seule** : un téléphone parcourt uniquement les dossiers explicitement partagés et peut télécharger un fichier. Le PC doit être allumé, éveillé et connecté.
3. **Transport distant** : commencer avec une route privée/VPN choisie et configurée par l’utilisateur. Un relais géré par le projet, destiné à fonctionner sans réglage réseau, serait une phase séparée avec ses propres coûts et engagements de confidentialité.
4. **Écriture ou synchronisation éventuelle** : hors du premier périmètre. Les ajouts, remplacements, suppressions et copies hors ligne créent des risques et des comportements distincts à concevoir et tester.

### Score provisoire : 5/8

Besoin 1 · adéquation 1 · levier 1 · différenciation 2. La différenciation potentielle vient d’un accès intégré à l’expérience SpaceNotch, mais le besoin et la meilleure expérience téléphone restent à démontrer. Le score n’autorise pas un développement.

- **Hypothèse :** au moins une population identifiable veut régulièrement accéder à ses propres fichiers Windows depuis son téléphone hors du domicile ou du réseau local, et préfère un accès à la demande à une synchronisation permanente.
- **Risque principal :** fuite ou altération de fichiers par un téléphone perdu, un appairage compromis, une mauvaise configuration réseau ou un service exposé trop largement ; hausse forte du coût de support et d’exploitation si SpaceNotch fournit un relais.
- **Test léger :** entretiens avec six personnes et storyboard de trois étapes (choisir les dossiers sur PC, appairer le téléphone, parcourir/télécharger à distance). Comparer accès VPN, partage local existant et synchronisation ; ne pas exposer un vrai PC à Internet.
- **Seuil pour poursuivre :** au moins 4/6 personnes décrivent un besoin récent et récurrent ; 5/6 comprennent quels dossiers sont accessibles, quand le PC doit être allumé et comment révoquer un téléphone. Avant un essai technique, démontrer lecture seule, appairage révocable et absence d’accès à des chemins hors périmètre.
- **Condition d’arrêt :** si la majorité veut surtout synchroniser ou sauvegarder, si le VPN est inacceptable et qu’aucun relais soutenable n’est identifié, ou si le modèle de permissions reste ambigu, ne pas bâtir de service de fichiers distant dans SpaceNotch.

### Garde-fous techniques et de confidentialité

- Ne jamais exposer directement SMB/port 445 à Internet ; Microsoft recommande de bloquer l’accès SMB entrant depuis Internet ([guide de sécurité SMB](https://learn.microsoft.com/en-us/windows-server/storage/file-server/smb-secure-traffic)). Le service éventuel serait un protocole applicatif limité, sans droits administrateur.
- L’accès initial est **lecture seule**, avec des racines de dossiers choisies explicitement et une validation des chemins côté serveur. Une connexion VPN protège le trajet réseau, mais ne remplace ni l’autorisation par dossier ni le contrôle d’accès de l’application.
- Appairage explicite, identifiant de chaque appareil, révocation depuis le PC, durée de session limitée et absence d’ouverture automatique après installation. Aucune action d’écriture, de suppression ou d’exécution de fichier dans le MVP.
- Un accès entre réseaux nécessite une route : VPN, adresse publiquement joignable/configuration réseau, ou relais. Les systèmes de traversée NAT peuvent établir une connexion directe et utiliser un relais quand elle échoue ; Tailscale documente ces deux cas et le chiffrement de bout en bout de son propre réseau ([types de connexion](https://tailscale.com/docs/reference/connection-types), [modèle de chiffrement](https://tailscale.com/docs/concepts/tailscale-encryption)). C’est un exemple d’architecture, pas un choix de fournisseur pour SpaceNotch.
- Un relais tiers peut ne pas lire le contenu si le protocole est chiffré de bout en bout, mais il peut quand même voir certaines métadonnées de connexion. Si le projet opère un relais, disponibilité, bande passante, abus, mises à jour, incidents et politique de conservation deviennent des responsabilités produit.
- La voie de transport est à étudier séparément de l’app mobile : interface native ou client Web, Android/iOS, travail en arrière-plan, fichiers hors ligne, chiffrement et coût d’exploitation sont des décisions ouvertes.

### Sources et limites

- [Microsoft — sécuriser le trafic SMB](https://learn.microsoft.com/en-us/windows-server/storage/file-server/smb-secure-traffic)
- [Tailscale — types de connexion](https://tailscale.com/docs/reference/connection-types)
- [Tailscale — chiffrement](https://tailscale.com/docs/concepts/tailscale-encryption)
- [Phone Link — prérequis et configuration](https://support.microsoft.com/en-us/windows/apps/phonelink/phone-link-requirements-and-setup) — produit existant utile à comparer ; sa configuration documentée dépend du même réseau Wi-Fi.
- [KDE Connect — documentation utilisateur](https://userbase.kde.org/KDEConnect) — client PC/téléphone et appairage existants ; l’accès distant dépend encore d’une route réseau.

Ces sources ne prouvent pas que les utilisateurs veulent une nouvelle solution de partage distant. Ne pas annoncer « accès partout » avant des tests sur réseaux NAT et une stratégie explicite pour les relais.

---

## B. Personnalisation du bureau Windows façon macOS

### Promesse à tester

Faire cohabiter SpaceNotch, un dock inspiré de macOS et une barre supérieure SpaceNotch dédiée, pour créer une expérience visuelle cohérente sur Windows. Chaque surface et chaque modification de la barre des tâches s’active séparément, pendant l’installation ou plus tard dans les réglages ; rien n’est imposé.

**La barre supérieure est une surface SpaceNotch**, avec ses propres éléments (horloge, raccourcis, états système et commandes SpaceNotch). Elle ne prétend pas reprendre les menus de chaque application Windows : aucune API globale ne garantit cette compatibilité.

### Proposition de paliers

1. **Cohérence SpaceNotch** : apparence de la notch et de ses surfaces suivant les choix de thème Windows ; aucun changement à Explorer.
2. **Barres SpaceNotch optionnelles** : dock et top bar indépendants, chacun activable ou masquable. Étudier une fenêtre native ancrée ; vérifier réservation d’espace, plein écran, multi-écrans et DPI.
3. **Raccourcis vers les commandes Windows existantes** : proposer, au cas par cas, les options natives de masquage/réduction de Search, Widgets ou Task View, avec rappel des raccourcis alternatifs. Un bouton masqué n’est pas nécessairement une fonction remplacée.
4. **Style avancé de la taskbar** : transparence, taille et masquage d’éléments internes, seulement comme option expérimentale indépendante, si la voie technique et le support sont acceptables. Ce palier ne doit pas conditionner le dock, la top bar ou le fonctionnement de base.

### Score provisoire : 6/8

Besoin 1 · adéquation 1 · levier 2 · différenciation 2. Le potentiel différenciant est une expérience unifiée notch + surfaces de bureau, mais l’envie de remplacer visuellement certains éléments Windows doit être prouvée. Le score n’autorise pas un développement.

- **Hypothèse :** un groupe d’utilisateurs préfère activer un dock et une barre supérieure assortis à SpaceNotch, tout en gardant le choix de conserver intégralement la taskbar Windows.
- **Risque principal :** surcharge visuelle et perte d’accès aux commandes système ; comportement fragile après mise à jour si le style avancé agit sur les éléments internes d’Explorer.
- **Test léger :** maquettes comparant (A) notch seule, (B) notch + dock, (C) notch + dock + top bar ; six utilisateurs choisissent les surfaces, les éléments qu’ils masqueraient et savent retrouver Start, recherche, zone de notification et réglages système.
- **Seuil pour poursuivre :** au moins 4/6 choisissent un des composants ajoutés pour un scénario récurrent ; 6/6 comprennent que dock, top bar et personnalisation avancée sont indépendants, désactivables et réversibles. Aucun composant essentiel n’est masqué sans alternative découverte par tous.
- **Condition d’arrêt :** si les participants préfèrent la taskbar native avec seulement la notch, si le dock et la top bar se concurrencent, ou si le scénario dépend d’un hack Explorer non récupérable après une mise à jour, garder uniquement la personnalisation de SpaceNotch et ses intégrations natives.

### Garde-fous techniques et UX

- **Préserver Explorer par défaut.** Windows documente les AppBars, fenêtres ancrées qui négocient leur position avec la taskbar et les autres barres ([Microsoft — Application Desktop Toolbars](https://learn.microsoft.com/en-us/windows/win32/shell/application-desktop-toolbars)). C’est une base possible pour une barre compagnon, pas une API de redessin complet de la taskbar.
- **Séparer les options.** Toggles distincts pour le dock, la top bar SpaceNotch, les raccourcis vers réglages natifs et le style avancé. Installation et désinstallation ne changent jamais ces préférences sans consentement explicite.
- **Ne pas confondre style natif et style interne.** Les options Windows documentées sont le chemin de base ([personnaliser la taskbar](https://support.microsoft.com/en-us/windows/experience/personalization/customize-the-taskbar-in-windows)). La transparence poussée, la taille arbitraire et les sélecteurs internes de Taskbar Styler reposent sur des détails d’interface et une injection de code, pas sur une API publique stable ([guide Taskbar Styler](https://github.com/ramensoftware/windows-11-taskbar-styling-guide), [injection Windhawk](https://github.com/ramensoftware/windhawk/wiki/Injection-targets-and-critical-system-processes)). Toute intégration de cette voie demanderait une décision de confiance, de distribution, de compatibilité et de récupération ; ne pas supposer qu’il faut embarquer Windhawk.
- **Retour arrière évident.** Conserver les choix précédents, offrir « restaurer l’apparence Windows », démarrer en mode sûr si une modification avancée échoue, et ne pas réactiver automatiquement un style non vérifié après une mise à jour Windows.
- **Protéger les fonctions système.** Ne masquer ni Start, réseau, volume, confidentialité, ni zone de notification par défaut. Si un utilisateur les masque volontairement, l’assistant doit expliquer les alternatives et confirmer le choix.
- **Barre supérieure honnête.** Ses widgets appartiennent à SpaceNotch. Les menus actifs d’applications peuvent être ajoutés plus tard par intégrations dédiées, pas promis universellement.
- **Valider l’expérience complète.** Tester plusieurs versions Windows, écrans et niveaux DPI, taskbar auto-hide, plein écran/jeux, contraste, clavier et Narrateur ; prévoir que certaines options avancées soient indisponibles ou suspendues sur une version non validée.

### Sources et limites

- [Microsoft — personnaliser la taskbar](https://support.microsoft.com/en-us/windows/experience/personalization/customize-the-taskbar-in-windows)
- [Microsoft — Application Desktop Toolbars / AppBars](https://learn.microsoft.com/en-us/windows/win32/shell/application-desktop-toolbars)
- [Windhawk — Taskbar Styler](https://github.com/ramensoftware/windows-11-taskbar-styling-guide)
- [Windhawk — injection et processus cibles](https://github.com/ramensoftware/windhawk/wiki/Injection-targets-and-critical-system-processes)
- [Windhawk — récupération en mode sans échec](https://ramensoftware.com/windhawk-v1-3-beta)

Les capacités natives et les projets de modification évoluent avec Windows. Les documents techniques et signalements de compatibilité justifient une réserve, pas une estimation de taux de panne.

---

## Décision provisoire

- Conserver ces deux idées comme **chantiers d’exploration séparés** ; pas de promesse de livraison ni d’ordre de priorité ferme.
- Tester d’abord la demande, les modèles d’usage et les critères d’arrêt. Pour le partage distant, commencer par un concept en lecture seule ; pour la personnalisation, commencer par des maquettes de surfaces optionnelles sans injection Explorer.
- Une éventuelle réalisation nécessitera un périmètre approuvé, une architecture, des critères d’acceptation et une revue de sécurité adaptés à chaque chantier.

## Références au projet

- [Feuille d’idées et filtre de nécessité](2026-10-16-idees-et-filtre-produit.md)
- [Direction de SpaceNotch — 6 octobre 2026](2026-10-06-direction.md)
- [Documentation de l’API de greffons](../plugin-api.md)
