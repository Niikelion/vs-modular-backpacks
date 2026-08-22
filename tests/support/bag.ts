/**
 * Shared helpers for the Immersive Backpacks integration tests.
 *
 * Two sources of truth, on purpose:
 *   - STATE (what is attached, how many cargo slots) comes from the bridge's generic `inspect_block`,
 *     which reads the block entity's persisted attribute tree. Nothing mod-specific is needed for it.
 *   - GEOMETRY (which points exist, what they accept, where to aim) comes from `ib_points_at`, which the
 *     mod registers itself — selection boxes are computed from shape markers, so they are in no tree.
 */
import os from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";
import type { VsWorld, Vec3 } from "vsmcp-server/testkit";

// Compiled to tests/dist/support/, so three levels up is the repo root.
const here = path.dirname(fileURLToPath(import.meta.url));
const repoRoot = path.resolve(here, "..", "..", "..");

/**
 * Debug output, not Release: the release build publishes a nested `mod/publish/` copy and Vintage Story
 * refuses a folder mod whose dll isn't at its root — it then loads the assets and silently skips the code.
 */
export const MOD_PATHS = [
  process.env.VSMCP_MOD_PATH ?? path.resolve(repoRoot, "..", "VSMCP", "VSMCP", "bin", "Debug", "Mods"),
  process.env.IB_MOD_PATH ?? path.resolve(repoRoot, "ImmersiveBackpacks", "bin", "Debug", "Mods"),
];

/**
 * The tests get their own game data directory, so the run loads exactly these two mods. Without it the
 * client also scans the developer's own Mods folder, and one mod there with an unresolved dependency
 * drops the client out of the world into an install dialog — the bridge then never comes up.
 */
export const DATA_PATH = process.env.IB_TEST_DATA_PATH ?? path.join(os.tmpdir(), "ib-integration-tests");

export interface BagPoint {
  code: string;
  index: number;
  /** Index to expect back from get_lookat when aimed at this point. */
  selectionBoxIndex: number;
  categories: string[];
  box: { x1: number; y1: number; z1: number; x2: number; y2: number; z2: number };
  /** A point inside this box on the face the player is on — pass straight to look(). */
  aim: Vec3;
}

export interface BlockState {
  code?: string;
  entity?: { class: string; attributes: Record<string, any> };
  container?: { count: number; slots: { slot: number; stack: any }[] };
}

export function inspectBlock(world: VsWorld, pos: Vec3, side: "server" | "client" = "server") {
  return world.act<BlockState>("inspect_block", { x: pos.x, y: pos.y, z: pos.z, side });
}

/** The item code attached at a point, or null. Reads the block entity's own `placed_addons` tree. */
export async function attachedAt(world: VsWorld, pos: Vec3, pointCode: string): Promise<string | null> {
  const state = await inspectBlock(world, pos);
  return state.entity?.attributes?.placed_addons?.[pointCode]?.code ?? null;
}

/** The bag's current cargo slot count — grows as bag addons are attached. */
export async function cargoSlots(world: VsWorld, pos: Vec3): Promise<number> {
  const state = await inspectBlock(world, pos);
  return state.entity?.attributes?.inventory?.qslots ?? 0;
}

/**
 * An attachment point's geometry. Aim is computed against the player's CURRENT position, so call this
 * after moving and before looking.
 */
export async function pointAt(world: VsWorld, pos: Vec3, code: string): Promise<BagPoint> {
  const bag = await world.act<{ found: boolean; block?: string; points?: BagPoint[] }>("ib_points_at", {
    x: pos.x,
    y: pos.y,
    z: pos.z,
  });
  if (!bag.found) throw new Error(`No modular backpack at (${pos.x}, ${pos.y}, ${pos.z}) — found ${bag.block}.`);
  const point = bag.points?.find((p) => p.code === code);
  if (!point) {
    throw new Error(`Bag has no point '${code}'. Points: ${bag.points?.map((p) => p.code).join(", ")}`);
  }
  return point;
}

/**
 * Give an item and get it into the player's HAND.
 *
 * `give_item` uses TryGiveItemstack, which routes by inventory priority — a backpack lands in the
 * character's worn bag slots rather than the hotbar, so aiming and clicking with it would use whatever
 * was already selected. This finds it wherever it landed and moves it out.
 */
export async function giveToHand(world: VsWorld, code: string, quantity = 1): Promise<number> {
  const fragment = code.split(":").pop()!;
  await world.give(code, quantity);

  // give_item applies on the server, the inventory is read from the client — poll rather than sleep.
  const found = await world.waitFor(`'${fragment}' to appear in an inventory`, async () => {
    const hotbar = await slotHolding(world, "hotbar", fragment);
    if (hotbar !== null) return { inv: "hotbar", slot: hotbar };

    const { inventories } = await world.act<{ inventories: { id: string }[] }>("list_inventories");
    for (const inv of inventories) {
      const slot = await slotHolding(world, inv.id, fragment);
      if (slot !== null) return { inv: inv.id, slot };
    }
    return false;
  });

  if (found.inv !== "hotbar") {
    await world.act("move_item", { from: found, to: { inv: "hotbar", slot: 0 } });
  }
  return world.selectItem(fragment);
}

async function slotHolding(world: VsWorld, inv: string, fragment: string): Promise<number | null> {
  const contents = await world.act<{ slots: { slot: number; empty: boolean; code?: string }[] }>(
    "get_inventory",
    { inv },
  );
  const hit = contents.slots.find((s) => !s.empty && s.code?.includes(fragment));
  return hit ? hit.slot : null;
}
