# Tool compatibility assets

Run with `VINTAGE_STORY` pointing at the game installation:

```powershell
dotnet run --project tests/compatibility-assets/CompatibilityAssetTests.csproj
```

The tests apply production patches using the game's `Tavis.JsonPatch` library. They check inventory tags,
attachment categories, optional-mod gating, and preservation of existing tags and attributes. Covered models:
six walking sticks (original and Lite editions), six Dolabra forms, SoldierSpy's warpick, and patched vanilla tools.

Optionally pass a folder containing published mod ZIPs to repeat the checks against upstream assets:

```powershell
dotnet run --project tests/compatibility-assets/CompatibilityAssetTests.csproj -- C:/path/to/test-mods
```

No game process or save is modified. These checks also run as part of the Cake build.
