#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using ImmersiveBackpacks.blocks;
using ImmersiveBackpacks.inventory;
using ImmersiveBackpacks.items;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

internal static class SaveCompatibilityTests
{
    internal static void Run()
    {
        HeldBackpackLoadsAndRoundTrips();
        PlacedBackpackLoadsAndRoundTrips();
        CurrentHeldBagLoadsLegacyState();
        CurrentBlockEntityLoadsLegacyState();
        PlacedItemWornPlacedRoundTrip();
    }

    private static void CurrentHeldBagLoadsLegacyState()
    {
        var context = TestContext.Create();
        var stack = new ItemStack(context.Bag) { Attributes = Load("pre-2.0-held-backpack.tree.b64") };

        var contents = context.Bag.GetContents(stack, context.World);
        Assert(contents is { Length: 3 }, "Current IHeldBag did not reconstruct the legacy slot layout.");
        AssertStack(contents![0], 301, 4, "base-cargo");
        AssertStack(contents[1], 302, 1, "addon-cargo");
        Assert(contents[2] == null, "Current IHeldBag changed the legacy empty slot.");
        AssertCommonState(stack.Attributes);
    }

    private static void CurrentBlockEntityLoadsLegacyState()
    {
        var context = TestContext.Create();
        var blockEntity = context.NewBlockEntity();
        blockEntity.FromTreeAttributes(Load("pre-2.0-placed-backpack.tree.b64"), context.World);

        Assert(blockEntity.BackpackItemCode.Equals(context.Bag.Code), "Current block entity lost the bag type.");
        Assert(Math.Abs(blockEntity.MeshAngleRad - 1.25f) < 0.0001f, "Current block entity lost its angle.");
        AssertStack(blockEntity.AttachedItems[0], 201, 1, "addon-left");
        AssertStack(blockEntity.AttachedItems[1], 202, 1, "addon-top");
        AssertStack(blockEntity.Inventory[0].Itemstack, 301, 4, "base-cargo");
        AssertStack(blockEntity.Inventory[1].Itemstack, 302, 1, "addon-cargo");
        Assert(blockEntity.Inventory[2].Empty, "Current block entity changed the legacy empty slot.");

        var saved = new TreeAttribute();
        blockEntity.ToTreeAttributes(saved);
        var reloaded = context.NewBlockEntity();
        reloaded.FromTreeAttributes(TreeAttribute.CreateFromBytes(saved.ToBytes()), context.World);
        AssertBlockEntityState(reloaded);
    }

    private static void PlacedItemWornPlacedRoundTrip()
    {
        var context = TestContext.Create();
        var placed = context.NewBlockEntity();
        placed.FromTreeAttributes(Load("pre-2.0-placed-backpack.tree.b64"), context.World);

        var itemStack = placed.CreateDropItemStack(context.World);
        Assert(itemStack != null, "Placed legacy backpack could not be converted to an item.");
        AssertCommonState(itemStack!.Attributes);
        var wornContents = context.Bag.GetContents(itemStack, context.World);
        AssertStack(wornContents[0], 301, 4, "base-cargo");
        AssertStack(wornContents[1], 302, 1, "addon-cargo");
        Assert(wornContents[2] == null, "Placed-to-worn conversion changed the empty slot.");

        var placedAgain = context.NewBlockEntity();
        placedAgain.Api = context.Api;
        placedAgain.InitFromItemStack(itemStack, initializeInventory: false);
        AssertBlockEntityState(placedAgain);

        var savedAgain = new TreeAttribute();
        placedAgain.ToTreeAttributes(savedAgain);
        var reloaded = context.NewBlockEntity();
        reloaded.FromTreeAttributes(TreeAttribute.CreateFromBytes(savedAgain.ToBytes()), context.World);
        AssertBlockEntityState(reloaded);
    }

    private static void AssertBlockEntityState(BlockEntityImmersiveBackpack blockEntity)
    {
        AssertStack(blockEntity.AttachedItems[0], 201, 1, "addon-left");
        AssertStack(blockEntity.AttachedItems[1], 202, 1, "addon-top");
        AssertStack(blockEntity.Inventory[0].Itemstack, 301, 4, "base-cargo");
        AssertStack(blockEntity.Inventory[1].Itemstack, 302, 1, "addon-cargo");
        Assert(blockEntity.Inventory[2].Empty, "Empty slot changed during backpack transition.");
    }

