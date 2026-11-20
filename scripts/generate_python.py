#!/usr/bin/env python3
"""Generate python/finclient/_generated from the OpenAPI snapshots in spec/.

Writes, for each service, a module of TypedDict and Literal types, and for FinGraph, FinNews and FinSocial a module of
resource classes (one method per operation). It uses only the standard library and its output is deterministic, so CI
regenerates and diffs it.

    python scripts/generate_python.py
"""
from __future__ import annotations

import json
import keyword
import re
import sys
import textwrap
from pathlib import Path
from typing import Any

ROOT = Path(__file__).resolve().parent.parent
SPEC_DIR = ROOT / "spec"
OUT_DIR = ROOT / "python" / "finclient" / "_generated"

# (service, spec file, types module, ops module or None)
SERVICES = [
    ("FinCopy", "fincopy-v1.json", "copy_types", None),
    ("FinGraph", "fingraph-v1.json", "graph_types", "graph_ops"),
    ("FinNews", "finnews-v1.json", "news_types", "news_ops"),
    ("FinSocial", "finsocial-v1.json", "social_types", "social_ops"),
]

# Descriptions that mention internal planning marks are left out of the generated files.
INTERNAL = re.compile(r"\b(TP?\d{2,4}|Q\d{2,4}-[A-D])\b")
HTTP_METHODS = ("get", "post", "put", "patch", "delete")
WIDTH = 100


def snake(name: str) -> str:
    text = re.sub(r"([A-Z]+)([A-Z][a-z])", r"\1_\2", name)
    text = re.sub(r"([a-z0-9])([A-Z])", r"\1_\2", text)
    text = re.sub(r"\W+", "_", text).strip("_").lower()
    return text + "_" if keyword.iskeyword(text) else text


def pascal(name: str) -> str:
    return "".join(part[:1].upper() + part[1:] for part in re.split(r"\W+", name) if part)


def clean(text: str | None) -> str:
    """A description with CRLF mapped to LF, or an empty string when it carries an internal mark."""
    if not text:
        return ""
    text = text.replace("\r\n", "\n").strip()
    return "" if INTERNAL.search(text) else re.sub(r"[ \t]+\n", "\n", re.sub(r"\n\s+", "\n", text))


def ref_name(ref: str) -> str:
    return ref.rsplit("/", 1)[-1]


class Types:
    """Turns schema fragments into Python type expressions."""

    def __init__(self, qualify: str | None):
        # In the types module a reference is a quoted forward reference; elsewhere it is `<qualify>.Name`.
        self.qualify = qualify

    def ref(self, name: str) -> str:
        return f'"{name}"' if self.qualify is None else f"{self.qualify}.{name}"

    def expr(self, schema: dict[str, Any], used: set[str]) -> str:
        used.update(("Optional", "List", "Dict", "Any", "Literal", "Union"))
        inner = self._expr(schema, used)
        if schema.get("nullable") and inner != "Any":
            return f"Optional[{inner}]"
        return inner

    def _expr(self, schema: dict[str, Any], used: set[str]) -> str:
        if "$ref" in schema:
            name = ref_name(schema["$ref"])
            used.add(name)
            return self.ref(name)
        if "allOf" in schema and len(schema["allOf"]) == 1:
            return self._expr(schema["allOf"][0], used)
        if "enum" in schema:
            values = ", ".join(json.dumps(v) for v in schema["enum"])
            return f"Literal[{values}]"
        kind = schema.get("type")
        if kind == "string":
            return "str"
        if kind == "integer":
            return "int"
        if kind == "number":
            return "float"
        if kind == "boolean":
            return "bool"
        if kind == "array":
            return f"List[{self.expr(schema.get('items', {}), used)}]"
        if kind == "object" or "additionalProperties" in schema or "properties" in schema:
            extra = schema.get("additionalProperties")
            if isinstance(extra, dict) and extra:
                return f"Dict[str, {self.expr(extra, used)}]"
            return "Dict[str, Any]"
        return "Any"


def doc_lines(text: str, indent: str) -> list[str]:
    lines: list[str] = []
    for paragraph in text.split("\n"):
        if not paragraph.strip():
            lines.append("")
            continue
        # An indented line (an argument entry) continues one step deeper than it starts.
        lead = paragraph[: len(paragraph) - len(paragraph.lstrip())]
        wrapped = textwrap.wrap(
            paragraph.strip(), WIDTH,
            initial_indent=indent + lead, subsequent_indent=indent + lead + ("    " if lead else ""),
            break_long_words=False, break_on_hyphens=False)
        lines.extend(wrapped or [""])
    return [line.rstrip() for line in lines]


