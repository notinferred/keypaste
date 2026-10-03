#!/usr/bin/env python3
"""Records the demo GIF's session from the real binaries, as a tape for render-gif.py.

It plays docs/demo.md's scenario with a scripted MCP client in place of Claude, sending the calls
verify-demo.sh sends: `keypaste agent` runs on a real pseudo-terminal, so what it prints, colours
included, is what a person's terminal receives; the client asks for the fixture credential twice on
one connection, the person answers o and then d, and `keypaste log` closes it. The waits below are
the recording's pacing, and every timestamp in the tape is real. `--home /home/you` gives the paths
docs/demo.md prints.

It needs a pty, so Linux or macOS. On Windows, build, capture in the SDK container from PowerShell,
and render on the host:

  dotnet build keypaste.slnx -c Release
  docker run --rm -v "${PWD}:/src" -w /src mcr.microsoft.com/dotnet/sdk:10.0.302 bash -c 'apt-get update -qq && apt-get install -y -qq python3 >/dev/null && python3 scripts/demo/capture-demo.py --home /home/you --keypaste "dotnet artifacts/bin/Keypaste.Cli/release/keypaste.dll" --keypaste-mcp "dotnet artifacts/bin/Keypaste.Mcp/release/keypaste-mcp.dll" --out artifacts/demo/tape.json'
  python scripts/demo/render-gif.py artifacts/demo/tape.json docs/demo/keypaste-demo.gif
"""
import argparse
import codecs
import fcntl
import json
import os
import pty
import shlex
import shutil
import struct
import subprocess
import sys
import tempfile
import termios
import threading
import time

MASTER = "demo-master-pw"
SECRET = "sk_test_EXAMPLE_ONLY_not_a_real_key_0000"
ENTRY = "env/demo/STRIPE_KEY"
REASON = "deploy the billing service to staging"
CLIENT = "claude-code"
COLUMNS, ROWS = 103, 20
TYPING_CPS = 30


class Tape:
    def __init__(self):
        self.started = time.monotonic()
        self.events = []
        self.lock = threading.Lock()

    def add(self, pane, kind, **fields):
        with self.lock:
            self.events.append({"t": round(time.monotonic() - self.started, 3), "pane": pane, "type": kind, **fields})

    def typed(self, pane, command):
        self.add(pane, "command", text=command, cps=TYPING_CPS)
        time.sleep(len(command) / TYPING_CPS + 0.3)


class Terminal:
    """One program on a pseudo-terminal, its output recorded as it arrives."""

    def __init__(self, tape, argv, env):
        self.tape = tape
        self.text = ""
        self.changed = threading.Condition()
        self.pid, self.fd = pty.fork()
        if self.pid == 0:
            os.execvpe(argv[0], argv, env)
        fcntl.ioctl(self.fd, termios.TIOCSWINSZ, struct.pack("HHHH", ROWS, COLUMNS, 0, 0))
        self.reader = threading.Thread(target=self._read, daemon=True)
        self.reader.start()

    def _read(self):
        decoder = codecs.getincrementaldecoder("utf-8")()
        while True:
            try:
                data = os.read(self.fd, 4096)
            except OSError:
                data = b""
            if not data:
                break
            chunk = decoder.decode(data)
            if chunk:
                self.tape.add("top", "output", text=chunk)
                with self.changed:
                    self.text += chunk
                    self.changed.notify_all()
        with self.changed:
            self.changed.notify_all()

    def wait_for(self, needle, after=None, count=1, timeout=60):
        """Waits until needle appears, following the count-th occurrence of after when one is given."""

        def seen():
            start = 0
            if after is not None:
                for _ in range(count):
                    start = self.text.find(after, start)
                    if start < 0:
                        return False
                    start += len(after)
            return needle in self.text[start:]

        with self.changed:
            if not self.changed.wait_for(seen, timeout):
                sys.exit(f"capture: timed out waiting for {needle!r}; so far:\n{self.text}")

    def press(self, keys, secret=False):
        if secret:
            self.tape.add("top", "secret")
        else:
            self.tape.add("top", "key", key=keys)
        os.write(self.fd, keys.encode())

    def finish(self):
        _, status = os.waitpid(self.pid, 0)
        self.reader.join(timeout=5)
        os.close(self.fd)
        return os.waitstatus_to_exitcode(status)


class McpClient:
    """The stand-in for Claude: a transport that sends exactly the calls docs/demo.md describes."""

    def __init__(self, tape, argv, env, stderr):
        self.tape = tape
        self.replies = {}
        self.arrived = threading.Condition()
        self.process = subprocess.Popen(argv, stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=stderr, env=env)
        threading.Thread(target=self._read, daemon=True).start()

    def _read(self):
        for line in self.process.stdout:
            message = json.loads(line)
            self.tape.add("bottom", "receive", message=message)
            with self.arrived:
                self.replies[message.get("id")] = message
                self.arrived.notify_all()

    def send(self, message):
        self.tape.add("bottom", "send", message=message)
        self.process.stdin.write((json.dumps(message) + "\n").encode())
        self.process.stdin.flush()

    def call(self, message, timeout=90):
        self.send(message)
        with self.arrived:
            if not self.arrived.wait_for(lambda: message["id"] in self.replies, timeout):
                sys.exit(f"capture: no reply to request {message['id']}")
        return self.replies[message["id"]]

    def close(self):
        self.process.stdin.close()
        self.process.wait(timeout=30)