    private static void HeldBackpackLoadsAndRoundTrips()
    {
        var legacy = Load("pre-2.0-held-backpack.tree.b64");
        AssertCommonState(legacy);

        var slots = BackpackSaveData.GetHeldSlots(legacy);
        AssertStack(BackpackSaveData.GetStack(slots, "slot-0"), 301, 4, "base-cargo");
        AssertStack(BackpackSaveData.GetStack(slots, "slot-1"), 302, 1, "addon-cargo");
        Assert(BackpackSaveData.GetStack(slots, "slot-2") == null, "Legacy empty held slot must remain empty.");

        var saved = (TreeAttribute)legacy.Clone();
        BackpackSaveData.SetAddons(saved, BackpackSaveData.GetAddons(legacy)!.Clone());
        BackpackSaveData.SetHeldSlots(saved, slots!.Clone());

        var reloaded = TreeAttribute.CreateFromBytes(saved.ToBytes());
        AssertCommonState(reloaded);
        AssertStack(BackpackSaveData.GetStack(BackpackSaveData.GetHeldSlots(reloaded), "slot-0"),
            301, 4, "base-cargo");
        AssertStack(BackpackSaveData.GetStack(BackpackSaveData.GetHeldSlots(reloaded), "slot-1"),
            302, 1, "addon-cargo");
    }

    private static void PlacedBackpackLoadsAndRoundTrips()
    {
        var legacy = Load("pre-2.0-placed-backpack.tree.b64");
        AssertCommonState(legacy);
        Assert(legacy.GetString("backpackItemCode") == "game:backpack-normal",
            "Legacy placed backpack item code was lost.");
        Assert(Math.Abs(legacy.GetFloat("meshAngle") - 1.25f) < 0.0001f,
            "Legacy placed backpack angle was lost.");

        var inventory = LoadInventory(legacy);
        AssertStack(inventory[0].Itemstack, 301, 4, "base-cargo");
        AssertStack(inventory[1].Itemstack, 302, 1, "addon-cargo");
        Assert(inventory[2].Empty, "Legacy empty placed slot must remain empty.");

        var saved = new TreeAttribute();
        saved.SetString("backpackItemCode", legacy.GetString("backpackItemCode"));
        saved.SetFloat("meshAngle", legacy.GetFloat("meshAngle"));
        var savedInventory = new TreeAttribute();
        inventory.ToTreeAttributes(savedInventory);
        saved["inventory"] = savedInventory;
        BackpackSaveData.SetAddons(saved, BackpackSaveData.GetAddons(legacy)!.Clone());

        var reloaded = TreeAttribute.CreateFromBytes(saved.ToBytes());
        AssertCommonState(reloaded);
        Assert(reloaded.GetString("backpackItemCode") == "game:backpack-normal",
            "Placed backpack item code changed during round-trip.");
        Assert(Math.Abs(reloaded.GetFloat("meshAngle") - 1.25f) < 0.0001f,
            "Placed backpack angle changed during round-trip.");
        var reloadedInventory = LoadInventory(reloaded);
        AssertStack(reloadedInventory[0].Itemstack, 301, 4, "base-cargo");
        AssertStack(reloadedInventory[1].Itemstack, 302, 1, "addon-cargo");
        Assert(reloadedInventory[2].Empty, "Empty placed slot changed during round-trip.");
    }

    private static InventoryGeneric LoadInventory(ITreeAttribute tree)
    {
        var savedInventory = tree.GetTreeAttribute("inventory")
            ?? throw new InvalidOperationException("Legacy placed inventory is missing.");
        var inventory = new InventoryGeneric(savedInventory.GetInt("qslots"), "legacy-fixture", null);
        inventory.FromTreeAttributes(savedInventory);
        return inventory;
    }

    private static void AssertCommonState(ITreeAttribute tree)
    {
        var addons = BackpackSaveData.GetAddons(tree);
        AssertStack(BackpackSaveData.GetStack(addons, "left"), 201, 1, "addon-left");
        AssertStack(BackpackSaveData.GetStack(addons, "top"), 202, 1, "addon-top");
    }

    private static void AssertStack(ItemStack? stack, int id, int quantity, string tag)
    {
        Assert(stack != null, $"Expected item {id} is missing.");
        Assert(stack!.Class == EnumItemClass.Item, $"Item {id} changed collectible class.");
        Assert(stack.Id == id, $"Expected item {id}, got {stack.Id}.");
        Assert(stack.StackSize == quantity, $"Item {id} quantity changed.");
        Assert(stack.Attributes.GetString("legacyTag") == tag, $"Item {id} attributes were lost.");
    }

    private static TreeAttribute Load(string name)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "fixtures", name);
        return TreeAttribute.CreateFromBytes(Convert.FromBase64String(File.ReadAllText(path).Trim()));
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

internal sealed class TestContext
{
    private readonly Dictionary<int, Item> itemsById;
    private readonly Dictionary<string, Item> itemsByCode;
    private readonly Block placedBlock;

    internal ItemImmersiveBag Bag { get; }
    internal IWorldAccessor World { get; }
    internal ICoreAPI Api { get; }

