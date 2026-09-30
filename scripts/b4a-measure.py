"""B.4a experiment: size and start time of one keypaste binary against the two it replaces.

Usage: b4a-measure.py <one-dir> <two-dir> <warm-rounds> <cold-rounds> [cache-drop command...]
"""

import gzip
import lzma
import os
import statistics
import subprocess
import sys
import tempfile
import time

one_dir, two_dir = os.path.abspath(sys.argv[1]), os.path.abspath(sys.argv[2])
warm_rounds, cold_rounds = int(sys.argv[3]), int(sys.argv[4])
drop = sys.argv[5:]
suffix = ".exe" if os.name == "nt" else ""

one = os.path.join(one_dir, "keypaste" + suffix)
two_cli = os.path.join(two_dir, "keypaste" + suffix)
two_mcp = os.path.join(two_dir, "keypaste-mcp" + suffix)

print("== sizes in bytes")
print(f"{'binary':<24}{'file':>12}{'gzip -9':>12}{'xz -9':>12}")
for label, path in [("two: keypaste", two_cli), ("two: keypaste-mcp", two_mcp), ("one: keypaste", one)]:
    data = open(path, "rb").read()
    print(f"{label:<24}{len(data):>12}{len(gzip.compress(data, 9)):>12}{len(lzma.compress(data, preset=9)):>12}")

print("== names the SDK's HTTP transports would leave in a binary")
for label, path in [("two: keypaste", two_cli), ("two: keypaste-mcp", two_mcp), ("one: keypaste", one)]:
    data = open(path, "rb").read()
    counts = {name: data.count(name.encode()) for name in
              ["ModelContextProtocol", "StdioServerTransport", "HttpClientTransport", "StreamableHttp", "SseClient"]}
    print(f"{label:<24}{counts}")

work = tempfile.mkdtemp()
env = dict(os.environ, KEYPASTE_HOME=work)
bridge = ["--vault", os.path.join(work, "vault.kdbx"), "--audit-log", os.path.join(work, "audit.jsonl"), "--client-label", "b4a"]
initialize = b'{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"b4a","version":"1"}}}\n'


def reap(p):
    if hasattr(os, "wait4"):
        _, status, usage = os.wait4(p.pid, 0)
        p.returncode = os.waitstatus_to_exitcode(status)
        return usage.ru_maxrss // 1024 if sys.platform == "darwin" else usage.ru_maxrss
    p.wait()
    return 0


def to_exit(cmd):
    start = time.perf_counter()
    p = subprocess.Popen(cmd, stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, env=env)
    rss = reap(p)
    elapsed = time.perf_counter() - start
    assert p.returncode == 0, (cmd, p.returncode)
    return elapsed, rss


def to_initialize(cmd):
    start = time.perf_counter()
    p = subprocess.Popen(cmd, stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, env=env)
    p.stdin.write(initialize)
    p.stdin.flush()
    line = p.stdout.readline()
    elapsed = time.perf_counter() - start
    assert b'"id":1' in line, (cmd, line)
    p.stdin.close()
    p.stdout.close()
    return elapsed, reap(p)


cases = [
    ("cli --version", "two", to_exit, [two_cli, "--version"]),
    ("cli --version", "one", to_exit, [one, "--version"]),
    ("bridge --help", "two", to_exit, [two_mcp, "--help"]),
    ("bridge --help", "one", to_exit, [one, "mcp", "--help"]),
    ("bridge initialize", "two", to_initialize, [two_mcp, *bridge]),
    ("bridge initialize", "one", to_initialize, [one, "mcp", *bridge]),
]


def measure(label, rounds, cold):
    samples = {i: [] for i in range(len(cases))}
    for _ in range(rounds):
        for i, (_, _, how, cmd) in enumerate(cases):
            if cold:
                subprocess.run(drop, check=True, stdout=subprocess.DEVNULL)
            samples[i].append(how(cmd))
    print(f"== {label}: {rounds} rounds, cases interleaved")
    print(f"{'case':<20}{'layout':<8}{'median ms':>10}{'p10':>8}{'p90':>8}{'min':>8}{'max':>8}{'maxrss KiB':>12}")
    for i, (name, layout, _, _) in enumerate(cases):
        ms = sorted(s[0] * 1000 for s in samples[i])
        rss = statistics.median(s[1] for s in samples[i])
        q = statistics.quantiles(ms, n=10)
        print(f"{name:<20}{layout:<8}{statistics.median(ms):>10.1f}{q[0]:>8.1f}{q[-1]:>8.1f}{ms[0]:>8.1f}{ms[-1]:>8.1f}{rss:>12.0f}")


for _ in range(3):
    for _, _, how, cmd in cases:
        how(cmd)
measure("warm page cache", warm_rounds, cold=False)
if drop and cold_rounds:
    measure("file cache dropped before each start", cold_rounds, cold=True)
