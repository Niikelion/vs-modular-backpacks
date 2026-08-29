# Changelog

## 1.8.0

- **Breaking, for mod authors: the shared attachment transform moved to its own top-level attribute**, and is now
  written in the same shape as every other transform in the game:

  ```json
  "immersiveBackpackAttachment": { "category": "twohanded" },
  "immersiveAttachedTransform": {
    "translation": { "x": 0.18, "y": 0.045, "z": 0.038 },
    "scale": 1.32
  }
  ```

  The old `immersiveBackpackAttachment.attachedTransform`, with its `offset`/`rotation` arrays, is still read when
  the new attribute is absent, so existing assets and third-party patches keep working. `placed`, `worn`,
  `toolTransform` and the rest are unchanged.

  The reason is tooling. Vanilla's Transform Editor stores an extra transform at a *top-level* collectible
  attribute in ModelTransform form; this mod stored it nested and in its own format, so the editor tab only worked
  because we hijacked its get/set events. That put the live-tuned number somewhere no write-back tool could find:
  `/tfedit` could show it, but nothing could save it. With the storage matching what the editor already does, the
  tab needs no interception, and a transform tuned in game can be written straight back into the asset - which is
  what that editor tab was built for.

## 1.7.0

- **Breaking, for mod authors: an item must now declare a category to go on a toolstrap.** A strap used to take
  anything the game calls a pickaxe, axe, shovel, hoe or prospecting pick, so modded tools landed there by
  accident as much as by design. A strap now names the categories its point accepts, the way a backpack does, and
  an item that declares none does not strap - tool or not. The category is `twohanded`, since a strap is shaped
  to carry a tool you hold in both hands. Vanilla's axes, pickaxe, shovel, hoe and prospecting pick are patched
  in; anything else opts in with one line next to the transform it already has:

  ```json
  "immersiveBackpackAttachment": {
    "category": "twohanded",
    "attachedTransform": { "scale": 1.32, "offset": [0.18, 0.045, 0.038] }
  }
  ```

- **Toolsmith compatibility.** A tinkered tool on a worn toolstrap showed the plain vanilla tool instead of the
  head, handle and binding you built it from. Worn geometry has to be a shape rather than a mesh - it hangs off
  the player's skeleton - so Toolsmith's own renderer could not serve it; the same parts are now composed into a
  shape for that path.

## 1.5.0

- **Custom storage flags for mod authors.** A bag's own slots and an addon's slots were locked to our presets, so
  a compat patch could not make a bag hold only what it should. Both now take an optional `storageFlags` (a raw
  bitmask, or flag names such as `"Metallurgy"` or `["General", "Agriculture"]`) and `slotBgColor` - on the bag's
  `backpack` attributes, and on an addon's `immersiveBackpackAttachment`. Unset means the old behaviour.

## 1.4.0

- **Slot outlines now tell you what fits.** Hold an addon and point at a slot on a placed backpack: the outline
  turns green where it would attach and red where it would not, so you can find the right point without trying
  every one of them. With an empty hand the outline stays as it always was.

## 1.3.2

- **Sacks no longer swallow your backpack.** A linen or mining sack attached at its own size, a good deal larger
  than a pouch in the very same slot, so a pair of them hid the pack and everything else on it. They now sit at
  pouch size.
- **Fixed: the attach help only listed what fits the lantern point.** Look at an empty slot and it cycles through
  every addon that can go there; every point except the lantern's showed nothing, because it took the list from a
  form of creative-inventory entry that only the lantern happens to use.

## 1.3.1

- **Player Model Library compatibility.** On a custom player model - Rust Girls, for instance - a worn backpack
  showed up but everything attached to it did not. PlayerModelLib rebuilds worn gear from the bag's own shape for
  any model other than the seraph, which threw away the pouches, lanterns and straps we had composed onto it. The
  addons are now composed again onto the shape it produces.

## 1.3.0

- **Roll-up bed compatibility.** Its mattresses attach to a sturdy backpack's top strap, just like the hay bed.
  Their rolled-up shape takes its texture from the block rather than from the shape file, which an attached addon
  could not resolve before - so a bag addon now looks up both.

## 1.2.3

- **New standard backpack model**, by MeadowTealeaf.

## 1.2.2

- **Storage Tweaks compatibility.** Its sort and stack buttons appeared on a backpack but did nothing, whether
  the pack was placed or worn. It picks the slots it may touch by exact type name, so every slot in our bags -
  which are our own classes - was skipped, leaving it nothing to sort. Ordinary and ore slots are now plain
  vanilla slots, which they never needed to stop being. Tool slots stay ours, so a sort leaves the tools on your
  toolstrap where you put them.

## 1.2.1

- For modders: a tool's transform on a toolstrap can be tuned live too. Hold a second copy of the tool and use
  the transform editor's "Immersive attachment" tab; a placed bag now rebuilds as you drag, and an item that
  carries no attributes of its own (most vanilla tools) gets them created rather than dropping the edit.

## 1.2.0

- **Strap a hay bed to your pack.** It rides on the sturdy backpack's top point - the same one a toolstrap uses,
  so it's one or the other - and shows as a rolled bedroll rather than a whole bed.
- **Immersive Backpack Overhaul compatibility.** Pouches and toolstraps now declare themselves as small bags,
  so IBO accepts them in its small-bag slots. Before this, IBO's slot-size gate refused them everywhere except
  the backpack slot, which made them unwearable as standalone bags. Applied only when IBO is installed.
- The sturdy pouch sits lower in its inventory slot, so its taller model no longer rides high.
- Fixed: an attached *block* ignored the smaller shape it declares for being attached, and drew its full block
  shape instead. Only the hay bed exercises this today, but it would have hit any block addon.
- For modders: an addon's attached transform can now be tuned live in-game. Hold the addon, open the transform
  editor (`.tfedit`) and pick the "Immersive attachment" tab; the values map straight onto the item's
  `immersiveBackpackAttachment.attachedTransform`.

## 1.1.1

- **Fixed: junk ending up on your toolstraps.** With a full hotbar and inventory, a picked-up item could be
  stuffed into a toolstrap's tool slot - hence the firelogs and gears riding on people's backs. Tool slots now
  reject non-tools when the game auto-fills them, not just when you drag something in by hand. Anything already
  sitting in a tool slot stays there and can be dragged out as usual.
- **Bags render on Equus horses.** A bag in a mount's bag slot is now drawn with its attachments, using a model
  posed for the animal. Needs the companion mod, *Modular Backpacks: Equus Compatibility*.

## 1.1.0

- **New backpack and pouch models**, contributed by MeadowTealeaf.
- Worn-shape lookup now goes through the game's own per-slot shape resolution rather than reading the worn shape
  attribute directly. Mods that relocate that attribute (Equus does) no longer strip the bag off your back.

## 1.0.2

- Mod description: pouch and toolstrap cards, updated controls.
- MIT license.
- CI updates.

## 1.0.1

- **Fixed: invisible backpacks when Equus is installed.** Equus moves the vanilla worn-bag shape into a per-slot
  map; we read the old location, got nothing, and drew no bag at all - for every worn bag, not just ours.