    private TestContext(ItemImmersiveBag bag, Dictionary<int, Item> itemsById,
        Dictionary<string, Item> itemsByCode, Block placedBlock, IWorldAccessor world, ICoreAPI api)
    {
        Bag = bag;
        this.itemsById = itemsById;
        this.itemsByCode = itemsByCode;
        this.placedBlock = placedBlock;
        World = world;
        Api = api;
    }

    internal static TestContext Create()
    {
        var bag = new ItemImmersiveBag
        {
            ItemId = 100,
            Code = new AssetLocation("game", "backpack-normal"),
            Attributes = new JsonObject(JObject.Parse("""
            {
              "backpack": { "quantitySlots": 1, "storageFlags": 189 },
              "immersiveBackpack": {
                "attachmentPoints": [
                  { "code": "left", "categories": [], "hitbox": [0, 0, 0, 1, 1, 1] },
                  { "code": "top", "categories": [], "hitbox": [0, 0, 0, 1, 1, 1] }
                ]
              }
            }
            """))
        };
        var itemsById = new Dictionary<int, Item>
        {
            [100] = bag,
            [201] = new TestHeldBagItem(201, "addon-left"),
            [202] = new TestHeldBagItem(202, "addon-top"),
            [301] = NewItem(301, "base-cargo"),
            [302] = NewItem(302, "addon-cargo")
        };
        var itemsByCode = new Dictionary<string, Item>();
        foreach (var item in itemsById.Values) itemsByCode[item.Code.ToString()] = item;

        var placedBlock = new Block
        {
            BlockId = 77,
            Code = new AssetLocation("fixture", "placed-backpack")
        };

        ICoreAPI? api = null;
        var blockAccessor = InterfaceProxy<IBlockAccessor>.Create((_, _) => null);
        var world = InterfaceProxy<IWorldAccessor>.Create((method, args) => method.Name switch
        {
            "get_Api" => api,
            "get_BlockAccessor" => blockAccessor,
            "get_Side" => EnumAppSide.Server,
            "GetItem" when args?[0] is int id => itemsById.GetValueOrDefault(id),
            "GetItem" when args?[0] is AssetLocation code => itemsByCode.GetValueOrDefault(code.ToString()),
            "GetBlock" when args?[0] is int id && id == placedBlock.Id => placedBlock,
            "GetBlock" when args?[0] is AssetLocation => placedBlock,
            _ => InterfaceProxy<IWorldAccessor>.Default(method.ReturnType)
        });
        api = InterfaceProxy<ICoreAPI>.Create((method, _) => method.Name switch
        {
            "get_World" => world,
            "get_Side" => EnumAppSide.Server,
            _ => InterfaceProxy<ICoreAPI>.Default(method.ReturnType)
        });

        typeof(CollectibleObject).GetField("api", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(bag, api);
        return new TestContext(bag, itemsById, itemsByCode, placedBlock, world, api);
    }

    internal BlockEntityImmersiveBackpack NewBlockEntity()
        => new()
        {
            Pos = new BlockPos(4, 5, 6),
            Block = placedBlock
        };

    private static Item NewItem(int id, string code)
        => new() { ItemId = id, Code = new AssetLocation("fixture", code) };
}

internal sealed class TestHeldBagItem : Item, IHeldBag
{
    internal TestHeldBagItem(int id, string code)
    {
        ItemId = id;
        Code = new AssetLocation("fixture", code);
    }

    public int GetQuantitySlots(ItemStack bagstack) => 1;
    public List<ItemSlotBagContent> GetOrCreateSlots(ItemStack bagstack, InventoryBase parentinv,
        int bagIndex, IWorldAccessor world) => throw new NotSupportedException();
    public ItemStack[] GetContents(ItemStack bagstack, IWorldAccessor world) => [];
    public void Store(ItemStack bagstack, ItemSlotBagContent slot) => throw new NotSupportedException();
    public void Clear(ItemStack bagstack) { }
    public bool IsEmpty(ItemStack bagstack) => true;
    public string GetSlotBgColor(ItemStack bagstack) => null!;
    EnumItemStorageFlags IHeldBag.GetStorageFlags(ItemStack bagstack) => EnumItemStorageFlags.General;
    public TagSet GetStorageTags(ItemStack bagStack) => TagSet.Empty;
}

internal class InterfaceProxy<T> : DispatchProxy where T : class
{
    private System.Func<MethodInfo, object?[]?, object?> handler = null!;

    internal static T Create(System.Func<MethodInfo, object?[]?, object?> handler)
    {
        var proxy = Create<T, InterfaceProxy<T>>();
        ((InterfaceProxy<T>)(object)proxy).handler = handler;
        return proxy;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        => targetMethod == null ? null : handler(targetMethod, args);

    internal static object? Default(Type type)
        => type == typeof(void) || !type.IsValueType ? null : Activator.CreateInstance(type);
}
