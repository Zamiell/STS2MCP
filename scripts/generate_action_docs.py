from __future__ import annotations

import argparse
import re
import sys
from dataclasses import dataclass, field
from pathlib import Path

SOURCE_FILES = (
    "McpMod.Actions.cs",
    "McpMod.MultiplayerActions.cs",
)


@dataclass
class ActionDoc:
    action: str
    category: str
    description: str
    fields: list[tuple[str, str, bool, str]] = field(default_factory=list)


def parse_string_args(text: str) -> list[str]:
    return re.findall(r'"((?:[^"\\]|\\.)*)"', text)


def parse_bool_arg(text: str) -> bool | None:
    match = re.search(r"\b(true|false)\b", text, flags=re.IGNORECASE)
    if match is None:
        return None
    return match.group(1).lower() == "true"


def parse_actions(root: Path) -> list[ActionDoc]:
    docs: list[ActionDoc] = []
    for source_file in SOURCE_FILES:
        lines = (root / source_file).read_text(encoding="utf-8-sig").splitlines()
        pending_action: ActionDoc | None = None
        pending_fields: list[tuple[str, str, bool, str]] = []

        for line in lines:
            stripped = line.strip()
            if stripped.startswith("[McpAction("):
                args = parse_string_args(stripped)
                if len(args) != 3:
                    raise SystemExit(
                        f"Malformed McpAction in {source_file}: {stripped}"
                    )
                pending_action = ActionDoc(args[0], args[1], args[2])
                pending_fields = []
                continue

            if stripped.startswith("[McpActionField("):
                args = parse_string_args(stripped)
                required = parse_bool_arg(stripped)
                if len(args) != 3 or required is None:
                    raise SystemExit(
                        f"Malformed McpActionField in {source_file}: {stripped}",
                    )
                pending_fields.append((args[0], args[1], required, args[2]))
                continue

            if pending_action is not None and re.search(r"\bExecute\w+\s*\(", stripped):
                pending_action.fields = pending_fields
                docs.append(pending_action)
                pending_action = None
                pending_fields = []

    return sorted(docs, key=lambda doc: (doc.category, doc.action))


def parse_switch_actions(root: Path) -> set[str]:
    actions: set[str] = {"menu_select"}
    for source_file in SOURCE_FILES:
        source = (root / source_file).read_text(encoding="utf-8-sig")
        actions.update(re.findall(r'"([a-z][a-z0-9_]+)"\s*=>\s*Execute', source))
    return actions


def render_markdown(docs: list[ActionDoc]) -> str:
    lines = [
        "# STS2MCP Action Reference",
        "",
        "This file is generated from `McpAction` and `McpActionField` attributes in the C# source.",
        "Run `python scripts/generate_action_docs.py` after changing action metadata.",
        "",
    ]

    current_category: str | None = None
    for doc in docs:
        if doc.category != current_category:
            current_category = doc.category
            lines.extend([f"## {current_category}", ""])

        lines.extend([f"### `{doc.action}`", "", doc.description, ""])
        if doc.fields:
            lines.extend(
                [
                    "| Field | Type | Required | Description |",
                    "| --- | --- | --- | --- |",
                ]
            )
            for name, field_type, required, description in doc.fields:
                lines.append(
                    f"| `{name}` | `{field_type}` | {'yes' if required else 'no'} | {description} |",
                )
            lines.append("")
        else:
            lines.extend(["No fields.", ""])

    return "\n".join(lines).rstrip() + "\n"


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()

    root = Path(__file__).resolve().parents[1]
    output_path = root / "docs" / "actions.md"
    docs = parse_actions(root)
    documented = {doc.action for doc in docs}
    missing = sorted(parse_switch_actions(root) - documented)
    if missing:
        raise SystemExit(
            "Missing McpAction metadata for: " + ", ".join(missing),
        )

    text = render_markdown(docs)

    if args.check:
        existing = (
            output_path.read_text(encoding="utf-8") if output_path.exists() else ""
        )
        if existing != text:
            print(
                f"{output_path} is out of date. Run scripts/generate_action_docs.py.",
                file=sys.stderr,
            )
            raise SystemExit(1)
        print(f"{output_path} is up to date.")
        return

    output_path.write_text(text, encoding="utf-8")
    print(f"wrote {output_path}")


if __name__ == "__main__":
    main()
