# Modular Backpacks: KCs DragonFolk Compatibility

Content-only companion mod. Hard-depends on `kcsdragons` and `immersivemodularbackpacks`, so it loads after
KCs DragonFolk and its assets win.

## What it does

KCs DragonFolk uses PlayerModelLib's `model-replacements-byshape` config to swap the vanilla backpack's worn
shape (`game:item/bag/backpack-{type}-attached`) for its own (`kcsdragons:item/backpack-{type}-attached`) on
dragon models. Its replacement shape has none of our `slot_*` attachment-point markers, so worn addons had
nowhere to attach and silently vanished.

This mod **overrides those two replacement shapes** with our own attached bag, plus KCs' tail-bags grafted back
on. Because we load after KCs, our files at `assets/kcsdragons/shapes/item/backpack-{type}-attached.json` win, and
PML's replacement now resolves to our marker-bearing shape. Dragons wear the standard modular backpack with every
addon working, and still keep the dragon-flavour tail-bags.

We do not touch KCs' code, config, or any other shape - only the two backpack shapes it substitutes for ours.

## What's in the override shapes

Each `backpack-{type}-attached.json` here is:

1. Our own `ImmersiveBackpacks/assets/game/shapes/item/bag/backpack-{type}-attached.json` (bag body + `slot_*`
   attachment-point markers), with texture paths qualified to `game:` (they live in the `kcsdragons` domain here,
   so unqualified paths would misresolve).
2. Plus the four tail-bag root elements copied verbatim from KCs' original shape - `Big-Tailbag`, `Small-Tailbag`,
   `Gecc-Tailbag`, `Nub-Tailbag`. Each step-parents to a tail bone (`Big-Tail2` etc.) that a *tail variant* skin
   part provides, so only the one matching the player's chosen tail renders; the rest are silently skipped (no
   error). We include all four - matching what KCs' backpack bundled - so any tail choice keeps its bag, including
   tail variants that don't exist yet. Their faces reference `#leather`/`#midtone`, renamed here to dedicated
   `#kcsleather`/`#kcsmidtone` keys (with KCs' `game:`-qualified paths) so they never collide with our body's
   own texture keys.

## Maintenance

These two shapes are **static, hand-generated** (no build step, to keep this mod pure content). If our source
attached shapes change - geometry or, especially, the `slot_*` markers - regenerate: re-copy our attached shape,
qualify its textures to `game:`, then re-append KCs' four `*-Tailbag` root subtrees with their face textures
renamed to `#kcsleather`/`#kcsmidtone`. If KCs restructures its tail-bags or adds a new tail variant, re-copy the
tail subtrees from their updated shape.