def triple(text: str) -> str:
    return text.replace("\\", "\\\\").replace('"""', '\\"\\"\\"')


def docstring(text: str, indent: str) -> list[str]:
    body = doc_lines(triple(text), indent)
    if len(body) == 1:
        return [f'{indent}"""{body[0].strip()}"""']
    body[0] = f'{indent}"""{body[0].strip()}'
    return body + [f'{indent}"""']


# ---------------------------------------------------------------------------------------------------------------------
# Types module


def emit_types(service: str, spec: dict[str, Any], source: str) -> str:
    schemas: dict[str, Any] = spec["components"]["schemas"]
    types = Types(None)
    out = [
        f'"""{service} types, generated by scripts/generate_python.py from spec/{source}. Do not edit by hand."""',
        "from __future__ import annotations",
        "",
        "from typing import Any, Dict, List, Literal, Optional, TypedDict, Union  # noqa: F401",
        "",
    ]
    for name in sorted(schemas):
        schema = schemas[name]
        description = clean(schema.get("description"))
        out.append("")
        if "enum" in schema and schema.get("type") in (None, "string", "integer"):
            used: set[str] = set()
            out.append(f"{name} = {types.expr(schema, used)}")
            if description:
                out.extend(docstring(description, ""))
        elif "properties" in schema:
            out.extend(emit_typed_dict(name, schema, types, description))
        else:
            used = set()
            out.append(f"{name} = {types.expr(schema, used)}")
            if description:
                out.extend(docstring(description, ""))
    out.append("")
    return "\n".join(out)


def emit_typed_dict(name: str, schema: dict[str, Any], types: Types, description: str) -> list[str]:
    props: dict[str, Any] = schema["properties"]
    required = [p for p in props if p in schema.get("required", [])]
    fields: list[tuple[str, str, str]] = []
    used: set[str] = set()
    for prop, sub in props.items():
        fields.append((prop, types.expr(sub, used), clean(sub.get("description"))))
    # A field named like a type it mentions would hide that type inside the class body.
    functional = any(prop in used or not prop.isidentifier() or keyword.iskeyword(prop) for prop, _, _ in fields)
    if functional:
        if required:
            raise SystemExit(f"{name}: required fields in a TypedDict that needs the functional form are not supported.")
        body = ", ".join(f"{json.dumps(p)}: {t}" for p, t, _ in fields)
        lines = [f'{name} = TypedDict("{name}", {{{body}}}, total=False)'] if len(body) < 80 else (
            [f'{name} = TypedDict(', f'    "{name}",', "    {"]
            + [f"        {json.dumps(p)}: {t}," for p, t, _ in fields]
            + ["    },", "    total=False,", ")"]
        )
        if description:
            lines.extend(docstring(description, ""))
        return lines
    lines: list[str] = []
    base = "TypedDict"
    if required:
        lines.append(f"class _{name}Required(TypedDict):")
        for prop, annotation, doc in fields:
            if prop in required:
                lines.append(f"    {prop}: {annotation}")
                if doc:
                    lines.extend(docstring(doc, "    "))
        lines += ["", ""]
        base = f"_{name}Required"
    lines.append(f"class {name}({base}, total=False):")
    if description:
        lines.extend(docstring(description, "    "))
    optional = [f for f in fields if f[0] not in required]
    if not optional and not description:
        lines.append("    pass")
    for prop, annotation, doc in optional:
        lines.append(f"    {prop}: {annotation}")
        if doc:
            lines.extend(docstring(doc, "    "))
    return lines


# ---------------------------------------------------------------------------------------------------------------------
# Operations module


class Param:
    def __init__(self, raw: dict[str, Any]):
        self.wire: str = raw["name"]
        self.where: str = raw["in"]
        self.required: bool = bool(raw.get("required"))
        self.py = snake(self.wire)
        self.description = clean(raw.get("description"))
        self.schema: dict[str, Any] = raw.get("schema", {})
        content = raw.get("content", {}).get("application/json")
        self.is_qry = self.where == "query" and content is not None
        self.content_schema: dict[str, Any] = content.get("schema", {}) if content else {}


