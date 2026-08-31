import { spawnSync } from "node:child_process";
import { copyFileSync, existsSync, readdirSync, statSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const testsRoot = path.dirname(fileURLToPath(import.meta.url));
const repoRoot = path.dirname(testsRoot);

if (!process.env.VINTAGE_STORY) {
  throw new Error("VINTAGE_STORY must point to the Vintage Story installation directory.");
}

run("dotnet", [
  "run",
  "--project",
  path.join(repoRoot, "CakeBuild", "CakeBuild.csproj"),
  "--",
  "--target",
  "Build",
  "--configuration",
  "Debug",
], repoRoot);

const fixtureProject = path.join(testsRoot, "fixture", "ToolstrapMatrixFixture.csproj");
run("dotnet", ["build", fixtureProject, "-c", "Debug"], repoRoot);

const fixtureMods = path.join(testsRoot, "fixture", "bin", "Debug", "Mods");
const dolabraPath = process.env.IB_MATRIX_DOLABRA_PATH
  ?? path.join(repoRoot, ".compat-test", "mods", "Infantry-dolabra_2.0.2.zip");
const walkingSticksPath = process.env.IB_MATRIX_WALKING_STICKS_PATH
  ?? newestMatchingFile(
    path.join(process.env.APPDATA ?? "", "VintagestoryData", "ModsByServer"),
    /^adventurers-walking-stick-lite.*\.zip$/i,
  );

stageMod(dolabraPath, path.join(fixtureMods, "matrix-dolabra.zip"), "IB_MATRIX_DOLABRA_PATH");
stageMod(walkingSticksPath, path.join(fixtureMods, "matrix-walking-sticks.zip"), "IB_MATRIX_WALKING_STICKS_PATH");

run(process.execPath, [path.join(testsRoot, "node_modules", "typescript", "bin", "tsc")], testsRoot);
run(process.execPath, [path.join(testsRoot, "dist", "toolstrap-matrix.js")], testsRoot);

function run(command, args, cwd) {
  const result = spawnSync(command, args, { cwd, env: process.env, stdio: "inherit" });
  if (result.error) throw result.error;
  if (result.status !== 0) process.exit(result.status ?? 1);
}

function stageMod(source, destination, variableName) {
  if (!source || !existsSync(source)) {
    throw new Error(`Compatibility mod archive not found. Set ${variableName}.`);
  }
  copyFileSync(source, destination);
}

function newestMatchingFile(root, pattern) {
  if (!root || !existsSync(root)) return undefined;

  const matches = [];
  const pending = [root];
  while (pending.length > 0) {
    const directory = pending.pop();
    for (const entry of readdirSync(directory, { withFileTypes: true })) {
      const fullPath = path.join(directory, entry.name);
      if (entry.isDirectory()) pending.push(fullPath);
      else if (entry.isFile() && pattern.test(entry.name)) matches.push(fullPath);
    }
  }

  return matches.sort((a, b) => statSync(b).mtimeMs - statSync(a).mtimeMs)[0];
}
