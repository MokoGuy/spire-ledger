#!/usr/bin/env python3
"""Drive Slay the Spire 2 for local Spire Ledger tests through a locally built, hardened STS2MCP mod.

STS2MCP (github.com/Gennadiyev/STS2MCP, patched in K:/Projets/sts2-tools/STS2MCP on branch local-hardening)
exposes game state and actions on http://127.0.0.1:15526. It is only copied into mods/ for a test session
(`install`) and removed afterwards (`uninstall`), so normal play never runs it.

Usage: python scripts/dev/sts2.py <command> [args]
  install | uninstall          copy/remove STS2_MCP.dll + manifest in the game's mods/ folder
  deploy                       copy the built SpireLedger.dll + manifest into mods/
  launch | quit                start the game (waits for the API) / close it
  state                        print the singleplayer state (markdown)
  act <action> [k=v ...]       POST an action, e.g. act play_card card_index=0 target=JAW_WORM_0
  console <command...>         run a dev-console command, e.g. console fight JAW_WORM
  shot <name>                  save the viewport to %TEMP%/sts2shots/<name>.png
  log [pattern]                print [SpireLedger] lines (or lines matching pattern) from the game log
"""
import json
import os
import re
import shutil
import subprocess
import sys
import time
import urllib.error
import urllib.request

GAME_DIR = os.environ.get("STS2GameDir", r"D:\FastSteamLibrary\steamapps\common\Slay the Spire 2")
MODS = os.path.join(GAME_DIR, "mods")
REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
MCP_OUT = r"K:\Projets\sts2-tools\STS2MCP\out\STS2_MCP"
MCP_MANIFEST = r"K:\Projets\sts2-tools\STS2MCP\mod_manifest.json"
PORT = int(os.environ.get("STS2MCP_PORT", "15526"))
BASE = f"http://127.0.0.1:{PORT}"
LOG = os.path.join(os.environ["APPDATA"], "SlayTheSpire2", "logs", "godot.log")


def http(method, path, body=None, timeout=30):
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(BASE + path, data=data, method=method,
                                 headers={"Content-Type": "application/json"} if data else {})
    with urllib.request.urlopen(req, timeout=timeout) as r:
        raw = r.read().decode("utf-8")
    try:
        return json.loads(raw)
    except ValueError:
        return raw


def api_up():
    try:
        http("GET", "/", timeout=2)
        return True
    except (urllib.error.URLError, OSError):
        return False


def running():
    out = subprocess.run(["tasklist", "/FI", "IMAGENAME eq SlayTheSpire2.exe"], capture_output=True, text=True).stdout
    return "SlayTheSpire2.exe" in out


def install():
    shutil.copy2(os.path.join(MCP_OUT, "STS2_MCP.dll"), os.path.join(MODS, "STS2_MCP.dll"))
    shutil.copy2(MCP_MANIFEST, os.path.join(MODS, "STS2_MCP.json"))
    print("STS2MCP installed in", MODS)


def uninstall():
    for name in ("STS2_MCP.dll", "STS2_MCP.json"):
        p = os.path.join(MODS, name)
        if os.path.exists(p):
            os.remove(p)
    print("STS2MCP removed from", MODS)


def deploy():
    if running():
        sys.exit("game is running; quit it first (the DLL is memory-mapped)")
    shutil.copy2(os.path.join(REPO, "out", "SpireLedger.dll"), os.path.join(MODS, "SpireLedger.dll"))
    shutil.copy2(os.path.join(REPO, "mod_manifest.json"), os.path.join(MODS, "SpireLedger.json"))
    print("SpireLedger deployed")


def launch(timeout=120):
    if not running():
        # Through Steam: launching the exe directly fails Steamworks init ("Erreur Steam").
        os.startfile("steam://rungameid/2868840")
    end = time.time() + timeout
    while time.time() < end:
        if api_up():
            print("API up")
            return
        time.sleep(2)
    sys.exit("API did not come up (is STS2MCP installed and the modded save profile chosen?)")


def quit_game(timeout=30):
    if not running():
        return
    subprocess.run(["taskkill", "/IM", "SlayTheSpire2.exe"], capture_output=True)
    end = time.time() + timeout
    while running() and time.time() < end:
        time.sleep(1)
    if running():
        subprocess.run(["taskkill", "/F", "/IM", "SlayTheSpire2.exe"], capture_output=True)
    print("game closed")


def parse_kv(args):
    out = {}
    for a in args:
        k, _, v = a.partition("=")
        out[k] = int(v) if re.fullmatch(r"-?\d+", v) else v
    return out


def act(action, **params):
    return http("POST", "/api/v1/singleplayer", {"action": action, **params})


def main(argv):
    if not argv:
        print(__doc__)
        return
    cmd, rest = argv[0], argv[1:]
    if cmd == "install":
        install()
    elif cmd == "uninstall":
        uninstall()
    elif cmd == "deploy":
        deploy()
    elif cmd == "launch":
        launch()
    elif cmd == "quit":
        quit_game()
    elif cmd == "state":
        print(http("GET", "/api/v1/singleplayer?format=markdown"))
    elif cmd == "act":
        print(json.dumps(act(rest[0], **parse_kv(rest[1:])), indent=2, ensure_ascii=False))
    elif cmd == "console":
        print(json.dumps(act("console", command=" ".join(rest)), indent=2, ensure_ascii=False))
    elif cmd == "shot":
        print(json.dumps(act("screenshot", name=rest[0] if rest else "shot"), indent=2))
    elif cmd == "log":
        pat = re.compile(rest[0] if rest else r"\[SpireLedger\]")
        with open(LOG, encoding="utf-8", errors="replace") as f:
            for line in f:
                if pat.search(line):
                    print(line.rstrip())
    else:
        sys.exit(f"unknown command {cmd}")


if __name__ == "__main__":
    main(sys.argv[1:])
