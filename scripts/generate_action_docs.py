from __future__ import annotations

import argparse
import re
import subprocess
import sys
import tempfile
from dataclasses import dataclass, field
from pathlib import Path

SOURCE_FILES = (
    "McpMod.Actions.cs",
    "McpMod.MultiplayerActions.cs",
    "McpMod.ReplayPlayback.cs",
)
DOCUMENTATION_FILES = ("McpApiDocumentation.cs",)


@dataclass
class ActionDoc:
    action: str
    category: str
    description: str
    fields: list[tuple[str, str, bool, str]] = field(default_factory=list)
    notes: list[str] = field(default_factory=list)


@dataclass
class EndpointDoc:
    method: str
    path: str
    description: str


@dataclass
class QueryParameterDoc:
    endpoint: str
    name: str
    values: str
    default: str
    description: str


@dataclass
class StateTypeDoc:
    state_type: str
    screen: str
    actions: str
    description: str


@dataclass
class SectionDoc:
    order: int
    title: str
    body: str


def parse_string_args(text: str) -> list[str]:
    return re.findall(r'"((?:[^"\\]|\\.)*)"', text)


def parse_bool_arg(text: str) -> bool | None:
    match = re.search(r"\b(true|false)\b", text, flags=re.IGNORECASE)
    if match is None:
        return None
    return match.group(1).lower() == "true"


def parse_int_arg(text: str) -> int | None:
    match = re.search(r"\(\s*(\d+)\s*,", text)
    if match is None:
        return None
    return int(match.group(1))


def parse_actions(root: Path) -> list[ActionDoc]:
    docs: list[ActionDoc] = []
    for source_file in SOURCE_FILES:
        lines = (root / source_file).read_text(encoding="utf-8-sig").splitlines()
        pending_action: ActionDoc | None = None
        pending_fields: list[tuple[str, str, bool, str]] = []
        pending_notes: list[str] = []

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
                pending_notes = []
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

            if stripped.startswith("[McpActionNote("):
                args = parse_string_args(stripped)
                if len(args) != 1:
                    raise SystemExit(
                        f"Malformed McpActionNote in {source_file}: {stripped}",
                    )
                pending_notes.append(args[0])
                continue

            if pending_action is not None and re.search(r"\bExecute\w+\s*\(", stripped):
                pending_action.fields = pending_fields
                pending_action.notes = pending_notes
                docs.append(pending_action)
                pending_action = None
                pending_fields = []
                pending_notes = []

    return sorted(docs, key=lambda doc: (doc.category, doc.action))


def parse_api_metadata(
    root: Path,
) -> tuple[
    list[EndpointDoc], list[QueryParameterDoc], list[StateTypeDoc], list[SectionDoc]
]:
    endpoints: list[EndpointDoc] = []
    query_parameters: list[QueryParameterDoc] = []
    state_types: list[StateTypeDoc] = []
    sections: list[SectionDoc] = []

    for source_file in DOCUMENTATION_FILES:
        lines = (root / source_file).read_text(encoding="utf-8-sig").splitlines()
        for line in lines:
            stripped = line.strip()
            if stripped.startswith("[assembly: McpEndpoint("):
                args = parse_string_args(stripped)
                if len(args) != 3:
                    raise SystemExit(
                        f"Malformed McpEndpoint in {source_file}: {stripped}"
                    )
                endpoints.append(EndpointDoc(*args))
                continue

            if stripped.startswith("[assembly: McpQueryParameter("):
                args = parse_string_args(stripped)
                if len(args) != 5:
                    raise SystemExit(
                        f"Malformed McpQueryParameter in {source_file}: {stripped}",
                    )
                query_parameters.append(QueryParameterDoc(*args))
                continue

            if stripped.startswith("[assembly: McpStateType("):
                args = parse_string_args(stripped)
                if len(args) != 4:
                    raise SystemExit(
                        f"Malformed McpStateType in {source_file}: {stripped}"
                    )
                state_types.append(StateTypeDoc(*args))
                continue

            if stripped.startswith("[assembly: McpDocSection("):
                args = parse_string_args(stripped)
                order = parse_int_arg(stripped)
                if len(args) != 2 or order is None:
                    raise SystemExit(
                        f"Malformed McpDocSection in {source_file}: {stripped}"
                    )
                sections.append(SectionDoc(order, args[0], args[1]))

    return (
        endpoints,
        query_parameters,
        state_types,
        sorted(sections, key=lambda section: section.order),
    )


def parse_switch_actions(root: Path) -> set[str]:
    actions: set[str] = {"menu_select"}
    for source_file in SOURCE_FILES:
        source = (root / source_file).read_text(encoding="utf-8-sig")
        actions.update(re.findall(r'"([a-z][a-z0-9_]+)"\s*=>\s*Execute', source))
    return actions


def render_wrapped_text(text: str) -> list[str]:
    return text.split("\\n")


