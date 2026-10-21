'use strict';

const { spawn } = require('node:child_process');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');

// What `keypaste setup` registers for Claude Desktop under the default home, which follows the chosen vault (D-0389, D-0446).
const bridgeArguments = ['mcp', '--client-label', 'claude-desktop', '--allow-run', '--expose', '**'];

// Path.IsPathFullyQualified's rule (T-40): on Windows a rooted entry such as \tools still depends on the current drive.
function isFullyQualified(directory) {
  return process.platform === 'win32'
    ? /^[A-Za-z]:[\\/]/.test(directory) || /^[\\/]{2}[^\\/]/.test(directory)
    : path.isAbsolute(directory);
}

function onPath(name) {
  return (process.env.PATH || '')
    .split(path.delimiter)
    .filter(isFullyQualified)
    .map((directory) => path.join(directory, name));
}

function installedPlaces() {
  const home = os.homedir();

  if (process.platform === 'win32') {
    const localAppData = process.env.LOCALAPPDATA || path.join(home, 'AppData', 'Local');
    return [path.join(localAppData, 'Programs', 'keypaste', 'keypaste.exe')];
  }

  const bundled = path.join('keypaste.app', 'Contents', 'MacOS', 'keypaste');
  const apps = process.platform === 'darwin'
    ? [path.join('/Applications', bundled), path.join(home, 'Applications', bundled), '/usr/local/bin/keypaste', '/opt/homebrew/bin/keypaste']
    : [];

  return [...apps, path.join(home, '.local', 'bin', 'keypaste')];
}

function isProgram(file) {
  try {
    fs.accessSync(file, fs.constants.X_OK);
    return fs.statSync(file).isFile();
  } catch {
    return false;
  }
}

// Another admin can write /Applications and Homebrew's prefix, so an installed place counts only when the file it resolves to and every directory above it belong to root or this account (T-39).
function isOwnedHere(file) {
  if (process.platform === 'win32') {
    return true;
  }

  try {
    for (let place = fs.realpathSync(file); ; place = path.dirname(place)) {
      const owner = fs.statSync(place).uid;

      if (owner !== 0 && owner !== process.getuid()) {
        return false;
      }

      if (place === path.dirname(place)) {
        return true;
      }
    }
  } catch {
    return false;
  }
}

const places = installedPlaces();
const keypaste = places.find((file) => isProgram(file) && isOwnedHere(file))
  ?? onPath(process.platform === 'win32' ? 'keypaste.exe' : 'keypaste').find(isProgram);

if (keypaste === undefined) {
  process.stderr.write(
    `keypaste is not installed: it is not in ${places.join(', ')} or on PATH. `
      + 'Install keypaste (https://keypaste.com), then restart Claude.\n');
  process.exitCode = 1;
} else {
  const bridge = spawn(keypaste, bridgeArguments, { stdio: 'inherit', windowsHide: true });

  for (const signal of ['SIGINT', 'SIGTERM', 'SIGHUP']) {
    process.on(signal, () => bridge.kill(signal));
  }

  bridge.on('error', (error) => {
    process.stderr.write(`keypaste could not start ${keypaste}: ${error.message}\n`);
    process.exitCode = 1;
  });
  bridge.on('exit', (code) => {
    process.exitCode = code ?? 1;
  });
}
