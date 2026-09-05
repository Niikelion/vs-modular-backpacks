using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

internal static class BackpackAssetTests
{
    public static void Run()
    {
        const string configPath = "/attributes/immersiveBackpackByType";
        var patches = JArray.Parse(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "assets", "vanilla-backpack.json")));
        var configPatch = patches.Single(p => (string)p["path"] == configPath);
        var variants = (JObject)configPatch["value"];
        var lightPatches = patches.Where(p =>
            (string)p["condition"]?["when"] == "immersivebackpacksLightSlots").ToArray();

        Assert(lightPatches.Length == variants.Count,
            "Each backpack variant must have exactly one light-slot toggle patch.");

        foreach (var variant in variants.Properties())
        {
            var points = (JArray)variant.Value["attachmentPoints"];
            var lantern = points.Single(p => (string)p["code"] == "lantern");
            var categories = (JArray)lantern["categories"];
            var light = categories.Single(c => (string)c == "light");
            string path = $"{configPath}/{variant.Name}/attachmentPoints/{points.IndexOf(lantern)}"
                + $"/categories/{categories.IndexOf(light)}";

            Assert(lightPatches.Count(p =>
                    (string)p["path"] == path
                    && (string)p["op"] == "remove"
                    && (string)p["file"] == (string)configPatch["file"]
                    && (string)p["condition"]["isValue"] == "false") == 1,
                $"{variant.Name}: disabling light slots must remove only the lantern's light category.");
        }

        Console.WriteLine("Backpack light-slot asset tests passed.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