def request(request_id):
    return {
        "jsonrpc": "2.0",
        "id": request_id,
        "method": "tools/call",
        "params": {
            "name": "request_credential",
            "arguments": {"entry": ENTRY, "field": "password", "reason": REASON, "ttl_seconds": 900},
        },
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--keypaste", default="artifacts/bin/Keypaste.Cli/release/keypaste")
    parser.add_argument("--keypaste-mcp", default="artifacts/bin/Keypaste.Mcp/release/keypaste-mcp")
    parser.add_argument("--home", help="an empty directory to use as HOME (default: a new temporary one)")
    parser.add_argument("--out", required=True)
    options = parser.parse_args()

    keypaste = shlex.split(options.keypaste)
    keypaste_mcp = shlex.split(options.keypaste_mcp)
    home = options.home or tempfile.mkdtemp(prefix="keypaste-demo-")
    if os.path.exists(os.path.join(home, ".keypaste")):
        sys.exit(f"capture: {home} already has a .keypaste directory; use an empty HOME")
    os.makedirs(home, exist_ok=True)
    vault = os.path.join(home, "keypaste-demo.kdbx")

    env = {
        "PATH": os.environ.get("PATH", "/usr/bin:/bin"),
        "HOME": home,
        "LANG": "C.UTF-8",
        "TERM": "xterm-256color",
        "COLORTERM": "truecolor",
    }
    if "DOTNET_ROOT" in os.environ:
        env["DOTNET_ROOT"] = os.environ["DOTNET_ROOT"]

    def setup(*args, stdin):
        subprocess.run(keypaste + list(args), input=stdin.encode(), env=env, check=True, capture_output=True)

    setup("init", vault, stdin=f"{MASTER}\n{MASTER}\n")
    setup("add", ENTRY, "--vault", vault, stdin=f"{MASTER}\n{SECRET}\n")

    tape = Tape()
    tape.add("top", "prompt")
    time.sleep(0.6)
    tape.typed("top", "keypaste agent --vault ~/keypaste-demo.kdbx")
    agent = Terminal(tape, keypaste + ["agent", "--vault", vault], env)
    agent.wait_for("Master password:")
    time.sleep(0.9)
    agent.press(MASTER + "\r", secret=True)
    agent.wait_for("Press Ctrl+C to stop.")
    time.sleep(1.6)

    with open(os.path.join(home, "keypaste-mcp.stderr"), "wb") as mcp_stderr:
        client = McpClient(tape, keypaste_mcp + ["--vault", vault, "--client-label", CLIENT], env, mcp_stderr)
        client.call({
            "jsonrpc": "2.0",
            "id": 1,
            "method": "initialize",
            "params": {"protocolVersion": "2025-11-25", "capabilities": {}, "clientInfo": {"name": CLIENT, "version": "2.0.0"}},
        })
        client.send({"jsonrpc": "2.0", "method": "notifications/initialized"})
        time.sleep(0.4)

        answers = []
        for asked, (request_id, key, reading, hold) in enumerate(((2, "o", 4.5, 3.5), (3, "d", 2.6, 3.0)), start=1):
            pending = threading.Thread(target=lambda rid=request_id: answers.append(client.call(request(rid))))
            pending.start()
            agent.wait_for("[d] deny", after="an agent is asking for a credential.", count=asked)
            time.sleep(reading)
            agent.press(key)
            pending.join()
            time.sleep(hold)

        client.close()

    time.sleep(0.6)
    agent.press("\x03")
    if agent.finish() != 0:
        sys.exit("capture: keypaste agent did not stop cleanly")

    time.sleep(0.5)
    tape.add("top", "prompt")
    time.sleep(0.5)
    tape.typed("top", "keypaste log")
    log = Terminal(tape, keypaste + ["log"], env)
    log.finish()
    tape.add("top", "prompt")

    once, refused = sorted(answers, key=lambda reply: reply["id"])
    if once["result"].get("isError") or once["result"]["structuredContent"]["value"] != SECRET:
        sys.exit("capture: the request allowed once did not return the fixture credential")
    if once["result"]["structuredContent"]["expires_in_seconds"] != 0:
        sys.exit("capture: the request allowed once reported a lifetime")
    if not refused["result"].get("isError") or SECRET in json.dumps(refused):
        sys.exit("capture: the refused request was not refused")

    shown = agent.text + log.text
    for leak, what in ((MASTER, "the master password"), (SECRET, "the credential")):
        if leak in shown:
            sys.exit(f"capture: {what} reached a terminal")

    os.makedirs(os.path.dirname(os.path.abspath(options.out)), exist_ok=True)
    with open(options.out, "w", encoding="utf-8") as out:
        json.dump({"columns": COLUMNS, "rows": ROWS, "events": tape.events}, out, indent=1, ensure_ascii=False)
    print(f"capture: {len(tape.events)} events, {tape.events[-1]['t']:.1f}s, written to {options.out}")
    if not options.home:
        shutil.rmtree(home, ignore_errors=True)


if __name__ == "__main__":
    main()