def resolve(spec: dict[str, Any], schema: dict[str, Any]) -> dict[str, Any]:
    while True:
        if "$ref" in schema:
            schema = spec["components"]["schemas"][ref_name(schema["$ref"])]
        elif "allOf" in schema and len(schema["allOf"]) == 1:
            schema = schema["allOf"][0]
        else:
            return schema


def param_annotation(param: Param, types: Types) -> str:
    schema = param.schema
    used: set[str] = set()
    if schema.get("format") == "date-time" and "$ref" not in schema:
        return "Union[str, datetime]"
    return types.expr(schema, used)


def billing(op: dict[str, Any]) -> str:
    auth = op.get("x-fin-auth")
    tiers = op.get("x-fin-tiers") or []
    cost = op.get("x-fin-cost") or 0
    if auth == "none":
        return "Needs no API key."
    if tiers:
        return "Included in paid plans (package tiers " + ", ".join(str(t) for t in tiers) + ")."
    if cost:
        plural = "" if cost == 1 else "s"
        return f"Billed {cost:g} credit{plural} per call."
    return "No credit charge."


def response_shape(spec: dict[str, Any], op: dict[str, Any], types: Types) -> tuple[str, str, str | None]:
    """Returns (kind, annotation, item annotation); kind is "page", "list" or "item"."""
    ok = op["responses"].get("200", {}).get("content", {}).get("application/json", {}).get("schema")
    if ok is None:
        return "item", "Any", None
    envelope = resolve(spec, ok)
    props = envelope.get("properties", {})
    data = props.get("data")
    if ok.get("$ref", "").endswith("ErrorBody"):
        # A reserved operation: it answers with an error (501) and has no success shape.
        return "item", "Any", None
    if data is None:
        return "item", types.expr(ok, set()), None
    used: set[str] = set()
    data_type = types.expr({k: v for k, v in data.items() if k != "nullable"}, used)
    resolved = resolve(spec, data)
    if resolved.get("type") == "array":
        item = types.expr(resolved.get("items", {}), used)
        if "paging" in props:
            return "page", f"rt.Page[{item}]", item
        return "list", f"List[{item}]", item
    return "item", data_type, None


def emit_ops(service: str, spec: dict[str, Any], source: str, types_module: str) -> str:
    types = Types("t")
    qry_schemas: dict[str, list[str]] = {}
    groups: dict[tuple[str, str], list[tuple[str, str, dict[str, Any]]]] = {}
    for path in sorted(spec["paths"]):
        surface = "api" if path.startswith("/api/") else "v1" if path.startswith("/v1/") else None
        if surface is None:
            raise SystemExit(f"{path}: not under /api or /v1.")
        for method in HTTP_METHODS:
            op = spec["paths"][path].get(method)
            if op is None:
                continue
            tag = (op.get("tags") or [op["operationId"].split("_")[1].removesuffix("Controller")])[0]
            groups.setdefault((surface, tag), []).append((path, method, op))

    class_lines: list[str] = []
    members: dict[str, list[tuple[str, str]]] = {"api": [], "v1": []}
    for (surface, tag), ops in sorted(groups.items()):
        cls = pascal(tag) + ("Api" if surface == "api" else "V1")
        members[surface].append((snake(tag), cls))
        class_lines += ["", "", f"class {cls}(rt.Group):"]
        where = "/api" if surface == "api" else "/v1"
        class_lines.extend(docstring(f"{tag} operations under ``{where}``.", "    "))
        seen: set[str] = set()
        for path, method, op in ops:
            action = snake(op["operationId"].split("_", 2)[2])
            if action in seen:
                raise SystemExit(f"{service} {tag} {surface}: two operations are named {action}.")
            seen.add(action)
            class_lines += emit_operation(spec, types, path, method, op, action, qry_schemas)

    out = [
        f'"""{service} operations, generated by scripts/generate_python.py from spec/{source}. Do not edit by hand."""',
        "from __future__ import annotations",
        "",
        "from datetime import datetime  # noqa: F401",
        "from typing import Any, Dict, Iterator, List, Optional, Tuple, Union  # noqa: F401",
        "",
        "from .. import _runtime as rt",
        f"from . import {types_module} as t",
        "",
        "# The fields each operation's `qry` object knows, by schema; snake_case spellings are accepted for them.",
        "_QRY: Dict[str, Tuple[str, ...]] = {",
    ]
    for name in sorted(qry_schemas):
        out.append(f"    {json.dumps(name)}: (")
        out.extend(f"        {json.dumps(field)}," for field in qry_schemas[name])
        out.append("    ),")
    out.append("}")
    out.extend(class_lines)
    short = service.removeprefix("Fin")
    for surface, label in (("api", "Api"), ("v1", "V1")):
        out += ["", "", f"class {short}{label}Groups:"]
        where = "``/api``" if surface == "api" else "``/v1``"
        out.extend(docstring(f"Every {service} operation under {where}, by group.", "    "))
        out += ["", "    def __init__(self, service: rt.Service) -> None:"]
        if not members[surface]:
            out.append("        pass")
        for attr, cls in members[surface]:
            out.append(f"        self.{attr} = {cls}(service)")
    out += ["", "", f"class {service}({short}ApiGroups):"]
    out.extend(docstring(
        f"The {service} part of the client. ``/api`` operations are groups on this object (credits per call, any plan); "
        "``/v1`` operations are under ``.v1`` (included in paid plans, gated by tier).", "    "))
    out += [
        "",
        "    def __init__(self, service: rt.Service) -> None:",
        "        super().__init__(service)",
        f"        self.v1 = {short}V1Groups(service)",
        "",
    ]
    return "\n".join(out)


