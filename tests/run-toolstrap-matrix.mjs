import { spawnSync } from "node:child_process";
import { copyFileSync, cpSync, existsSync, mkdirSync, readdirSync, rmSync } from "node:fs";
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
const builtModPath = process.env.IB_MOD_PATH
  ?? path.join(repoRoot, "ImmersiveBackpacks", "bin", "Debug", "Mods", "immersivemodularbackpacks");
const stagedModPath = path.join(fixtureMods, "matrix-immersive-backpacks");
const dolabraPath = process.env.IB_MATRIX_DOLABRA_PATH
  ?? path.join(repoRoot, ".compat-test", "mods", "Infantry-dolabra_2.0.2.zip");
const toolsmithPath = process.env.IB_MATRIX_TOOLSMITH_PATH
  ?? path.join(repoRoot, ".compat-test", "mods", "toolsmith_1.2.19.zip");
const soldierSpyCraftworksPath = process.env.IB_MATRIX_SOLDIERSPY_CRAFTWORKS_PATH
  ?? path.join(repoRoot, ".compat-test", "mods", "SoldierSpy-Craftworks-1.4.1.zip");
const walkingSticksPath = process.env.IB_MATRIX_WALKING_STICKS_PATH
  ?? path.join(repoRoot, ".compat-test", "mods", "adventurers-walking-stick-net10_3.0.9.zip");

stageMod(dolabraPath, path.join(fixtureMods, "matrix-dolabra.zip"), "IB_MATRIX_DOLABRA_PATH");
stageMod(toolsmithPath, path.join(fixtureMods, "matrix-toolsmith.zip"), "IB_MATRIX_TOOLSMITH_PATH");
stageMod(soldierSpyCraftworksPath, path.join(fixtureMods, "matrix-soldierspy-craftworks.zip"), "IB_MATRIX_SOLDIERSPY_CRAFTWORKS_PATH");
stageMod(walkingSticksPath, path.join(fixtureMods, "matrix-walking-sticks.zip"), "IB_MATRIX_WALKING_STICKS_PATH");
stageFolderMod(builtModPath, stagedModPath, "IB_MOD_PATH");
process.env.IB_MOD_PATH = fixtureMods;

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

function stageFolderMod(source, destination, variableName) {
  if (!source || !existsSync(source)) {
    throw new Error(`Built mod folder not found. Set ${variableName}.`);
  }

  rmSync(destination, { recursive: true, force: true });
  mkdirSync(destination, { recursive: true });
  for (const entry of readdirSync(source, { withFileTypes: true })) {
    if (entry.name === "publish") continue;
    cpSync(path.join(source, entry.name), path.join(destination, entry.name), { recursive: true });
  }
}
