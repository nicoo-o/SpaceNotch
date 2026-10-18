// SessionStart : donne à Claude, dès le premier tour, l'état réel du dépôt.
//
// L'auteur travaille sur plusieurs PC : la copie locale peut avoir plusieurs
// commits de retard, ou du travail jamais poussé. Le savoir avant de modifier
// quoi que ce soit évite de repartir d'une base périmée. Le hook le fait de
// façon déterministe, au lieu de compter sur une consigne que Claude peut
// oublier : une centaine de tokens, `git fetch` compris.
//
// Au mieux : sans réseau, hors dépôt ou sur la moindre erreur, le hook se tait
// et sort en 0 — il ne doit jamais retarder ni bloquer le démarrage. Il ne
// formule que des faits (la doc des hooks déconseille un texte qui ressemble à
// une commande système).
const { execFileSync } = require('child_process');
const fs = require('fs');
const path = require('path');

// Un chemin à la Git Bash (/c/Users/…) est lu par Node comme C:\c\Users\….
const root = (process.env.CLAUDE_PROJECT_DIR || process.cwd()).replace(/^\/([a-zA-Z])\//, '$1:/');

function git(args, timeout = 10000) {
  return execFileSync('git', args, {
    cwd: root,
    encoding: 'utf8',
    timeout,
    stdio: ['ignore', 'pipe', 'ignore'],
    env: { ...process.env, GIT_TERMINAL_PROMPT: '0' },
  }).trim();
}

// Les relais se nomment relais-AAAA-MM-JJ[-suffixe].md ; dans une même journée,
// l'ordre est : sans suffixe (jour), « soir », « nuit ». Le tri par nom ne le
// donne pas (« - » précède « . »).
function lastRelay() {
  const dir = path.join(root, 'docs', 'plans');
  const rank = { soir: 1, nuit: 2 };
  const relays = fs.readdirSync(dir)
    .map((f) => /^relais-(\d{4}-\d{2}-\d{2})(?:-(.+))?\.md$/.exec(f))
    .filter(Boolean)
    .map((m) => ({ file: m[0], key: m[1] + String(m[2] ? rank[m[2]] ?? 1 : 0) }))
    .sort((a, b) => a.key.localeCompare(b.key));
  return relays.length ? relays[relays.length - 1].file : null;
}

function plural(n, one, many) {
  return `${n} ${n > 1 ? many : one}`;
}

function main() {
  const branch = git(['rev-parse', '--abbrev-ref', 'HEAD']);

  let fetched = true;
  try {
    git(['fetch', '--quiet'], 8000);
  } catch {
    fetched = false;
  }

  let sync = 'retard et avance non relevés (pas de origin/main)';
  try {
    const [behind, ahead] = git(['rev-list', '--left-right', '--count', 'origin/main...HEAD']).split(/\s+/).map(Number);
    sync = `${plural(behind, 'commit', 'commits')} de retard et ${plural(ahead, 'commit', 'commits')} d'avance sur origin/main`
      + (fetched ? '' : ' (dernier fetch réussi, réseau indisponible maintenant)');
  } catch {
    // Pas de origin/main : le message de repli suffit.
  }

  const dirty = git(['status', '--porcelain', '--untracked-files=all']).split('\n').filter(Boolean).length;
  const relay = lastRelay();

  const lines = [
    `État du dépôt au démarrage : branche ${branch}, ${sync}.`,
    dirty === 0
      ? 'Arbre de travail propre.'
      : `${plural(dirty, 'fichier modifié ou non suivi', 'fichiers modifiés ou non suivis')} (à préserver).`,
  ];

  if (relay) {
    lines.push(`Dernier relais de session : docs/plans/${relay}.`);
  }

  process.stdout.write(lines.join(' ') + '\n');
}

// Le hook lit son entrée pour la vider, mais n'en a pas besoin.
process.stdin.resume();
process.stdin.on('data', () => {});
process.stdin.on('end', () => {
  try {
    main();
  } catch {
    // Au mieux, voir plus haut.
  }
});
