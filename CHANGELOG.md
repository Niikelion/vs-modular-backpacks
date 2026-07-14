# Changelog

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
