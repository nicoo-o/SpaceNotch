# Scénarios d'accès aux états — avant / après (2026-10-05)

Un utilisateur qui ne sait rien de SpaceNotch veut faire quelque chose. Pour chaque cas, le geste
qu'un néophyte tente en premier, ce qui se passait avant la session et ce qui se passe avec les
PR ouvertes (#39, #43, #44). Direction choisie : [ADR-028](../decisions/ADR-028-acces-tableau-de-bord-et-gestes-enseignes.md).

« Après » décrit le comportement des branches, vérifié en instance de dev ou par les tests cités
dans les PR. Ce n'est pas un test utilisateur : à rejouer avec un vrai néophyte avant de vendre.

| Sans rien savoir, j'essaie de… | Geste tenté en premier | Avant | Après |
|---|---|---|---|
| lancer un minuteur | clic sur la notch | la recherche s'ouvre ; le minuteur n'est qu'au clic droit | la recherche s'ouvre avec la tuile **Minuteur** (15 min) ; s'il tourne déjà, la tuile le montre |
| écrire une note rapide | clic sur la notch | recherche seule ; la note n'est qu'au double-clic ou au clic droit | tuile **Note** |
| retrouver un texte copié | clic sur la notch | recherche seule ; la carte du presse-papier restait affichée sans fin (constat 3) | tuile **Presse-papier** (désactivée quand il est vide) ; après une copie, un signal bref « Copié · N », puis la copie attend dans la pile (#39) |
| découvrir ce que fait la notch | clic sur la notch | recherche seule | recherche et quatre tuiles, dont **Plus** (le menu rapide complet) |
| changer le volume pendant la musique | molette sur la notch | marchait, mais rien ne le disait (aide cachée derrière Alt) | à la première musique, « Molette · volume » s'affiche 4 s dans la notch ; plus jamais une fois le geste utilisé |
| parcourir les copies | molette | rien (le geste est Ctrl + molette, introuvable) | quand le presse-papier est présenté, « Ctrl + molette · la pile » s'affiche une fois |
| fermer ce qui est ouvert | clic ailleurs ou Échap | marchait | inchangé |
| tout faire au clavier | raccourci de la recherche, puis Tab | Tab parcourait les résultats | sur la recherche vide, Tab entre dans les tuiles, ←/→ les parcourent, ↑ ou Échap rendent le champ |

Restent sans chemin visible (feuille de route) : détacher et raccrocher la notch, l'étagère, la
présentation du premier lancement (rejouable seulement depuis les Réglages).
