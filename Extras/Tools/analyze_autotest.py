"""
Разбор логов автотеста: фазы матча, статистика, сверка контрольных сумм террейна
(каждый клиент должен получить ту же последовательность хэшей, что и хост) и поиск исключений.
"""
import re
import sys
from pathlib import Path

LOG_DIR = Path(sys.argv[1] if len(sys.argv) > 1 else Path(__file__).parent / "autotest_logs")
HASH_RE = re.compile(r"\[RocketWars\] terrain (explosion|history x\d+|build): round (\d+) hash ([0-9A-F]{8})")
EXC_RE = re.compile(r"(Exception|Error)[: ]", re.I)
IGNORE = ("Shader", "d3d", "Unable to find", "Fallback handler", "WebSocket", "Failed to present", "PlayerConnection")


def read(name):
    p = LOG_DIR / f"{name}.log"
    return p.read_text(encoding="utf-8", errors="replace").splitlines() if p.exists() else []


def final_hash_per_round(lines):
    """Последний хэш каждого раунда (после всех взрывов) — его и сверяем."""
    result = {}
    for line in lines:
        m = HASH_RE.search(line)
        if m:
            result[int(m.group(2))] = m.group(3)
    return result


def main():
    names = sorted(p.stem for p in LOG_DIR.glob("*.log"))
    host = final_hash_per_round(read("Host"))
    print(f"logs: {', '.join(names)}")
    print(f"host final hash per round: {host}")

    ok = True
    for name in names:
        lines = read(name)
        auto = [l for l in lines if "[AUTOTEST]" in l]
        print(f"\n=== {name}: {len(auto)} autotest lines")
        for l in auto:
            if any(k in l for k in ("phase", "stats", "DONE", "TIMEOUT", "disconnected", "starting")):
                print("  " + l.strip())

        errors = [l for l in lines if EXC_RE.search(l) and not any(i in l for i in IGNORE)]
        if errors:
            ok = False
            print(f"  !! {len(errors)} error lines, first: {errors[:3]}")

        if name == "Host":
            continue
        mine = final_hash_per_round(lines)
        finished = any("[AUTOTEST] DONE" in l for l in lines)
        if not finished and mine:
            # клиент вылетел/был убит посреди раунда — его последний хэш промежуточный
            mine.pop(max(mine))
            print("  (client did not finish: last round skipped)")
        for rnd, h in mine.items():
            if rnd in host and host[rnd] != h:
                ok = False
                print(f"  !! round {rnd}: hash {h} != host {host[rnd]}")
        common = [r for r in mine if r in host]
        print(f"  hash rounds checked: {common} -> {'OK' if all(mine[r] == host[r] for r in common) else 'MISMATCH'}")

    print("\nRESULT:", "PASS" if ok else "FAIL")


if __name__ == "__main__":
    main()
