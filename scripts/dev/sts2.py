#!/usr/bin/env python3
r"""Drive Slay the Spire 2 for local Spire Ledger tests through a locally built, hardened STS2MCP mod.

STS2MCP (github.com/Gennadiyev/STS2MCP, patched in K:/Projets/sts2-tools/STS2MCP on branch local-hardening)
exposes game state and actions on http://127.0.0.1:15526. It is only copied into mods/ for a test session
(`install`) and removed afterwards (`uninstall`), so normal play never runs it.

Usage: python scripts/dev/sts2.py <command> [args]
  install | uninstall          copy/remove STS2_MCP.dll + manifest in the game's mods/ folder
  deploy                       copy the built SpireLedger.dll + manifest into mods/
  launch | quit                start / close the test copy (never the player's own game): Steam-less,
                               saves under %APPDATA%/SlayTheSpire2/default, mod data in SpireLedger/local
  launch-local [args...]       same, with game arguments, on STS2MCP_PORT, e.g. --fastmp host_custom
  refresh-copy                 re-copy the Steam install into the test copy (after a game update)
  The test copy is STS2_TEST_DIR (K:\STS2-test); SPIRELEDGER_HISTORY_NETID=<SteamID64> reads the copied
  game history as that player (the Steam-less instance plays as id 1).
  Set STS2MCP_PORT to talk to another instance and STS2_API=multiplayer for co-op state/actions.
  state                        print the singleplayer state (markdown)
  act <action> [k=v ...]       POST an action, e.g. act play_card card_index=0 target=JAW_WORM_0
  console <command...>         run a dev-console command, e.g. console fight JAW_WORM
  shot <name>                  save the viewport to %TEMP%/sts2shots/<name>.png
  fight [max_rounds]           auto-play the current fight: attacks on the weakest enemy, skills on self,
                               then end turn, until the fight is over (simple, deterministic policy)
  autoplay FLOOR [pick=I]      play the run until FLOOR is reached (fights, map, events, rewards, rest,
                               shop) with simple deterministic choices; pick=I takes card reward I
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

# A separate copy of the game, launched Steam-less only: the player's own Steam install keeps the
# Workshop version and is never touched (refresh the copy after a game update: refresh-copy).
GAME_DIR = os.environ.get("STS2_TEST_DIR", r"K:\STS2-test")
STEAM_GAME_DIR = os.environ.get("STS2GameDir", r"D:\FastSteamLibrary\steamapps\common\Slay the Spire 2")
MODS = os.path.join(GAME_DIR, "mods")
REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
MCP_OUT = r"K:\Projets\sts2-tools\STS2MCP\out\STS2_MCP"
MCP_MANIFEST = r"K:\Projets\sts2-tools\STS2MCP\mod_manifest.json"
PORT = int(os.environ.get("STS2MCP_PORT", "15526"))
BASE = f"http://127.0.0.1:{PORT}"
API = "/api/v1/" + os.environ.get("STS2_API", "singleplayer")
LOG = os.path.join(os.environ["APPDATA"], "SlayTheSpire2", "logs", "godot.log")


def http(method, path, body=None, timeout=30):
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(BASE + path, data=data, method=method,
                                 headers={"Content-Type": "application/json"} if data else {})
    try:
        with urllib.request.urlopen(req, timeout=timeout) as r:
            raw = r.read().decode("utf-8")
    except urllib.error.HTTPError as e:  # 409 = wrong mode (singleplayer vs multiplayer), 4xx = bad action
        raw = e.read().decode("utf-8", "replace") or json.dumps({"status": "error", "error": f"HTTP {e.code}"})
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


def test_pids():
    """Process ids of game instances started from the test copy; the player's own game is never listed."""
    ps = ("Get-Process SlayTheSpire2 -ErrorAction SilentlyContinue | "
          f"Where-Object {{ $_.Path -like '{GAME_DIR}\\*' }} | ForEach-Object {{ $_.Id }}")
    out = subprocess.run(["powershell", "-NoProfile", "-Command", ps], capture_output=True, text=True).stdout
    return [int(x) for x in out.split() if x.isdigit()]


def running():
    return bool(test_pids())


def refresh_copy():
    """Re-copy the Steam install into the test folder (after a game update), keeping the test mods/."""
    if running():
        sys.exit("a test instance is running; quit it first")
    r = subprocess.run(["robocopy", STEAM_GAME_DIR, GAME_DIR, "/E", "/XD", "mods", "/NFL", "/NDL", "/NJH", "/NP", "/MT:8"])
    os.makedirs(MODS, exist_ok=True)
    print("test copy refreshed" if r.returncode < 8 else f"robocopy failed ({r.returncode})")


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
    # Always Steam-less from the test copy: a Steam launch would start the player's own game.
    launch_local([], timeout)


