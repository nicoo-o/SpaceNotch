# Plan de session du 2026-10-05 — fusion, corrections, direction

Brief : `docs/prompts/2026-10-05-fusion-corrections-direction.md` (PR #36). Les numéros « n° » sont
ceux de `docs/plans/2026-10-04-feuille-de-route.md`.

## Jalon 1 — Fusionner

- [x] #40 Mise à jour : seule la copie installée se met à jour — relue, CI verte, fusionnée (`a9fb4b5`)
- [x] #37 Mesure `--frames`, constats, feuille de route — relue, CI verte, fusionnée (`5a6acba`)
- [x] #38 Repos étroit — relue, CI verte, fusionnée (`21529b0`)
- [x] #39 Copie discrète — relue, corrigée sur sa branche (`d11875c` : liste du signal figée, coller
      ne refermait pas), CI verte, fusionnée (`2a1320f`)
- [x] #41 Fluidité — trois relecteurs + `/code-review` ; corrigée sur sa branche (`295ff53` : scène à
      onglets qui remontait de 34 DIP au repli) ; mise à jour depuis `main` (`458e9b0`) ; CI verte ;
      fusionnée (`54ffc96`)
- [x] #42 Matière masquée — `main` + #42 construit et testé en local, fusionnée (`2497ada`)
- [x] #43 Tableau de bord — `main` + #43 construit et testé en local, fusionnée (`474bb3b`)
- [x] #44 Gestes enseignés — corrigée sur sa branche (`afcb0d5` : leçon et geste désaccordés) ;
      ADR-028 « Accepté » et index (`cb896a2`) ; mise à jour depuis `main` (`068b78b`) ; fusionnée (`b06172b`)
- [x] #36 Skill `/prompt-opus` et briefs — gabarit corrigé (`86712f0`), fusionnée (`560f241`)
- [x] `main` (`560f241`) construit en Release sans avertissement, 910 + 23 tests verts
- [x] Installation 1.17.1 relevée en lecture seule et comparée aux choix de l'utilisateur
- [x] Correction de l'installation : démarrage réactivé (StartupApproved 03 → 02), avec ton accord

## Jalon 2 — Corriger (une branche et une PR par sujet)

- [x] n° 33 Exception non gérée — PR #45 (minuteurs, travail posté, async void, minuteurs des fonctionnalités ;
      preuve `--fault-test` sur la vraie app)
- [x] n° 7 Mise à jour ratée — PR #46 (relance dans tous les cas, carte, report ; preuve `--fault-install`)
- [ ] n° 47 CPU quand un agent travaille (Clawd, reflet) — seuil à faire confirmer
- [ ] n° 46 Pauses du GC pendant l'animation — plan d'abord (mode plan)
- [ ] n° 48–50 Minuteur sans nom UIA, animations notch retirée, menu rapide aligné sur les tuiles

## Jalon 3 — Direction

- [ ] Feuille de route re-triée pour un projet gratuit
- [ ] Enquête (workflow, une dimension par agent)
- [ ] Choix de fond posés à l'utilisateur
- [ ] `docs/plans/2026-10-05-direction.md`, README, page GitHub (proposés puis appliqués)

## Clôture

- [ ] Relais `docs/plans/relais-2026-10-05-b.md` avec le prochain brief
- [ ] Mémoire
- [ ] Rapport final
