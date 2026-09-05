# VSMCP integration utilities

Install the local test dependencies once:

```powershell
cd tests
npm install
```

Generate the two-handed toolstrap offset matrix:

```powershell
npm run matrix:toolstrap
```

The Node runner builds the mod and fixture, launches an isolated temporary Vintage Story world, attaches a
toolstrap through the real interaction path, captures every geometry-distinct supported model, and writes an
HTML contact sheet to `tests/artifacts/toolstrap-matrix/index.html`.

Each item must pass the live toolstrap attachment point's acceptance check and transfer into its actual cargo
slot through `TryPutInto` (including the inventory's tag/storage filters). Rejected or missing items remain in
the matrix as red cards with failure reasons; they are never forced into the slot for screenshots. Acceptance
results are also recorded in `manifest.json`. The runner finishes the report before returning a nonzero exit
code if any item failed. Toolsmith rows apply their configured multipart presets before testing insertion.

Run the report regression tests without launching the game:

```powershell
npm run build
node --test dist/toolstrap-matrix-report.test.js
```

Serve the generated matrix to another device on the local network:

```powershell
npm run matrix:serve
```

Environment overrides:

- `VINTAGE_STORY`: Vintage Story installation directory (required).
- `VSMCP_ROOT`: VSMCP repository root; defaults to the sibling `../VSMCP` checkout.
- `VSMCP_MOD_PATH`: built VSMCP Mods directory.
- `IB_MOD_PATH`: built Immersive Backpacks Mods directory.
- `IB_MATRIX_OUTPUT_PATH`: screenshot and HTML output directory.
- `IB_MATRIX_DATA_PATH`: isolated Vintage Story data directory.
- `IB_MATRIX_DOLABRA_PATH`: Dolabra mod archive used by the matrix.
- `IB_MATRIX_TOOLSMITH_PATH`: Toolsmith mod archive used by the matrix.
- `IB_MATRIX_SOLDIERSPY_CRAFTWORKS_PATH`: SoldierSpy Craftworks mod archive used by the matrix.
- `IB_MATRIX_WALKING_STICKS_PATH`: Adventurer's Walking Stick (original or Lite) archive used by the matrix; run each edition separately.
