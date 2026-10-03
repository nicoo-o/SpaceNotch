// Stop : construit la solution en Release quand le tour a touché du code.
//
// Release porte TreatWarningsAsErrors : un avertissement d'analyseur qui passe en
// Debug fait échouer la CI. Le vérifier une fois en fin de tour coûte bien moins
// que de le faire après chaque modification.
//
// Le build n'est relancé que si le code a changé depuis le dernier passage (empreinte
// des fichiers modifiés). Un échec renvoie les erreurs à Claude (code 2) une seule
// fois pour un même état : sans cette garde, un tour qui ne corrige rien bouclerait.
const { execFileSync, spawnSync } = require('child_process');
const crypto = require('crypto');
const fs = require('fs');
const os = require('os');
const path = require('path');

// Un chemin à la Git Bash (/c/Users/…) est lu par Node comme C:\c\Users\….
const root = (process.env.CLAUDE_PROJECT_DIR || process.cwd()).replace(/^\/([a-zA-Z])\//, '$1:/');
const code = /\.(cs|xaml|csproj|props|targets|editorconfig)$/i;

function git(args) {
  return execFileSync('git', args, { cwd: root, encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 });
}

function changedCode() {
  // -z : chemins bruts, sans guillemets ni échappement des accents.
  const entries = git(['status', '--porcelain', '-z', '--untracked-files=all']).split('\0').filter(Boolean);
  return entries.map((e) => e.slice(3)).filter((p) => code.test(p));
}

function fingerprint(paths) {
  const hash = crypto.createHash('sha1');
  hash.update(git(['rev-parse', 'HEAD']));

  for (const p of paths.sort()) {
    hash.update(p);
    try {
      hash.update(fs.readFileSync(path.join(root, p)));
    } catch {
      hash.update('<supprimé>');
    }
  }

  return hash.digest('hex');
}

function main() {
  let paths;
  try {
    paths = changedCode();
  } catch {
    return 0;
  }

  if (paths.length === 0) {
    return 0;
  }

  const stateFile = path.join(
    os.tmpdir(),
    'spacenotch-release-build-' + crypto.createHash('sha1').update(root).digest('hex').slice(0, 12) + '.json');
  const hash = fingerprint(paths);

  try {
    const state = JSON.parse(fs.readFileSync(stateFile, 'utf8'));
    if (state.hash === hash) {
      return 0;
    }
  } catch {
    // Premier passage.
  }

  const build = spawnSync('dotnet', [
    'build', 'SpaceNotch.sln', '-c', 'Release', '-p:Platform=x64',
    '-v', 'q', '-nologo', '-clp:ErrorsOnly;NoSummary',
  ], { cwd: root, encoding: 'utf8', maxBuffer: 64 * 1024 * 1024, timeout: 840000 });

  const output = `${build.stdout || ''}${build.stderr || ''}`;
  const ok = build.status === 0;
  fs.writeFileSync(stateFile, JSON.stringify({ hash, ok }));

  if (ok) {
    return 0;
  }

  // Une copie refusée vient d'un exécutable en cours d'utilisation (l'app lancée
  // depuis bin\x64\Release), pas du code : ce n'est pas à Claude de le corriger.
  if (/MSB30(21|26|27)/.test(output) && !/ error (CS|XLS|WMC)/.test(output)) {
    return 0;
  }

  const errors = [...new Set(output.split(/\r?\n/).map((l) => l.trim()).filter((l) => /error/i.test(l)))];
  process.stderr.write(
    'Le build Release (TreatWarningsAsErrors) échoue : la CI échouera aussi. Corriger avant de terminer.\n'
    + (errors.length ? errors.slice(0, 40).join('\n') : output.slice(-4000)) + '\n');
  return 2;
}

process.stdin.resume();
process.stdin.on('data', () => {});
process.stdin.on('end', () => { process.exitCode = main(); });