def render_metadata_sections(
    endpoints: list[EndpointDoc],
    query_parameters: list[QueryParameterDoc],
    state_types: list[StateTypeDoc],
    sections: list[SectionDoc],
) -> list[str]:
    lines = [
        "# STS2MCP API Reference",
        "",
        "This file is generated from C# metadata attributes in the source.",
        "Run `python scripts/generate_action_docs.py` after changing action metadata.",
        "",
    ]

    section_by_title = {section.title: section for section in sections}
    overview = section_by_title.get("Overview")
    if overview is not None:
        lines.extend(render_wrapped_text(overview.body))
        lines.append("")

    lines.extend(
        [
            "## Endpoints",
            "",
            "| Method | Path | Description |",
            "| --- | --- | --- |",
        ],
    )
    for endpoint in endpoints:
        lines.append(
            f"| `{endpoint.method}` | `{endpoint.path}` | {endpoint.description} |"
        )
    lines.append("")

    if query_parameters:
        lines.extend(["## Query Parameters", ""])
        for endpoint in sorted({param.endpoint for param in query_parameters}):
            lines.extend([f"### `{endpoint}`", ""])
            lines.extend(
                [
                    "| Parameter | Values | Default | Description |",
                    "| --- | --- | --- | --- |",
                ],
            )
            for param in [
                param for param in query_parameters if param.endpoint == endpoint
            ]:
                lines.append(
                    f"| `{param.name}` | `{param.values}` | `{param.default}` | {param.description} |",
                )
            lines.append("")

    lines.extend(["## Game State", ""])
    for title in ("Common Game State", "Object Shapes"):
        section = section_by_title.get(title)
        if section is not None:
            lines.extend([f"### {title}", ""])
            lines.extend(render_wrapped_text(section.body))
            lines.append("")

    lines.extend(
        [
            "### State Types",
            "",
            "| `state_type` | Screen | Available Actions | Description |",
            "| --- | --- | --- | --- |",
        ],
    )
    for state_type in state_types:
        actions = (
            state_type.actions
            if state_type.actions == "none"
            else f"`{state_type.actions}`"
        )
        lines.append(
            f"| `{state_type.state_type}` | {state_type.screen} | {actions} | {state_type.description} |",
        )
    lines.append("")

    for section in sections:
        if section.title in {"Overview", "Common Game State", "Object Shapes"}:
            continue
        if section.order >= 1000:
            continue
        lines.extend([f"## {section.title}", ""])
        lines.extend(render_wrapped_text(section.body))
        lines.append("")

    return lines


def render_actions(docs: list[ActionDoc]) -> list[str]:
    lines = ["## Actions", ""]
    current_category: str | None = None
    for doc in docs:
        if doc.category != current_category:
            current_category = doc.category
            lines.extend([f"### {current_category}", ""])

        lines.extend([f"#### `{doc.action}`", "", doc.description, ""])
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

        if doc.notes:
            lines.extend(["Notes:", ""])
            for note in doc.notes:
                lines.append(f"- {note}")
            lines.append("")

    return lines


def render_markdown(
    docs: list[ActionDoc],
    endpoints: list[EndpointDoc],
    query_parameters: list[QueryParameterDoc],
    state_types: list[StateTypeDoc],
    sections: list[SectionDoc],
) -> str:
    lines = render_metadata_sections(endpoints, query_parameters, state_types, sections)
    lines.extend(render_actions(docs))
    return "\n".join(lines).rstrip() + "\n"


def format_markdown(text: str) -> str:
    with tempfile.NamedTemporaryFile(
        "w+",
        prefix=".generated_api_reference_",
        suffix=".md",
        delete=False,
        encoding="utf-8",
        dir=Path.cwd(),
    ) as temp:
        temp.write(text)
        temp_path = Path(temp.name)

    try:
        temp_arg = temp_path.name
        commands = [
            f'bunx --bun prettier --write "{temp_arg}"',
            f'/mnt/c/Users/james/.bun/bin/bunx.exe --bun prettier --write "{temp_arg}"',
            f'C:/Users/james/.bun/bin/bunx.exe --bun prettier --write "{temp_arg}"',
        ]

        last_error = ""
        for command in commands:
            result = subprocess.run(
                command,
                shell=True,
                stdout=subprocess.PIPE,
                stderr=subprocess.PIPE,
                text=True,
            )
            if result.returncode == 0:
                break
            last_error = result.stderr.strip()
        else:
            raise SystemExit(
                f"Failed to format generated Markdown with Prettier: {last_error}"
            )

        return temp_path.read_text(encoding="utf-8")
    finally:
        temp_path.unlink(missing_ok=True)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()

    root = Path(__file__).resolve().parents[1]
    output_path = root / "docs" / "actions.md"
    docs = parse_actions(root)
    endpoints, query_parameters, state_types, sections = parse_api_metadata(root)
    documented = {doc.action for doc in docs}
    missing = sorted(parse_switch_actions(root) - documented)
    if missing:
        raise SystemExit(
            "Missing McpAction metadata for: " + ", ".join(missing),
        )

    text = format_markdown(
        render_markdown(docs, endpoints, query_parameters, state_types, sections),
    )

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