def launch_local(args, timeout=120):
    env = dict(os.environ, STS2MCP_PORT=str(PORT))
    subprocess.Popen([os.path.join(GAME_DIR, "SlayTheSpire2.exe"), "--force-steam", "off", *args], cwd=GAME_DIR,
                     env=env, creationflags=subprocess.DETACHED_PROCESS,
                     stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    end = time.time() + timeout
    while time.time() < end:
        if api_up():
            print(f"API up on {PORT}")
            return
        time.sleep(2)
    sys.exit(f"API did not come up on {PORT}")


def quit_game(timeout=30):
    if not running():
        return
    for pid in test_pids():
        subprocess.run(["taskkill", "/PID", str(pid)], capture_output=True)
    end = time.time() + timeout
    while running() and time.time() < end:
        time.sleep(1)
    for pid in test_pids():
        subprocess.run(["taskkill", "/F", "/PID", str(pid)], capture_output=True)
    print("game closed")


def parse_kv(args):
    out = {}
    for a in args:
        k, _, v = a.partition("=")
        out[k] = int(v) if re.fullmatch(r"-?\d+", v) else v
    return out


def act(action, **params):
    return http("POST", API, {"action": action, **params})


def get_json():
    return http("GET", API + "?format=json")


def auto_fight(max_rounds=30):
    """Deterministic policy so a seeded run replays identically: Vulnerable-applying and other attacks on
    the lowest-HP enemy, skills on self, end turn when nothing is playable."""
    for _ in range(max_rounds * 12):
        st = get_json()
        if st.get("state_type") not in ("monster", "elite", "boss"):
            return st.get("state_type")
        b = st.get("battle", {})
        if not b.get("is_play_phase") or b.get("turn") != "player":
            time.sleep(0.5)
            continue
        enemies = [e for e in b.get("enemies", []) if e.get("hp", 0) > 0]
        hand = [c for c in st["player"]["hand"] if c.get("can_play")]
        if not hand or not enemies:
            act("end_turn")
            time.sleep(2)
            continue
        card = sorted(hand, key=lambda c: (c["type"] != "Attack", c["index"]))[0]
        params = {"card_index": card["index"]}
        if card.get("target_type") == "AnyEnemy":
            params["target"] = min(enemies, key=lambda e: (e["hp"], e["entity_id"]))["entity_id"]
        r = act("play_card", **params)
        if r.get("status") != "ok":
            act("end_turn")
        time.sleep(1.2)
    return "timeout"


def ok(r):
    return isinstance(r, dict) and r.get("status") == "ok"


def try_indexes(action, key="index", n=4, **extra):
    for i in range(n):
        if ok(act(action, **{key: i}, **extra)):
            return True
    return False


def autoplay(target_floor, pick=0, max_steps=900):
    """Walk the run like a patient player: fight with the auto-fight policy, take the first map node /
    event option / rest option, claim every reward (card reward `pick`), leave shops. Stops on reaching
    `target_floor` outside combat, at game over, or after max_steps."""
    last, same = None, 0
    for _ in range(max_steps):
        try:
            st = get_json()
        except Exception:
            time.sleep(1); continue
        t = st.get("state_type")
        floor = (st.get("run") or {}).get("floor", 0)
        if t in ("game_over",):
            return "game_over"
        if floor >= target_floor and t in ("map", "event", "rewards", "rest_site", "shop", "treasure"):
            return f"floor {floor} ({t})"
        key = (t, floor, json.dumps(st.get("player", {}).get("hand", []))[:200])
        same = same + 1 if key == last else 0
        last = key
        if t in ("monster", "elite", "boss"):
            auto_fight(1)
        elif t == "rewards":
            r = st.get("rewards") or {}
            items = r.get("items") if isinstance(r, dict) else r
            done = False
            for i, it in enumerate(items or []):
                if ok(act("claim_reward", index=i)):
                    done = True; break
            if not done:
                act("proceed")
        elif t == "card_reward":
            if not ok(act("select_card_reward", card_index=pick)):
                act("skip_card_reward")
        elif t == "map":
            try_indexes("choose_map_node")
        elif t == "event":
            if not try_indexes("choose_event_option"):
                act("advance_dialogue"); act("proceed")
        elif t == "rest_site":
            if not try_indexes("choose_rest_option"):
                act("proceed")
        elif t in ("shop", "fake_merchant"):
            act("proceed")
        elif t == "treasure":
            if not try_indexes("claim_treasure_relic"):
                act("proceed")
        elif t == "relic_select":
            if not try_indexes("select_relic"):
                act("skip_relic_selection")
        elif t in ("card_select", "hand_select"):
            if not try_indexes("select_card") and not try_indexes("combat_select_card", key="card_index"):
                pass
            act("confirm_selection"); act("combat_confirm_selection")
        elif t == "bundle_select":
            try_indexes("select_bundle"); act("confirm_bundle_selection")
        else:
            act("proceed")
        if same > 25:  # stuck: nudge forward
            act("proceed"); act("advance_dialogue"); same = 0
        time.sleep(0.6)
    return "max_steps"


def main(argv):
    sys.stdout.reconfigure(encoding="utf-8")
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
    elif cmd == "launch-local":
        launch_local(rest)
    elif cmd == "refresh-copy":
        refresh_copy()
    elif cmd == "quit":
        quit_game()
    elif cmd == "state":
        print(http("GET", API + "?format=markdown"))
    elif cmd == "act":
        print(json.dumps(act(rest[0], **parse_kv(rest[1:])), indent=2, ensure_ascii=False))
    elif cmd == "console":
        print(json.dumps(act("console", command=" ".join(rest)), indent=2, ensure_ascii=False))
    elif cmd == "shot":
        print(json.dumps(act("screenshot", name=rest[0] if rest else "shot"), indent=2))
    elif cmd == "fight":
        print(auto_fight(int(rest[0]) if rest else 30))
    elif cmd == "autoplay":
        kv = parse_kv(rest[1:])
        print(autoplay(int(rest[0]), int(kv.get("pick", 0))))
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
