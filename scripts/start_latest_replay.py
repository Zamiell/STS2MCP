from __future__ import annotations

import argparse
import json
import os
import re
import subprocess
import sys
import time
import urllib.error
import urllib.request
from datetime import datetime
from pathlib import Path
from typing import Any

DEFAULT_PORT = 15526
STEAM_APP_URL = "steam://rungameid/2868840"
REPLAY_NAME_RE = re.compile(r"_(\d{8}_\d{6})\.replay$", re.IGNORECASE)


def get_default_replay_dir() -> Path:
    appdata = os.environ.get("APPDATA")
    if not appdata:
        raise SystemExit("APPDATA is not set; pass --replay-dir explicitly.")

    return Path(appdata) / "SlayTheSpire2" / "STS2MCP" / "replays"


def replay_sort_key(path: Path) -> tuple[datetime, float]:
    match = REPLAY_NAME_RE.search(path.name)
    if match:
        try:
            return (
                datetime.strptime(match.group(1), "%Y%m%d_%H%M%S"),
                path.stat().st_mtime,
            )
        except ValueError:
            pass

    return (datetime.fromtimestamp(path.stat().st_mtime), path.stat().st_mtime)


def find_latest_replay(replay_dir: Path) -> Path:
    if not replay_dir.exists():
        raise SystemExit(f"Replay directory does not exist: {replay_dir}")

    replays = [
        path
        for path in replay_dir.glob("*.replay")
        if path.is_file() and path.stat().st_size > 0
    ]
    if not replays:
        raise SystemExit(f"No non-empty .replay files found in: {replay_dir}")

    return max(replays, key=replay_sort_key)


def get_url(url: str, timeout: float) -> dict[str, Any]:
    with urllib.request.urlopen(url, timeout=timeout) as response:
        text = response.read().decode("utf-8")

    try:
        parsed = json.loads(text)
    except json.JSONDecodeError:
        return {}

    return parsed if isinstance(parsed, dict) else {}


def is_local_host(host: str) -> bool:
    return host.lower() in {"localhost", "127.0.0.1", "::1"}


def is_sts2mcp_available(base_url: str, timeout: float) -> bool:
    try:
        result = get_url(base_url, timeout)
    except urllib.error.URLError:
        return False

    message = str(result.get("message", ""))
    status = str(result.get("status", ""))
    return status == "ok" or "STS2 MCP" in message


def launch_game_through_steam() -> None:
    if hasattr(os, "startfile"):
        os.startfile(STEAM_APP_URL)  # type: ignore[attr-defined]
        return

    powershell = "Start-Process " f"'{STEAM_APP_URL}' " "-WindowStyle Hidden"
    subprocess.run(
        ["powershell.exe", "-NoProfile", "-Command", powershell],
        check=True,
    )


def wait_for_sts2mcp(
    base_url: str,
    request_timeout_seconds: float,
    startup_timeout_seconds: float,
) -> None:
    deadline = time.monotonic() + startup_timeout_seconds
    while time.monotonic() < deadline:
        if is_sts2mcp_available(base_url, request_timeout_seconds):
            return
        time.sleep(1)

    raise SystemExit(
        "Timed out waiting for STS2MCP to become available at "
        f"{base_url} after {startup_timeout_seconds:g} seconds.",
    )


def post_json(url: str, body: dict[str, Any], timeout: float) -> dict[str, Any]:
    data = json.dumps(body).encode("utf-8")
    request = urllib.request.Request(
        url,
        data=data,
        headers={"Content-Type": "application/json"},
        method="POST",
    )

    try:
        with urllib.request.urlopen(request, timeout=timeout) as response:
            text = response.read().decode("utf-8")
    except urllib.error.URLError as ex:
        raise SystemExit(f"Failed to contact STS2MCP at {url}: {ex}") from ex

    try:
        parsed = json.loads(text)
    except json.JSONDecodeError:
        raise SystemExit(f"STS2MCP returned non-JSON response:\n{text}") from None

    if not isinstance(parsed, dict):
        raise SystemExit(f"STS2MCP returned unexpected JSON response:\n{text}")

    return parsed


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Start the newest STS2MCP .replay file by timestamp."
    )
    parser.add_argument(
        "--replay-dir",
        type=Path,
        default=get_default_replay_dir(),
        help="Directory containing .replay files. Defaults to %%APPDATA%%/SlayTheSpire2/STS2MCP/replays.",
    )
    parser.add_argument("--port", type=int, default=DEFAULT_PORT)
    parser.add_argument(
        "--host",
        default="localhost",
        help="STS2MCP host. Defaults to localhost.",
    )
    parser.add_argument(
        "--force",
        action=argparse.BooleanOptionalAction,
        default=True,
        help="Replace any currently running replay. Defaults to true.",
    )
    parser.add_argument(
        "--return-to-main-menu",
        action=argparse.BooleanOptionalAction,
        default=True,
        help="Ask STS2MCP to return to the main menu before playback. Defaults to true.",
    )
    parser.add_argument(
        "--command-timeout-seconds",
        type=float,
        default=30,
        help="Per-command replay timeout passed to STS2MCP. Defaults to 30.",
    )
    parser.add_argument(
        "--request-timeout-seconds",
        type=float,
        default=10,
        help="HTTP request timeout. Defaults to 10.",
    )
    parser.add_argument(
        "--launch-if-needed",
        action=argparse.BooleanOptionalAction,
        default=True,
        help="Launch Slay the Spire 2 through Steam if STS2MCP is unavailable. Defaults to true.",
    )
    parser.add_argument(
        "--startup-timeout-seconds",
        type=float,
        default=120,
        help="How long to wait for STS2MCP after launching the game. Defaults to 120.",
    )
    args = parser.parse_args()

    replay = find_latest_replay(args.replay_dir)
    base_url = f"http://{args.host}:{args.port}/"
    url = f"http://{args.host}:{args.port}/api/v1/singleplayer"
    body = {
        "action": "start_replay",
        "path": str(replay),
        "force": args.force,
        "return_to_main_menu": args.return_to_main_menu,
        "command_timeout_seconds": args.command_timeout_seconds,
    }

    if not is_sts2mcp_available(base_url, args.request_timeout_seconds):
        if not args.launch_if_needed:
            raise SystemExit(f"STS2MCP is not available at {base_url}")
        if not is_local_host(args.host):
            raise SystemExit(
                "--launch-if-needed can only launch a local game; "
                f"host is {args.host!r}.",
            )

        print("STS2MCP is not available; launching Slay the Spire 2 through Steam.")
        launch_game_through_steam()
        wait_for_sts2mcp(
            base_url,
            args.request_timeout_seconds,
            args.startup_timeout_seconds,
        )

    result = post_json(url, body, args.request_timeout_seconds)
    print(json.dumps(result, indent=2))

    if result.get("status") == "error" or "error" in result:
        return 1

    return 0


if __name__ == "__main__":
    sys.exit(main())
