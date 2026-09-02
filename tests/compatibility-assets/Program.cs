using System.IO.Compression;
using JsonPatch.Operations;
using JsonPatch.Operations.Abstractions;
using Newtonsoft.Json.Linq;
using Tavis;

var patches = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "patches"), "*.json")
    .SelectMany(path => JArray.Parse(File.ReadAllText(path)).Cast<JObject>()).ToArray();
var strap = JObject.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "toolstrap.json")));
string requiredTag = strap["behaviors"]!.Single(b => (string?)b["name"] == "HeldBag")!["properties"]!["tags"]![0]!.Value<string>()!;
string requiredCategory = strap["attributes"]!["immersiveBackpack"]!["attachmentPoints"]![0]!["categories"]![0]!.Value<string>()!;
string[] stickNames = ["walkingstick", "walkingstick-cowskull", "walkingstick-crude", "walkingstick-fine", "walkingstick-lantern", "walkingstick-reinforced"];
var targets = stickNames.Select(name => (mod: "walkingsticklite", file: $"walkingstick:itemtypes/tool/walkingsticks/{name}.json"))
    .Concat(stickNames.Select(name => (mod: "walkingstick", file: $"walkingstick:itemtypes/tool/walkingsticks/{name}.json")))
    .Concat(new[] { "dolabra-axe", "dolabra-pick", "dolabra-blackbronze-axe", "dolabra-blackbronze-pick", "dolabra-steel-axe", "dolabra-steel-pick" }
        .Select(name => (mod: "dolabra", file: $"dolabra:itemtypes/tool/dolabra/{name}.json")))
    .Append((mod: "soldierspycraftworks", file: "soldierspycraftworks:itemtypes/warpick.json")).ToArray();

foreach (var (mod, file) in targets)
{
    foreach (bool existingTags in new[] { false, true })
    {
        var original = JObject.Parse("""
            { "attributes": { "unrelated": "keep", "immersiveBackpackAttachment": { "attachedTransform": { "scale": 1.32 } } } }
            """);
        if (existingTags) original["tags"] = new JArray("weapon", "weapon-melee");
        Check(file, mod, original);
        var absent = Apply(file, original, []);
        Assert(JToken.DeepEquals(original, absent), $"{file}: optional patches must not run without their mod.");
    }
}

// Every vanilla tool category must agree with its inventory tag too.
int vanillaCount = 0;
foreach (var group in patches.Where(p => ((string?)p["file"])?.StartsWith("game:itemtypes/tool/") == true)
             .GroupBy(p => (string)p["file"]!))
{
    var item = Apply(group.Key, JObject.Parse("{ 'attributes': {}, 'tags': ['existing'] }"), []);
    string? category = (string?)item["attributes"]?["immersiveBackpackAttachment"]?["category"];
    if (category is not ("twohanded" or "handtool")) continue;
    Assert(item["tags"]!.Values<string>().Contains(category), $"{group.Key}: tool category has no matching inventory tag.");
    Assert(item["tags"]!.Values<string>().Contains("existing"), $"{group.Key}: existing tags were removed.");
    string assetPath = Path.Combine(Environment.GetEnvironmentVariable("VINTAGE_STORY") ?? "", "assets", "survival", group.Key[5..]);
    if (!File.Exists(assetPath)) continue;
    var original = JObject.Parse(File.ReadAllText(assetPath));
    var patched = Apply(group.Key, original, []);
    Assert(patched["tags"]?.Values<string>().Contains(category) == true, $"{group.Key}: upstream asset lacks the slot tag after patching.");
    foreach (var tag in original["tags"]?.Values<string>() ?? [])
        Assert(patched["tags"]!.Values<string>().Contains(tag), $"{group.Key}: upstream tag {tag} was removed.");
    vanillaCount++;
}

int upstreamCount = 0;
if (args.Length > 0)
{
    foreach (string archivePath in Directory.GetFiles(args[0], "*.zip"))
    {
        using var archive = ZipFile.OpenRead(archivePath);
        var infoEntry = archive.Entries.FirstOrDefault(e => e.FullName == "modinfo.json");
        if (infoEntry == null) continue;
        using var infoReader = new StreamReader(infoEntry.Open());
        string mod = (string)JObject.Parse(infoReader.ReadToEnd())["modid"]!;
        foreach (var target in targets.Where(t => t.mod == mod))
        {
            string path = "assets/" + target.file.Replace(':', '/');
            var entry = archive.GetEntry(path) ?? throw new Exception($"Missing upstream asset: {path}");
            using var reader = new StreamReader(entry.Open());
            Check(target.file, mod, JObject.Parse(reader.ReadToEnd()));
            upstreamCount++;
        }
    }
    Assert(upstreamCount > 0, "No supported mod assets found in the supplied directory.");
}
Console.WriteLine($"Compatibility asset checks passed: {targets.Length} mod/asset combinations, {vanillaCount} vanilla assets, {upstreamCount} upstream mod assets.");

void Check(string file, string mod, JObject original)
{
    var item = Apply(file, original, [mod]);
    Assert(item["tags"]?.Values<string>().Contains(requiredTag) == true, $"{file} ({mod}): missing {requiredTag} inventory tag.");
    Assert((string?)item["attributes"]?["immersiveBackpackAttachment"]?["category"] == requiredCategory,
        $"{file} ({mod}): missing {requiredCategory} attachment category.");
    foreach (var tag in original["tags"]?.Values<string>() ?? [])
        Assert(item["tags"]!.Values<string>().Contains(tag), $"{file}: existing tag {tag} was removed.");
    foreach (string key in new[] { "unrelated", "attachableToEntity", "handbook" })
        Assert(JToken.DeepEquals(original["attributes"]?[key], item["attributes"]?[key]), $"{file}: unrelated attribute {key} changed.");
    var oldTransform = original["attributes"]?["immersiveBackpackAttachment"]?["attachedTransform"];
    Assert(JToken.DeepEquals(oldTransform, item["attributes"]?["immersiveBackpackAttachment"]?["attachedTransform"]),
        $"{file}: existing attachment metadata was replaced.");
}

JObject Apply(string file, JObject original, string[] mods)
{
    var result = (JObject)original.DeepClone();
    foreach (var patch in patches.Where(p => (string?)p["file"] == file))
    {
        var dependencies = patch["dependsOn"] as JArray;
        if (dependencies?.Any(d => mods.Contains((string)d["modid"]!) == ((bool?)d["invert"] ?? false)) == true) continue;
        Operation operation = (string?)patch["op"] switch
        {
            "add" => new AddReplaceOperation(),
            "addmerge" => new AddMergeOperation(),
            _ => throw new Exception($"Unexpected operation in tool compatibility test: {patch["op"]}")
        };
        operation.Read((JObject)patch.DeepClone());
        new PatchDocument([operation]).ApplyTo(result);
    }
    return result;
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}
