import path from "node:path";
import os from "node:os";
import { copyFile, mkdir, rm, writeFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";
import { openWorld, type VsWorld, type Vec3 } from "vsmcp-server/testkit";
import { MOD_PATHS, attachedAt, cargoSlots } from "./support/bag.js";
import { matrixHtml, type MatrixItem, type MatrixEntry, type SlotAcceptance } from "./support/toolstrap-matrix-report.js";

// One representative per distinct model shape. Material-only texture variants are intentionally omitted.
const ITEMS: MatrixItem[] = [
  { group: "Metal axe", code: "game:axe-scrap-scrap", label: "Scrap axe" },
  { group: "Metal axe", code: "game:axe-felling-copper", label: "Felling axe — copper model" },
  { group: "Metal axe", code: "game:axe-felling-iron", label: "Felling axe — iron model" },
  { group: "Metal axe", code: "game:axe-bearded-ruined", label: "Ruined bearded axe" },
  { group: "Metal axe", code: "game:axe-battle-ruined", label: "Ruined battle axe" },
  { group: "Metal axe", code: "game:axe-bardiche-ruined", label: "Ruined bardiche" },
  { group: "Metal axe", code: "game:axe-double-ruined", label: "Ruined double axe" },
  { group: "Stone axe", code: "game:axe-flint", label: "Stone axe" },
  { group: "Pickaxe", code: "game:pickaxe-copper", label: "Pickaxe — copper model" },
  { group: "Pickaxe", code: "game:pickaxe-iron", label: "Pickaxe — iron model" },
  { group: "Shovel", code: "game:shovel-flint", label: "Shovel — stone model" },
  { group: "Shovel", code: "game:shovel-copper", label: "Shovel — metal model" },
  { group: "Hoe", code: "game:hoe-flint", label: "Hoe — stone model" },
  { group: "Hoe", code: "game:hoe-copper", label: "Hoe — metal model" },
  { group: "Prospecting pick", code: "game:prospectingpick-copper", label: "Prospecting pick" },
  { group: "Spear", code: "game:spear-generic-flint", label: "Spear — stone model" },
  { group: "Spear", code: "game:spear-generic-copper", label: "Spear — metal model" },
  { group: "Spear", code: "game:spear-scrap-scrap", label: "Scrap spear" },
  { group: "Spear", code: "game:spear-generic-hacking", label: "Hacking spear" },
  { group: "Spear", code: "game:spear-generic-ornategold", label: "Ornate gold spear" },
  { group: "Spear", code: "game:spear-generic-ornatesilver", label: "Ornate silver spear" },
  { group: "Spear", code: "game:spear-generic-erel", label: "Erel spear" },
  { group: "Spear", code: "game:spear-boar-ruined", label: "Ruined boar spear" },
  { group: "Spear", code: "game:spear-voulge-ruined", label: "Ruined voulge" },
  { group: "Spear", code: "game:spear-fork-ruined", label: "Ruined fork" },
  { group: "Spear", code: "game:spear-ranseur-ruined", label: "Ruined ranseur" },
  { group: "Scythe", code: "game:scythe-copper", label: "Scythe" },
  { group: "Fishing pole", code: "game:fishingpole-simple-bamboo", label: "Fishing pole — bamboo" },
  { group: "Fishing pole", code: "game:fishingpole-simple-wood", label: "Fishing pole — wood" },
  { group: "Toolsmith", code: "game:axe-felling-iron", label: "Toolsmith — axe", preset: "toolsmith:axe" },
  { group: "Toolsmith", code: "game:pickaxe-iron", label: "Toolsmith — pickaxe", preset: "toolsmith:pickaxe" },
  { group: "Toolsmith", code: "game:shovel-copper", label: "Toolsmith — shovel", preset: "toolsmith:shovel" },
  { group: "Toolsmith", code: "game:scythe-copper", label: "Toolsmith — scythe", preset: "toolsmith:scythe" },
  { group: "Dolabra", code: "dolabra:dolabra-axe", label: "Dolabra — axe" },
  { group: "Dolabra", code: "dolabra:dolabra-pick", label: "Dolabra — pick" },
  { group: "Dolabra", code: "dolabra:dolabra-blackbronze-axe", label: "Dolabra — black bronze axe" },
  { group: "Dolabra", code: "dolabra:dolabra-blackbronze-pick", label: "Dolabra — black bronze pick" },
  { group: "Dolabra", code: "dolabra:dolabra-steel-axe", label: "Dolabra — steel axe" },
  { group: "Dolabra", code: "dolabra:dolabra-steel-pick", label: "Dolabra — steel pick" },
  { group: "Walking stick", code: "walkingstick:walkingstick", label: "Walking stick" },
  { group: "Walking stick", code: "walkingstick:walkingstick-cowskull", label: "Walking stick — cow skull" },
  { group: "Walking stick", code: "walkingstick:walkingstick-crude", label: "Walking stick — crude" },
  { group: "Walking stick", code: "walkingstick:walkingstick-fine", label: "Walking stick — fine" },
  { group: "Walking stick", code: "walkingstick:walkingstick-lantern-copper", label: "Walking stick — lantern" },
  { group: "Walking stick", code: "walkingstick:walkingstick-reinforced", label: "Walking stick — reinforced" },
  { group: "Walking stick", code: "walkingstick:walkingstick-architect", label: "Walking stick — architect" },
  { group: "Walking stick", code: "walkingstick:walkingstick-blackguard", label: "Walking stick — blackguard spearstaff" },
  { group: "Walking stick", code: "walkingstick:walkingstick-blackthorn", label: "Walking stick — blackthorn" },
  { group: "Walking stick", code: "walkingstick:walkingstick-forlorn", label: "Walking stick — forlorn spearstaff" },
  { group: "Walking stick", code: "walkingstick:walkingstick-gas", label: "Walking stick — gas lamp" },
  { group: "Walking stick", code: "walkingstick:walkingstick-gun-sheathed", label: "Walking stick — hidden gearlock" },
  { group: "Walking stick", code: "walkingstick:walkingstick-jonas", label: "Walking stick — Jonas ash-spitter" },
  { group: "Walking stick", code: "walkingstick:walkingstick-pathfinder", label: "Walking stick — pathfinder" },
  { group: "Walking stick", code: "walkingstick:walkingstick-sheathed", label: "Walking stick — hidden blade" },
  { group: "Walking stick", code: "walkingstick:walkingstick-shepherds", label: "Walking stick — shepherd's axe" },
  { group: "Walking stick", code: "walkingstick:walkingstick-sling", label: "Walking stick — staff sling" },
  { group: "Walking stick", code: "walkingstick:walkingstick-witch", label: "Walking stick — witch" },
  { group: "SoldierSpy Craftworks", code: "soldierspycraftworks:warpick", label: "SoldierSpy — Blackguard Warpick" },
];

const here = path.dirname(fileURLToPath(import.meta.url));
const repoRoot = path.resolve(here, "..", "..");
const outputDir = process.env.IB_MATRIX_OUTPUT_PATH
  ? path.resolve(process.env.IB_MATRIX_OUTPUT_PATH)
  : path.join(repoRoot, "tests", "artifacts", "toolstrap-matrix");
const dataPath = process.env.IB_MATRIX_DATA_PATH ?? path.join(os.tmpdir(), "ib-toolstrap-matrix");
const fixtureModPath = path.join(repoRoot, "tests", "fixture", "bin", "Debug", "Mods");

let world: VsWorld | undefined;

try {
  await rm(outputDir, { recursive: true, force: true });
  await mkdir(outputDir, { recursive: true });

  world = await openWorld({
    modPaths: [...MOD_PATHS, fixtureModPath],
    dataPath,
    playStyle: "creativebuilding",
    timeoutMs: 180_000,
  });

  const arena = await world.arena(7);
  const bagPos = await placeConfiguredBag(world, arena);
  const toolSlot = (await cargoSlots(world, bagPos)) - 1;
  if (toolSlot < 0) throw new Error("Toolstrap did not contribute a cargo slot.");

  // Close three-quarter view of the placed bag keeps the slot anchor and long tools readable.
  // Camera sits half a block below the bag base so the bag reads higher in frame.
  await world.teleport({ x: bagPos.x - 0.5, y: bagPos.y - 0.5, z: bagPos.z - 0.5 });
  const focus = { x: bagPos.x + 0.5, y: bagPos.y + 0.62, z: bagPos.z + 0.5 };
  await world.look(focus);
  await world.act("hotkey", { code: "togglehud" }); // strip HUD/hotbar/crosshair so shots show only the bag
  await world.wait(320); // let the automatic welcome/chat overlay fade before the first frame

  const manifest: MatrixEntry[] = [];
  for (let i = 0; i < ITEMS.length; i++) {
    const item = ITEMS[i];
    const result = await world.act<SlotAcceptance & { ok: boolean; error?: string; code?: string }>("ib_matrix_set_tool", {
      ...bagPos,
      slot: toolSlot,
      code: item.code,
      preset: item.preset,
    });
    if (!result.ok) throw new Error(`${item.code}: ${result.error}`);

    if (typeof result.accepted !== "boolean" || typeof result.available !== "boolean" || !result.reason)
      throw new Error("Matrix fixture did not report slot acceptance. Rebuild the fixture before generating the matrix.");

    await world.waitFor(`${item.code} slot acceptance to synchronize`, async () => {
      const state = await world!.inspectBlock(bagPos);
      if (!state.container) return false;
      const stack = state.container.slots.find((slot) => slot.slot === toolSlot)?.stack;
      return result.accepted ? stack?.code === item.code : !stack;
    });
    await world.look(focus);
    await world.wait(6);

    const shot = await world.screenshot();
    const file = `${String(i + 1).padStart(2, "0")}-${slug(item.label)}.png`;
    await copyFile(shot.path, path.join(outputDir, file));
    manifest.push({ ...item, file, accepted: result.accepted, available: result.available,
      inventoryAccepted: result.inventoryAccepted, attachmentAccepted: result.attachmentAccepted,
      moved: result.moved, reason: result.reason });
    console.log(`[${i + 1}/${ITEMS.length}] ${result.accepted ? "PASS" : "FAIL"} ${item.label}: ${result.reason}`);
  }

  await writeFile(path.join(outputDir, "manifest.json"), JSON.stringify(manifest, null, 2) + "\n");
  await writeFile(path.join(outputDir, "index.html"), matrixHtml(manifest));
  console.log(`\nMatrix: ${path.join(outputDir, "index.html")}`);
  if (manifest.some(item => !item.accepted)) process.exitCode = 1;
} finally {
  await world?.close();
}

async function placeConfiguredBag(
  activeWorld: VsWorld,
  arena: Awaited<ReturnType<VsWorld["arena"]>>,
): Promise<Vec3> {
  const pedestal = arena.at(2, 1, 0);
  const bagPos = arena.at(2, 2, 0);

  await activeWorld.place("game:rock-granite", pedestal);
  await activeWorld.place("immersivemodularbackpacks:backpack-placed-normal", bagPos);
  await activeWorld.waitForBlock(bagPos, "backpack-placed-normal");

  const attachResult = await activeWorld.act<{ ok: boolean; error?: string }>("ib_matrix_attach_toolstrap", { ...bagPos });
  if (!attachResult.ok) throw new Error(`Toolstrap setup failed: ${attachResult.error}`);
  await activeWorld.waitFor("the toolstrap to attach", async () =>
    (await attachedAt(activeWorld, bagPos, "left_strap"))?.includes("toolstrap") ?? false,
  );
  await activeWorld.breakBlock(pedestal);

  return bagPos;
}

function slug(value: string): string {
  return value.toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/^-|-$/g, "");
}