def emit_operation(
    spec: dict[str, Any], types: Types, path: str, method: str, op: dict[str, Any], action: str,
    qry_schemas: dict[str, list[str]],
) -> list[str]:
    params = [Param(p) for p in op.get("parameters", [])]
    path_params = [p for p in params if p.where == "path"]
    query_params = [p for p in params if p.where == "query" and not p.is_qry]
    qry = next((p for p in params if p.is_qry), None)
    body_schema = op.get("requestBody", {}).get("content", {}).get("application/json", {}).get("schema")
    kind, returns, item = response_shape(spec, op, types)
    auth = op.get("x-fin-auth", "key")

    args = ["self"]
    for p in path_params:
        args.append(f"{p.py}: str")
    qry_name = None
    if qry is not None:
        qry_name = ref_name(qry.content_schema["$ref"])
        qry_schemas[qry_name] = list(resolve(spec, qry.content_schema).get("properties", {}))
        args.append(f"qry: Optional[t.{qry_name}] = None")
    body_type = None
    body_optional = False
    if body_schema is not None:
        body_type = types.expr(body_schema, set())
        resolved = resolve(spec, body_schema)
        body_optional = not resolved.get("required")
        args.append(f"body: Optional[{body_type}] = None" if body_optional else f"body: {body_type}")
    keyword_params = sorted(query_params, key=lambda p: not p.required)
    if keyword_params:
        args.append("*")
    for p in keyword_params:
        annotation = param_annotation(p, types)
        args.append(f"{p.py}: {annotation}" if p.required else f"{p.py}: Optional[{annotation}] = None")
    signature_tail = ", **fields: Any" if qry is not None else ""

    def signature(name: str, ret: str) -> list[str]:
        head = f"    def {name}({', '.join(args)}{signature_tail}) -> {ret}:"
        if len(head) <= WIDTH:
            return [head]
        lines = [f"    def {name}("]
        lines += [f"        {a}," for a in args]
        if qry is not None:
            lines.append("        **fields: Any,")
        lines.append(f"    ) -> {ret}:")
        return lines

    # Docstring
    summary = clean(op.get("summary")) or action.replace("_", " ").capitalize()
    text = [summary]
    description = clean(op.get("description"))
    if description:
        text += ["", description]
    text += ["", f"``{method.upper()} {path}``. {billing(op)}"]
    documented = [p for p in path_params + keyword_params if p.description]
    if documented or qry is not None or body_schema is not None:
        text += ["", "Args:"]
        for p in path_params + keyword_params:
            text.append(f"    {p.py}: {p.description or p.wire}")
        if qry is not None:
            text.append(f"    qry: {qry.description or 'The query object.'} Pass a dict, keyword arguments, or both.")
            for field, sub in resolve(spec, qry.content_schema).get("properties", {}).items():
                detail = clean(sub.get("description"))
                text.append(f"        {field}: {detail}" if detail else f"        {field}")
        if body_schema is not None:
            text.append(f"    body: {clean(op['requestBody'].get('description')) or 'The request body.'}")
    text += ["", f"Returns: {'a page of rows with its paging info' if kind == 'page' else 'the rows' if kind == 'list' else 'the result'}."]

    call_path = re.sub(r"\{(\w+)\}", lambda m: "{rt.seg(" + snake(m.group(1)) + ")}", path)
    path_expr = f'f"{call_path}"' if "{" in path else f'"{path}"'
    query_items = ", ".join(f'("{p.wire}", {p.py})' for p in keyword_params)
    if keyword_params:
        query_expr = f"[{query_items}]"
    else:
        query_expr = "None"
    if qry is not None:
        qry_expr = f'rt.build_qry(_QRY["{qry_name}"], qry, fields)'
    else:
        qry_expr = "None"
    body_expr = "body" if body_schema is not None else "None"
    if body_schema is not None and body_optional:
        body_expr = "body if body is not None else {}"
    helper = {"page": "page", "list": "data", "item": "data"}[kind]
    out = [""] + signature(action, returns)
    out.extend(docstring("\n".join(text), "        "))
    call = [
        f"        return self._service.{helper}(",
        f'            "{method.upper()}", {path_expr}, auth="{auth}",',
    ]
    extras = []
    if keyword_params:
        extras.append(f"query={query_expr}")
    if qry is not None:
        extras.append(f"qry={qry_expr}")
    if body_schema is not None:
        extras.append(f"body={body_expr}")
    if extras:
        call.append("            " + ", ".join(extras) + ",")
    call.append("        )")
    out.extend(call)

    # Iterators over pages and items
    names = {p.wire.lower() for p in keyword_params}
    if kind == "page" and qry is not None:
        out += [""] + signature(f"{action}_pages", f"Iterator[{returns}]")
        out.extend(docstring(f"Yield every page of :meth:`{action}`.\n\nAdvances ``pageNo`` until the last page or an empty page.", "        "))
        positional = ", ".join(p.py for p in path_params)
        prefix = f"{positional}, " if positional else ""
        out.append(f"        return rt.iter_qry_pages(")
        out.append(f'            self.{action}, {prefix}qry=rt.build_qry(_QRY["{qry_name}"], qry, fields))')
        out += [""] + signature(f"{action}_items", f"Iterator[{item}]")
        out.extend(docstring(f"Yield every row of :meth:`{action}`, page after page.", "        "))
        out.append(f"        return rt.iter_items(self.{action}_pages({prefix}qry, **fields))")
    elif kind == "list" and {"limit", "offset"} <= names:
        all_args = [f"{p.py}={p.py}" for p in keyword_params if p.wire not in ("limit", "offset")]
        positional = [p.py for p in path_params]
        out += [""] + signature(f"{action}_pages", f"Iterator[{returns}]")
        out.extend(docstring(
            f"Yield every page of :meth:`{action}`.\n\nAdvances ``offset`` by the rows returned until a short or empty page.",
            "        "))
        call_args = ", ".join(positional + all_args + ["limit=lim", "offset=off"])
        out.append("        return rt.iter_offset_pages(")
        out.append(f"            lambda lim, off: self.{action}({call_args}), limit=limit, offset=offset)")
        out += [""] + signature(f"{action}_items", f"Iterator[{item}]")
        out.extend(docstring(f"Yield every row of :meth:`{action}`, page after page.", "        "))
        out.append(f"        return rt.iter_items(self.{action}_pages(")
        out.append(f"            {', '.join(positional + [p.py + '=' + p.py for p in keyword_params])}))")
    return out


def main() -> int:
    OUT_DIR.mkdir(parents=True, exist_ok=True)
    files: dict[str, str] = {
        "__init__.py": '"""Types and operations generated from the OpenAPI snapshots in spec/. Do not edit by hand."""\n',
    }
    for service, source, types_module, ops_module in SERVICES:
        spec = json.loads((SPEC_DIR / source).read_text(encoding="utf-8"))
        files[f"{types_module}.py"] = emit_types(service, spec, source)
        if ops_module:
            files[f"{ops_module}.py"] = emit_ops(service, spec, source, types_module)
    for name, text in files.items():
        (OUT_DIR / name).write_text(text, encoding="utf-8", newline="\n")
    print(f"Wrote {len(files)} files to {OUT_DIR.relative_to(ROOT)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
