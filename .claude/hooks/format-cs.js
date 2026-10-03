// PostToolUse (Edit|Write) : remet aux règles de .editorconfig les espaces du
// fichier .cs que Claude vient de modifier.
//
// Mode dossier (--folder) : la solution n'est pas chargée, ce qui ramène
// l'opération de dizaines de secondes à deux ou trois. Les espaces suffisent ici :
// les avertissements d'analyseur, eux, sont attrapés par le build Release de fin
// de tour (release-build.js).
//
// Au mieux : un échec de formatage ne doit jamais bloquer une modification.
const { execFileSync } = require('child_process');
const path = require('path');

// Un chemin à la Git Bash (/c/Users/…) est lu par Node comme C:\c\Users\… :
// le ramener à la forme Windows avant toute comparaison.
function windowsPath(p) {
  return p.replace(/^\/([a-zA-Z])\//, '$1:/');
}

let input = '';
process.stdin.on('data', (chunk) => { input += chunk; });
process.stdin.on('end', () => {
  let file;
  try {
    file = JSON.parse(input).tool_input?.file_path;
  } catch {
    return;
  }

  if (!file || !file.toLowerCase().endsWith('.cs')) {
    return;
  }

  const root = windowsPath(process.env.CLAUDE_PROJECT_DIR || process.cwd());
  const relative = path.relative(root, path.resolve(windowsPath(file)));

  // Hors du dépôt (scratchpad, autre projet) : pas notre .editorconfig.
  if (relative.startsWith('..') || path.isAbsolute(relative)) {
    return;
  }

  try {
    execFileSync('dotnet', ['format', 'whitespace', root, '--folder', '--include', relative], {
      stdio: 'ignore',
      timeout: 60000,
    });
  } catch {
    // Au mieux, voir plus haut.
  }
});
