import { readFile } from "node:fs/promises";
import { basename } from "node:path";

// One or more <snapshot.json> <live.json> pairs, for example:
//   node scripts/check-spec-drift.mjs spec/fincopy-v1.json live/fincopy-v1.json spec/fingraph-v1.json live/fingraph-v1.json
const paths = process.argv.slice(2);
if (paths.length === 0 || paths.length % 2 !== 0) {
  console.error("Usage: node scripts/check-spec-drift.mjs <snapshot.json> <live.json> [<snapshot.json> <live.json> ...]");
  process.exit(2);
}
function stable(value) {
  if (Array.isArray(value)) return value.map(stable);
  if (value && typeof value === "object") {
    return Object.fromEntries(Object.keys(value).sort().map(key => [key, stable(value[key])]));
  }
  // A description written on Windows carries CRLF; the live API is built on Linux and carries LF. Same text.
  if (typeof value === "string") return value.replace(/\r\n/g, "\n");
  return value;
}

function changedPaths(left, right, path = "$", result = []) {
  if (result.length >= 30 || Object.is(left, right)) return result;
  if (Array.isArray(left) && Array.isArray(right)) {
    if (left.length !== right.length) result.push(`${path}.length`);
    for (let i = 0; i < Math.min(left.length, right.length) && result.length < 30; i++) changedPaths(left[i], right[i], `${path}[${i}]`, result);
    return result;
  }
  if (left && right && typeof left === "object" && typeof right === "object" && !Array.isArray(left) && !Array.isArray(right)) {
    const keys = new Set([...Object.keys(left), ...Object.keys(right)]);
    for (const key of [...keys].sort()) {
      if (!(key in left) || !(key in right)) result.push(`${path}.${key}`);
      else changedPaths(left[key], right[key], `${path}.${key}`, result);
      if (result.length >= 30) break;
    }
    return result;
  }
  result.push(path);
  return result;
}

// Key order, whitespace and line endings are ignored; every value, operation id included, must match.
let drifted = false;
for (let i = 0; i < paths.length; i += 2) {
  const [snapshotPath, livePath] = [paths[i], paths[i + 1]];
  const name = basename(snapshotPath);
  const live = JSON.parse(await readFile(livePath, "utf8"));
  // A live document with no schemas is a site that is still deploying, not a contract change to snapshot.
  if (!live.components?.schemas || Object.keys(live.components.schemas).length === 0) {
    console.error(`The live document for ${name} has no schemas; it is not a usable contract.`);
    drifted = true;
    continue;
  }
  const normalizedSnapshot = stable(JSON.parse(await readFile(snapshotPath, "utf8")));
  const normalizedLive = stable(live);
  if (JSON.stringify(normalizedSnapshot) !== JSON.stringify(normalizedLive)) {
    console.error(`The OpenAPI contract behind ${name} changed. Changed paths:`);
    for (const path of changedPaths(normalizedSnapshot, normalizedLive)) console.error(`- ${path}`);
    console.error(`Review the live document, update spec/${name}, and regenerate the SDK types (scripts/generate-all.ps1).`);
    drifted = true;
  } else {
    console.log(`The live OpenAPI contract matches spec/${name}.`);
  }
}
if (drifted) process.exit(1);
