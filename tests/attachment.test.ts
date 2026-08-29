/**
 * The mod's central gesture, driven end to end: shift + right-click on an attachment point box.
 *
 * This goes through the real input path, so OnPlayerRightClick and everything under it runs exactly as it
 * would for a player — unlike writing the `placed_addons` tree directly, which is what earlier verification
 * rounds had to settle for and which tests the data path rather than the interaction.
 *
 *   cd tests && npm install && npm test
 */
import { test, before, after, describe } from "node:test";
import { strict as assert } from "node:assert";
import { openWorld, type VsWorld } from "vsmcp-server/testkit";
import { MOD_PATHS, DATA_PATH, attachedAt, cargoSlots, giveToHand, pointAt } from "./support/bag.js";

const BACKPACK = "game:backpack-normal";
const POUCH = "immersivemodularbackpacks:pouch-normal";

let world: VsWorld;

before(async () => {
  world = await openWorld({ modPaths: MOD_PATHS, dataPath: DATA_PATH, timeoutMs: 180_000 });
}, { timeout: 200_000 });

after(async () => {
  await world?.close();
}, { timeout: 60_000 });

/** Place a bag on the arena floor and return where it landed. */
async function placeBag(arena: Awaited<ReturnType<VsWorld["arena"]>>) {
  const ground = arena.at(2, 0, 0);
  const bagPos = arena.at(2, 1, 0);

  await giveToHand(world, BACKPACK);
  await world.look(arena.topOf(ground));
  await world.use({ shift: true });
  await world.waitForBlock(bagPos, "backpack-placed-normal");

  return bagPos;
}

/**
 * Aim at a point and confirm the crosshair really is on it. The point boxes overlap the bag's body box, so
 * a near miss silently targets the body — where shift + right-click means "pick the bag up", not "attach".
 * Asserting the box index turns that into a clear failure instead of "the attach did nothing".
 */
async function aimAtPoint(bagPos: { x: number; y: number; z: number }, code: string) {
  const point = await pointAt(world, bagPos, code);
  await world.look(point.aim);

  const look = await world.act<any>("get_lookat");
  assert.equal(
    look.selection?.block?.selectionBoxIndex,
    point.selectionBoxIndex,
    `crosshair is on box ${look.selection?.block?.selectionBoxIndex}, not ${code}'s ${point.selectionBoxIndex}`,
  );
  return point;
}

describe("attaching addons to a placed backpack", () => {
  test("shift + right-click on a pouch point attaches the pouch", async () => {
    const arena = await world.arena();
    const bagPos = await placeBag(arena);

    assert.equal(await attachedAt(world, bagPos, "left_pouch"), null, "left_pouch should start empty");

    await giveToHand(world, POUCH);
    await aimAtPoint(bagPos, "left_pouch");
    await world.use({ shift: true });

    const attached = await world.waitFor("the pouch to attach to left_pouch", async () => {
      const code = await attachedAt(world, bagPos, "left_pouch");
      return code?.includes("pouch-normal") ? code : false;
    });
    assert.match(attached, /pouch-normal/);
  });

  test("a point that doesn't accept the category refuses the pouch", async () => {
    const arena = await world.arena();
    const bagPos = await placeBag(arena);

    await giveToHand(world, POUCH);
    const lantern = await aimAtPoint(bagPos, "lantern");
    assert.ok(!lantern.categories.includes("pouch"), "precondition: lantern must not accept pouches");

    await world.use({ shift: true });
    await world.wait(20);

    assert.equal(
      await attachedAt(world, bagPos, "lantern"),
      null,
      "lantern point accepted a pouch it should have rejected",
    );

    // The gesture is consumed either way, so the pouch must still be in hand rather than placed as a block.
    await world.expectItem("pouch-normal");
  });

  test("attaching a pouch adds its cargo slots to the bag", async () => {
    const arena = await world.arena();
    const bagPos = await placeBag(arena);

    const baseline = await cargoSlots(world, bagPos);

    await giveToHand(world, POUCH);
    await aimAtPoint(bagPos, "left_pouch");
    await world.use({ shift: true });

    const grown = await world.waitFor("the bag's cargo to grow", async () => {
      const slots = await cargoSlots(world, bagPos);
      return slots > baseline ? slots : false;
    });
    assert.equal(grown, baseline + 3, "pouch-normal contributes 3 slots");
  });
});
