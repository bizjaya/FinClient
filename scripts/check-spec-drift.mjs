import { readFile } from "node:fs/promises";

const [snapshotPath, livePath] = process.argv.slice(2);
if (!snapshotPath || !livePath) {
  console.error("Usage: node scripts/check-spec-drift.mjs <snapshot.json> <live.json>");
  process.exit(2);
}

const snapshot = JSON.parse(await readFile(snapshotPath, "utf8"));
const live = JSON.parse(await readFile(livePath, "utf8"));
function stable(value) {
  if (Array.isArray(value)) return value.map(stable);
  if (value && typeof value === "object") {
    return Object.fromEntries(Object.keys(value).sort().map(key => [key, stable(value[key])]));
  }
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

// Key order and whitespace are ignored; every value, operation id included, must match.
const normalizedSnapshot = stable(snapshot);
const normalizedLive = stable(live);
if (JSON.stringify(normalizedSnapshot) !== JSON.stringify(normalizedLive)) {
  console.error("The FinCopy OpenAPI contract changed. Changed paths:");
  for (const path of changedPaths(normalizedSnapshot, normalizedLive)) console.error(`- ${path}`);
  console.error("Review the live document, update spec/fincopy-v1.json, and regenerate both DTO sets.");
  process.exit(1);
}
console.log("The live FinCopy OpenAPI contract matches spec/fincopy-v1.json.");
