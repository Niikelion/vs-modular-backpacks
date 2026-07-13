# Changelog

## 1.2.0

- **Immersive Backpack Overhaul compatibility.** Pouches and toolstraps now declare themselves as small bags,
  so IBO accepts them in its small-bag slots. Before this, IBO's slot-size gate refused them everywhere except
  the backpack slot, which made them unwearable as standalone bags. Applied only when IBO is installed.
- The sturdy pouch sits lower in its inventory slot, so its taller model no longer rides high.

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
