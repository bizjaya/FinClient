// Generates the FinGraph, FinNews and FinSocial resource classes (TypeScript and .NET) from the OpenAPI snapshots in spec/.
//
//   node scripts/generate-surface.mjs
//
// The output is checked in and CI regenerates it and fails on any difference, so it is deterministic: operations are read in
// path order, then HTTP method order, and nothing in the output depends on time or the machine. The shared plumbing (the
// transport, errors, paging and query helpers) is hand-written; only the per-operation methods are generated.
//
// Naming rules (also in the READMEs):
//   - /api/... operations are the credit-charged surface; /v1/... operations are the plan-included surface. They sit apart:
//     fin.graph.<group>.<op>() and fin.graph.v1.<group>.<op>() (TypeScript), fin.Graph.<Group>.<Op>Async() and
//     fin.Graph.V1.<Group>.<Op>Async() (.NET).
//   - <group> is the operation's first tag; <op> is the action part of its operationId (the text after the last "_"),
//     with a lower-case first letter in TypeScript.
//   - If two operations of one surface and group would get the same name, the later one in path order is named with its
//     HTTP method in front (getView), then with its last path segment, so the names stay unique and stable.
import { readFileSync, writeFileSync, mkdirSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const root = join(dirname(fileURLToPath(import.meta.url)), "..");
const METHODS = ["get", "post", "put", "patch", "delete"];

const services = [
  { id: "graph", name: "Graph", label: "FinGraph", spec: "fingraph-v1.json", baseUrl: "https://x.fingraph.io" },
  { id: "news", name: "News", label: "FinNews", spec: "finnews-v1.json", baseUrl: "https://x.finnews.io" },
  { id: "social", name: "Social", label: "FinSocial", spec: "finsocial-v1.json", baseUrl: "https://x.finsocial.io" },
];

const upperFirst = (s) => s.charAt(0).toUpperCase() + s.slice(1);
const lowerFirst = (s) => s.charAt(0).toLowerCase() + s.slice(1);
const csKeywords = new Set(["abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class", "const",
  "continue", "decimal", "default", "delegate", "do", "double", "else", "enum", "event", "explicit", "extern", "false", "finally",
  "fixed", "float", "for", "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock", "long",
  "namespace", "new", "null", "object", "operator", "out", "override", "params", "private", "protected", "public", "readonly",
  "ref", "return", "sbyte", "sealed", "short", "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw",
  "true", "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual", "void", "volatile", "while",
  "from", "to", "select", "where", "group", "into", "orderby", "join", "let", "on", "equals", "by", "ascending", "descending",
  "value", "var", "async", "await", "record", "init"]);
const csIdent = (s) => (csKeywords.has(s) ? `@${s}` : s);

// ---------------------------------------------------------------- reading a spec

function readOperations(svc) {
  const doc = JSON.parse(readFileSync(join(root, "spec", svc.spec), "utf8"));
  const schemas = doc.components.schemas;
  const ops = [];
  for (const path of Object.keys(doc.paths).sort()) {
    for (const method of METHODS) {
      const op = doc.paths[path][method];
      if (!op) continue;
      const surface = path.startsWith("/api/") ? "api" : path.startsWith("/v1/") ? "v1" : null;
      if (!surface) throw new Error(`${svc.spec}: ${path} is neither an /api nor a /v1 route.`);
      if (!op.tags?.length) throw new Error(`${svc.spec}: ${method} ${path} has no tag.`);
      const action = op.operationId.split("_").pop();
      const params = (op.parameters ?? []).map((p) => {
        if (p.$ref) throw new Error(`${svc.spec}: parameter references are not supported (${path}).`);
        return p;
      });
      const bodySchema = op.requestBody?.content?.["application/json"]?.schema;
      const ok = op.responses?.["200"]?.content?.["application/json"]?.schema;
      ops.push({
        path, method: method.toUpperCase(), surface, group: op.tags[0], action,
        name: lowerFirst(action),
        summary: op.summary ?? "", description: op.description ?? "",
        pathParams: params.filter((p) => p.in === "path").map(paramOf),
        queryParams: params.filter((p) => p.in === "query").map(paramOf),
        body: bodySchema ? { ref: refName(bodySchema), required: op.requestBody.required === true } : null,
        // A reply the document does not name (the candle routes return free-form JSON) is left untyped.
        response: refName(ok) ? { ref: refName(ok), schema: schemas[refName(ok)] } : null,
        cost: op["x-fin-cost"] ?? null,
        tiers: (op["x-fin-tiers"]?.length ? op["x-fin-tiers"] : op["x-fin-configured-tiers"]) ?? [],
      });
    }
  }
  // Unique, stable names within a surface and group.
  const taken = new Set();
  for (const op of ops) {
    let name = op.name;
    if (taken.has(`${op.surface}.${op.group}.${name}`)) name = lowerFirst(op.method.toLowerCase() + upperFirst(op.name));
    if (taken.has(`${op.surface}.${op.group}.${name}`)) name += upperFirst(op.path.split("/").pop().replace(/[^A-Za-z0-9]/g, ""));
    if (taken.has(`${op.surface}.${op.group}.${name}`)) throw new Error(`Cannot name ${op.method} ${op.path} uniquely.`);
    op.name = name;
    taken.add(`${op.surface}.${op.group}.${name}`);
  }
  for (const op of ops) op.paging = pagingOf(op);
  return { doc, ops };
}

function refName(schema) {
  const ref = schema?.$ref ?? schema?.allOf?.[0]?.$ref;
  return ref ? ref.split("/").pop() : null;
}

function paramOf(p) {
  const s = p.schema ?? {};
  let kind = "string";
  let ref = null;
  if (refName(s)) { kind = "ref"; ref = refName(s); }
  else if (s.type === "boolean") kind = "bool";
  else if (s.type === "integer") kind = s.format === "int64" ? "long" : "int";
  else if (s.type === "number") kind = "double";
  else if (s.type === "string" && s.format === "date-time") kind = "datetime";
  else if (s.type !== "string") throw new Error(`Unsupported parameter type for ${p.name}: ${JSON.stringify(s)}`);
  return { name: p.name, required: p.required === true, description: p.description ?? "", kind, ref };
}

/** How an operation pages, when it does: a `page` query value and a reply with a `data` array and a paging block. */
function pagingOf(op) {
  const props = op.response?.schema?.properties;
  const items = refName(props?.data?.items);
  if (!props?.data || props.data.type !== "array" || !items) return null;
  if (op.queryParams.some((p) => p.name === "page") && props.paging) return { items };
  return null;
}

// ---------------------------------------------------------------- shared text

// A word joiner: wrap() keeps what is joined by it on one line.
const GLUE = String.fromCharCode(1);
function plain(text) { return text.replace(/\s+/g, " ").trim(); }

function wrap(text, width) {
  const lines = [];
  let line = "";
  for (const word of plain(text).split(" ")) {
    if (line && line.length + word.length + 1 > width) { lines.push(line); line = word; } else line = line ? `${line} ${word}` : word;
  }
  if (line) lines.push(line);
  return lines.map((l) => l.split(GLUE).join(" "));
}

function numberText(value) { return String(value); }

/** The sentences a method's documentation carries, in plain text. */
function docLines(op) {
  const out = [];
  if (op.summary) out.push(plain(op.summary).replace(/\.?$/, "."));
  if (op.description && plain(op.description) !== plain(op.summary)) out.push(plain(op.description));
  out.push(`${op.method}${GLUE}${op.path}.`);
  if (op.surface === "api") {
    out.push(op.cost === null ? "Charged in credits per call." : `Charged ${numberText(op.cost)} credit${op.cost === 1 ? "" : "s"} per call.`);
  } else {
    out.push(op.tiers.length ? `Included in paid plans, for package tiers ${op.tiers.join(", ")}.` : "Included in paid plans.");
  }
  return wrap(out.join(" "), 112);
}

// ---------------------------------------------------------------- TypeScript

function tsType(p) {
  switch (p.kind) {
    case "ref": return `S["${p.ref}"]`;
    case "bool": return "boolean";
    case "int": case "long": case "double": return "number";
    case "datetime": return "string | Date";
    default: return "string";
  }
}

const tsDoc = (lines, indent) => lines.length === 1
  ? `${indent}/** ${lines[0].replace(/\*\//g, "*\\/")} */`
  : [`${indent}/**`, ...lines.map((l) => `${indent} * ${l.replace(/\*\//g, "*\\/")}`), `${indent} */`].join("\n");

function tsQueryType(op, indent) {
  const members = op.queryParams.map((p) => {
    const doc = p.description ? `${tsDoc(wrap(p.description, 100), indent + "  ")}\n` : "";
    return `${doc}${indent}  ${p.name}${p.required ? "" : "?"}: ${tsType(p)};`;
  });
  return `{\n${members.join("\n")}\n${indent}}`;
}

function tsMethod(svc, op) {
  const lines = [];
  const pathArgs = op.pathParams.map((p) => `${p.name}: string`);
  const returns = op.response ? `S["${op.response.ref}"]` : "unknown";
  const queryRequired = op.queryParams.some((p) => p.required);
  const args = [...pathArgs];
  if (op.body) args.push(`body: S["${op.body.ref}"]`);
  let queryArg = null;
  if (op.queryParams.length) queryArg = `query: ${tsQueryType(op, "  ")}${queryRequired ? "" : " = {}"}`;
  if (queryArg) args.push(queryArg);
  args.push("options: FinRequestOptions = {}");

  const template = "`" + op.path.slice(1).replace(/\{(\w+)\}/g, (_m, n) => "${pathSegment(" + n + ")}") + "`";
  const url = op.pathParams.length ? template : `"${op.path.slice(1)}"`;
  const input = [];
  if (op.queryParams.length) input.push("query");
  if (op.body) input.push("body");
  lines.push(tsDoc(docLines(op), "  "));
  // async: an argument that cannot be sent (an empty path argument) rejects the promise like every other failure.
  lines.push(`  async ${op.name}(${args.join(", ")}): Promise<${returns}> {`);
  lines.push(`    return this.http.call("${op.method}", ${url}, { ${input.join(", ")} }, options);`);
  lines.push("  }");

  if (op.paging) {
    const passArgs = [...op.pathParams.map((p) => p.name)].concat(op.body ? ["body"] : []);
    const pageType = `S["${op.response.ref}"]`;
    const itemType = `S["${op.paging.items}"]`;
    const sig = [...pathArgs, `query: ${tsQueryType(op, "  ")} = {}`, "options: FinRequestOptions = {}"].join(", ");
    const call = `(q) => this.${op.name}(${[...passArgs, "q", "options"].join(", ")})`;
    lines.push("");
    lines.push(tsDoc(wrap(`Reads every page of ${op.name}, advancing page from the one in the query (1 when unset). It stops after the last page the paging block names, or at the first empty page, which it does not yield.`, 112), "  "));
    lines.push(`  ${op.name}Pages(${sig}): AsyncGenerator<${pageType}, void, undefined> {`);
    lines.push(`    return pagesByPage(${call}, query);`);
    lines.push("  }");
    lines.push("");
    lines.push(tsDoc([`Reads every row of every page of ${op.name}, one at a time.`], "  "));
    lines.push(`  ${op.name}Items(${sig}): AsyncGenerator<${itemType}, void, undefined> {`);
    lines.push(`    return itemsOf(this.${op.name}Pages(${[...op.pathParams.map((p) => p.name), "query", "options"].join(", ")}));`);
    lines.push("  }");
  }
  return lines.join("\n");
}

function emitTypeScript(svc, ops) {
  const used = { paging: ops.some((op) => op.paging), segment: ops.some((op) => op.pathParams.length) };
  const support = [used.paging && "itemsOf", used.paging && "pagesByPage", used.segment && "pathSegment"].filter(Boolean);
  const out = [];
  out.push(`// <auto-generated>`);
  out.push(`// Generated by scripts/generate-surface.mjs from spec/${svc.spec}. Do not edit by hand: change the generator or the spec.`);
  out.push(`// </auto-generated>`);
  out.push(`import type { components } from "./generated/${svc.id}-schema.js";`);
  out.push(`import type { FinHttpTransport } from "./http-transport.js";`);
  out.push(`import type { FinRequestOptions } from "./options.js";`);
  out.push(`import { ${support.join(", ")} } from "./service-support.js";`);
  out.push("");
  out.push(`type S = components["schemas"];`);

  const classes = [];
  for (const surface of ["api", "v1"]) {
    const groups = [...new Set(ops.filter((o) => o.surface === surface).map((o) => o.group))].sort();
    for (const group of groups) {
      const className = `${svc.name}${surface === "v1" ? "V1" : ""}${group}Resource`;
      classes.push({ surface, group, className });
      const where = surface === "api" ? "credit-charged /api" : "plan-included /v1";
      out.push("");
      out.push(tsDoc([`${svc.label} ${group}: the ${where} operations.`], ""));
      out.push(`export class ${className} {`);
      out.push(`  constructor(private readonly http: FinHttpTransport) {}`);
      for (const op of ops.filter((o) => o.surface === surface && o.group === group)) {
        out.push("");
        out.push(tsMethod(svc, op));
      }
      out.push("}");
    }
  }
  for (const surface of ["v1", "api"]) {
    const own = classes.filter((c) => c.surface === surface);
    const name = surface === "v1" ? `Fin${svc.name}V1` : `Fin${svc.name}`;
    out.push("");
    if (surface === "v1") out.push(tsDoc([`${svc.label}'s /v1 operations, included in paid plans and gated by package tier.`], ""));
    else out.push(tsDoc([`${svc.label}. The groups on this object are the /api operations, charged in credits per call; the /v1 operations, included in paid plans, are under \`v1\`.`], ""));
    out.push(`export class ${name} {`);
    for (const c of own) out.push(`  readonly ${lowerFirst(c.group)}: ${c.className};`);
    if (surface === "api") out.push(`  /** The /v1 operations: included in paid plans, gated by package tier. */\n  readonly v1: Fin${svc.name}V1;`);
    out.push("");
    out.push(`  constructor(http: FinHttpTransport) {`);
    for (const c of own) out.push(`    this.${lowerFirst(c.group)} = new ${c.className}(http);`);
    if (surface === "api") out.push(`    this.v1 = new Fin${svc.name}V1(http);`);
    out.push(`  }`);
    out.push("}");
  }
  return out.join("\n") + "\n";
}

// ---------------------------------------------------------------- C#

function csType(p) {
  switch (p.kind) {
    case "ref": return p.ref;
    case "bool": return "bool";
    case "int": return "int";
    case "long": return "long";
    case "double": return "double";
    case "datetime": return "DateTimeOffset";
    default: return "string";
  }
}

const xml = (s) => s.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;");

function csDoc(lines, indent, params = []) {
  const out = [`${indent}/// <summary>`, ...lines.map((l) => `${indent}/// ${xml(l)}`), `${indent}/// </summary>`];
  for (const [name, text] of params) out.push(`${indent}/// <param name="${name}">${xml(text)}</param>`);
  return out.join("\n");
}

const camel = (s) => lowerFirst(s);

function csSignature(op) {
  // Path arguments, the body, the query, then the optional extras: C# wants the required ones first.
  const required = [];
  const optional = [];
  const docs = [];
  for (const p of op.pathParams) { required.push(`string ${csIdent(camel(p.name))}`); docs.push([camel(p.name), p.description || `The ${p.name} in the path.`]); }
  if (op.body) { required.push(`${op.body.ref} body`); docs.push(["body", "The request body."]); }
  for (const p of op.queryParams) {
    const id = csIdent(camel(p.name));
    if (p.required) { required.push(`${csType(p)} ${id}`); } else { optional.push(`${csType(p)}? ${id} = null`); }
    docs.push([camel(p.name), p.description || `The ${p.name} query value.`]);
  }
  return { required, optional, docs };
}

function csMethod(svc, op) {
  const sig = csSignature(op);
  const returns = op.response ? op.response.ref : "JsonElement";
  const write = op.method !== "GET";
  const extras = [];
  if (write) { extras.push("string? idempotencyKey = null"); sig.docs.push(["idempotencyKey", "A stable key for retried writes; one is made when you pass none."]); }
  extras.push("CancellationToken cancellationToken = default");
  sig.docs.push(["cancellationToken", "Cancels the call."]);
  const args = [...sig.required, ...sig.optional, ...extras].join(", ");
  const pathText = op.path.slice(1).replace(/\{(\w+)\}/g, (_m, n) => `{FinQuery.Segment(${csIdent(camel(n))})}`);
  const tuples = [];
  for (const p of op.queryParams) tuples.push(`("${p.name}", ${csIdent(camel(p.name))})`);
  const template = op.pathParams.length ? `$"${pathText}"` : `"${pathText}"`;
  const url = `FinQuery.Path(${[template, ...tuples].join(", ")})`;
  const out = [];
  out.push(csDoc(docLines(op), "    ", sig.docs));
  out.push(`    public Task<${returns}> ${upperFirst(op.name)}Async(${args})`);
  out.push(`        => _http.SendApiAsync<${returns}>("${op.method}", ${url}, ${op.body ? "body" : "null"}, ${write ? "idempotencyKey" : "null"}, cancellationToken);`);

  if (op.paging) {
    const name = upperFirst(op.name);
    const passPath = op.pathParams.map((p) => csIdent(camel(p.name)));
    const itemType = op.paging.items;
    const pageType = op.response.ref;
    const baseDocs = sig.docs.filter(([n]) => n !== "idempotencyKey");
    const pagesSig = [...sig.required, ...sig.optional, "CancellationToken cancellationToken = default"].join(", ");
    const passArgs = [...passPath, ...(op.body ? ["body"] : [])];
    out.push("");
    const optionalNames = op.queryParams.map((p) => csIdent(camel(p.name)));
    const callArgs = [...passArgs, ...optionalNames.map((n) => (n === "page" ? "n" : n)), "ct"];
    out.push(csDoc(wrap(`Reads every page of ${name}Async, advancing page from the one you pass (1 when unset). It stops after the last page the paging block names, or at the first empty page, which it does not yield.`, 112), "    ", baseDocs));
    out.push(`    public IAsyncEnumerable<${pageType}> ${name}PagesAsync(${pagesSig})`);
    out.push(`        => FinPaging.ByPage<${pageType}, ${itemType}>(page ?? 1,`);
    out.push(`            (n, ct) => ${name}Async(${callArgs.join(", ")}),`);
    out.push(`            p => p.Data, p => p.Paging?.PageNo, p => p.Paging?.PageCount, cancellationToken);`);
    out.push("");
    out.push(csDoc([`Reads every row of every page of ${name}PagesAsync, one at a time.`], "    ", baseDocs));
    const pass = [...passArgs, ...op.queryParams.map((p) => csIdent(camel(p.name))), "cancellationToken"];
    out.push(`    public IAsyncEnumerable<${itemType}> ${name}ItemsAsync(${pagesSig})`);
    out.push(`        => FinPaging.Items<${pageType}, ${itemType}>(${name}PagesAsync(${pass.join(", ")}), p => p.Data);`);
  }
  return out.join("\n");
}

function emitCSharp(svc, ops) {
  const out = [];
  out.push(`// <auto-generated>`);
  out.push(`// Generated by scripts/generate-surface.mjs from spec/${svc.spec}. Do not edit by hand: change the generator or the spec.`);
  out.push(`// </auto-generated>`);
  out.push(`#nullable enable`);
  out.push(`using System.Text.Json;`);
  out.push("");
  out.push(`namespace Fin.${svc.name};`);

  const classes = [];
  for (const surface of ["api", "v1"]) {
    const groups = [...new Set(ops.filter((o) => o.surface === surface).map((o) => o.group))].sort();
    for (const group of groups) {
      const className = `${surface === "v1" ? "V1" : ""}${group}Resource`;
      classes.push({ surface, group, className });
      const where = surface === "api" ? "credit-charged /api" : "plan-included /v1";
      out.push("");
      out.push(csDoc([`${svc.label} ${group}: the ${where} operations.`], ""));
      out.push(`public sealed class ${className}`);
      out.push("{");
      out.push("    private readonly FinHttpTransport _http;");
      out.push(`    internal ${className}(FinHttpTransport http) => _http = http;`);
      for (const op of ops.filter((o) => o.surface === surface && o.group === group)) {
        out.push("");
        out.push(csMethod(svc, op));
      }
      out.push("}");
    }
  }
  for (const surface of ["v1", "api"]) {
    const own = classes.filter((c) => c.surface === surface);
    const name = surface === "v1" ? `Fin${svc.name}V1Resource` : `Fin${svc.name}Resource`;
    out.push("");
    if (surface === "v1") out.push(csDoc([`${svc.label}'s /v1 operations, included in paid plans and gated by package tier.`], ""));
    else out.push(csDoc([`${svc.label}. The groups on this object are the /api operations, charged in credits per call; the /v1 operations, included in paid plans, are under V1.`], ""));
    out.push(`public sealed class ${name}`);
    out.push("{");
    out.push(`    internal ${name}(FinHttpTransport http)`);
    out.push("    {");
    for (const c of own) out.push(`        ${c.group} = new ${c.className}(http);`);
    if (surface === "api") out.push(`        V1 = new Fin${svc.name}V1Resource(http);`);
    out.push("    }");
    for (const c of own) {
      out.push("");
      out.push(csDoc([`${c.group}.`], "    "));
      out.push(`    public ${c.className} ${c.group} { get; }`);
    }
    if (surface === "api") {
      out.push("");
      out.push(csDoc(["The /v1 operations: included in paid plans, gated by package tier."], "    "));
      out.push(`    public Fin${svc.name}V1Resource V1 { get; }`);
    }
    out.push("}");
  }
  return out.join("\n") + "\n";
}

// ---------------------------------------------------------------- run

function write(file, text) {
  const path = join(root, file);
  mkdirSync(dirname(path), { recursive: true });
  writeFileSync(path, text, { encoding: "utf8" });
}

for (const svc of services) {
  const { ops } = readOperations(svc);
  write(`typescript/src/${svc.id}.generated.ts`, emitTypeScript(svc, ops));
  write(`dotnet/src/FinClient/Generated/Fin${svc.name}Resources.g.cs`, emitCSharp(svc, ops));
  console.log(`${svc.label}: ${ops.length} operations`);
}
