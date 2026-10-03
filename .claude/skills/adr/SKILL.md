---
name: adr
description: Écrit une décision d'architecture (ADR) SpaceNotch en français au numéro suivant, avec le gabarit de docs/decisions/README.md, et l'ajoute à l'index. À utiliser quand une décision engage l'architecture, une dépendance, la plateforme ou le comportement visible de la notch.
argument-hint: <la décision en une phrase>
---

# Nouvelle décision d'architecture

Décision : $ARGUMENTS

## 1. Numéro et nom

- Lister `docs/decisions/ADR-*.md` et prendre **le plus grand numéro + 1**, sur trois chiffres. Ne
  pas combler les trous (012 à 016 n'existent pas) ni réutiliser un numéro (011 existe deux fois,
  c'est un héritage, pas un modèle).
- Fichier : `docs/decisions/ADR-0NN-<slug-court-en-francais>.md` (minuscules, tirets, sans accents).

## 2. Contenu

Lire d'abord l'ADR le plus récent pour le ton, puis écrire :

```markdown
# ADR-0NN — <Titre de la décision>

**Statut** : Proposé

## Contexte

## Décision

## Justification

## Conséquences

## Alternatives écartées
```

Règles de `docs/decisions/README.md` :
- **Contexte** : le problème et ses contraintes réelles (mesures, versions, défauts observés).
- **Justification** : pourquoi, y compris ce qui a été mesuré ou vérifié.
- **Conséquences** : ce que la décision rend obligatoire, **y compris ce qui devient plus
  difficile**. Une décision sans inconvénient est une décision mal analysée.
- **Alternatives écartées** : chacune avec la raison du rejet, pas seulement son nom.
- Statut `Proposé` tant que l'utilisateur ne l'a pas validée ; `Accepté (vX.Y.Z)` une fois livrée.
- Citer les fichiers et ADR concernés par leur chemin.

Ne rien inventer : une mesure ou une version qui n'a pas été vérifiée s'écrit comme une hypothèse.

## 3. Index

Ajouter la ligne dans le tableau de `docs/decisions/README.md`, dans l'ordre des numéros :

```markdown
| [ADR-0NN](ADR-0NN-slug.md) | <Décision en une ligne> | Proposé |
```

Si la décision change la manière de lire le code, compléter aussi « Où regarder d'abord ».
