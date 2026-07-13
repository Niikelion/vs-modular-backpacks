using System;
using System.IO;
using System.Text.RegularExpressions;
using Cake.Common;
using Cake.Common.Diagnostics;
using Cake.Common.IO;
using Cake.Common.Tools.DotNet;
using Cake.Common.Tools.DotNet.Clean;
using Cake.Common.Tools.DotNet.Publish;
using Cake.Core;
using Cake.Frosting;
using Cake.Json;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;

namespace CakeBuild;

public static class Program
{
    public static int Main(string[] args)
    {
        return new CakeHost()
            .UseContext<BuildContext>()
            .Run(args);
    }
}

public class BuildContext : FrostingContext
{
    public const string ProjectName = "ImmersiveBackpacks";

    // Content-only companion mod: hard-depends on Equus, so its patches are applied after Equus's own and can
    // override them. Shipped as a separate zip, since a soft "load after" doesn't exist - see EquusProjectName.
    public const string EquusProjectName = "ImmersiveBackpacksEquus";
    public const string EquusModId = "modularbackpacksequus";

    public string BuildConfiguration { get; }
    public string Version { get; }
    public string Name { get; }
    public string EquusVersion { get; }
    public bool SkipJsonValidation { get; }

    public BuildContext(ICakeContext context)
        : base(context)
    {
        BuildConfiguration = context.Argument("configuration", "Release");
        SkipJsonValidation = context.Argument("skipJsonValidation", false);
        var modInfo = context.DeserializeJsonFromFile<ModInfo>($"../{ProjectName}/modinfo.json");
        Version = modInfo.Version;
        Name = modInfo.ModID;
        EquusVersion = context.DeserializeJsonFromFile<ModInfo>($"../{EquusProjectName}/modinfo.json").Version;
    }
}

// Regenerates every entity-attached bag shape from its held source shape: an exact copy of the geometry whose
// root element is rigid-translated, rotated and step-parented onto a host element. Lets the held shape be the
// single source of truth - edit it in Blockbench, build, and every attached variant follows. Runs before
// validation so the generated files are checked too.
//
// Two hosts today: the player ("-attached", step-parented to UpperTorso) and an Equus horse ("-ferus", to Hind,
// matching the placement of Equus's own backpack-{type} shape). The normal bag has no generated worn shape - its
// "-attached" was hand-tweaked - but it does get a ferus one. The ferus shapes belong to the Equus compat mod,
// so they only ship to players who have Equus.
[TaskName("PortAttachedShapes")]
public sealed class PortAttachedShapesTask : FrostingTask<BuildContext>
{
    // Root offsets are rigid: from/to and rotationOrigin all shift by RootDelta, size is preserved, children
    // ride along. A shape leaving the game domain must have its texture paths qualified, since unprefixed ones
    // would otherwise resolve against the domain the shape now lives in.
    private sealed record Port(
        string Variant, double[] RootDelta, double[] Rotation, string StepParent,
        string OutDir, string OutSuffix, bool QualifyTextures);

    private static readonly string GameBags =
        $"../{BuildContext.ProjectName}/assets/game/shapes/item/bag";

    private static readonly string EquusBags =
        $"../{BuildContext.EquusProjectName}/assets/{BuildContext.EquusModId}/shapes/item/bag";

    private static readonly Port[] Ports =
    [
        new("sturdy", [0.1776, -4.572, -4.8957], [-90, 83, 90], "UpperTorso", GameBags, "-attached", false),
        new("normal", [2.0, 11.2, -3.0], [-90, 0, 90], "Hind", EquusBags, "-ferus", true),
        new("sturdy", [2.1, 11.0, -3.0], [-90, 0, 90], "Hind", EquusBags, "-ferus", true),
    ];

    public override void Run(BuildContext context)
    {
        foreach (var port in Ports)
        {
            var heldPath = $"{GameBags}/backpack-{port.Variant}.json";
            var outPath = $"{port.OutDir}/backpack-{port.Variant}{port.OutSuffix}.json";
            if (!File.Exists(heldPath))
            {
                continue;
            }

            Directory.CreateDirectory(port.OutDir);

            var shape = JObject.Parse(File.ReadAllText(heldPath));
            var root = (JObject)((JArray)shape["elements"])[0];

            var from = ToVec(root["from"]);
            var to = ToVec(root["to"]);
            var rotO = root["rotationOrigin"] != null
                ? ToVec(root["rotationOrigin"])
                : new[] { (from[0] + to[0]) / 2, (from[1] + to[1]) / 2, (from[2] + to[2]) / 2 };

            var newFrom = Add(from, port.RootDelta);
            var newTo = new[] { newFrom[0] + (to[0] - from[0]), newFrom[1] + (to[1] - from[1]), newFrom[2] + (to[2] - from[2]) };

            root["from"] = Vec(newFrom);
            root["to"] = Vec(newTo);
            root["rotationOrigin"] = Vec(Add(rotO, port.RootDelta));
            root["rotationX"] = port.Rotation[0];
            root["rotationY"] = port.Rotation[1];
            root["rotationZ"] = port.Rotation[2];
            root["stepParentName"] = port.StepParent;

            if (port.QualifyTextures)
            {
                QualifyTexturesWithGameDomain(shape);
            }

            File.WriteAllText(outPath, shape.ToString(Formatting.Indented));
            context.Information($"Ported backpack-{port.Variant}.json -> {Path.GetFileName(outPath)}");
        }
    }

    private static void QualifyTexturesWithGameDomain(JObject shape)
    {
        if (shape["textures"] is not JObject textures)
        {
            return;
        }

        foreach (var tex in textures.Properties())
        {
            var path = (string)tex.Value;
            if (path != null && !path.Contains(':'))
            {
                tex.Value = $"game:{path}";
            }
        }
    }

    private static double[] ToVec(JToken t) => new[] { (double)t[0], (double)t[1], (double)t[2] };
    private static double[] Add(double[] a, double[] b) => new[] { a[0] + b[0], a[1] + b[1], a[2] + b[2] };
    // Round away binary-float noise (e.g. 4.5 + -4.7 = -0.20000000000000018) so the generated JSON stays clean.
    private static JArray Vec(double[] v) => new(Math.Round(v[0], 4), Math.Round(v[1], 4), Math.Round(v[2], 4));
}

[TaskName("ValidateJson")]
[IsDependentOn(typeof(PortAttachedShapesTask))]
public sealed class ValidateJsonTask : FrostingTask<BuildContext>
{
    public override void Run(BuildContext context)
    {
        if (context.SkipJsonValidation)
        {
            return;
        }

        var jsonFiles = context.GetFiles($"../{BuildContext.ProjectName}/assets/**/*.json")
            + context.GetFiles($"../{BuildContext.EquusProjectName}/assets/**/*.json");
        foreach (var file in jsonFiles)
        {
            try
            {
                var json = File.ReadAllText(file.FullPath);
                JToken.Parse(json);
            }
            catch (JsonException ex)
            {
                throw new Exception(
                    $"Validation failed for JSON file: {file.FullPath}{Environment.NewLine}{ex.Message}", ex);
            }
        }
    }
}

[TaskName("Build")]
[IsDependentOn(typeof(ValidateJsonTask))]
public sealed class BuildTask : FrostingTask<BuildContext>
{
    public override void Run(BuildContext context)
    {
        context.DotNetClean($"../{BuildContext.ProjectName}/{BuildContext.ProjectName}.csproj",
            new DotNetCleanSettings
            {
                Configuration = context.BuildConfiguration
            });


        context.DotNetPublish($"../{BuildContext.ProjectName}/{BuildContext.ProjectName}.csproj",
            new DotNetPublishSettings
            {
                Configuration = context.BuildConfiguration
            });
    }
}

[TaskName("Package")]
[IsDependentOn(typeof(BuildTask))]
public sealed class PackageTask : FrostingTask<BuildContext>
{
    public override void Run(BuildContext context)
    {
        context.EnsureDirectoryExists("../Releases");
        context.CleanDirectory("../Releases");
        context.EnsureDirectoryExists($"../Releases/{context.Name}");
        context.CopyFiles($"../{BuildContext.ProjectName}/bin/{context.BuildConfiguration}/Mods/mod/publish/*",
            $"../Releases/{context.Name}");
        if (context.DirectoryExists($"../{BuildContext.ProjectName}/assets"))
        {
            context.CopyDirectory($"../{BuildContext.ProjectName}/assets", $"../Releases/{context.Name}/assets");
        }

        context.CopyFile($"../{BuildContext.ProjectName}/modinfo.json", $"../Releases/{context.Name}/modinfo.json");
        if (context.FileExists($"../{BuildContext.ProjectName}/modicon.png"))
        {
            context.CopyFile($"../{BuildContext.ProjectName}/modicon.png", $"../Releases/{context.Name}/modicon.png");
        }

        context.Zip($"../Releases/{context.Name}", $"../Releases/{context.Name}_{context.Version}.zip");
        PackageEquusCompat(context);
    }

    // The Equus compat mod is content-only: no build, just its assets + modinfo zipped into its own release.
    private static void PackageEquusCompat(BuildContext context)
    {
        var staging = $"../Releases/{BuildContext.EquusModId}";
        context.EnsureDirectoryExists(staging);
        context.CopyDirectory($"../{BuildContext.EquusProjectName}/assets", $"{staging}/assets");
        context.CopyFile($"../{BuildContext.EquusProjectName}/modinfo.json", $"{staging}/modinfo.json");
        context.Zip(staging, $"../Releases/{BuildContext.EquusModId}_{context.EquusVersion}.zip");
    }
}

// Emits a paste-ready ModDB description from mod-description.html: strips every HTML comment (the ModDB
// editor mangles them) and syncs the version pill to modinfo.json so it can't drift. Standalone task -
// run with: ./build.ps1 --target=ModDescription
[TaskName("ModDescription")]
public sealed class ModDescriptionTask : FrostingTask<BuildContext>
{
    private const string Source = "../mod-description.html";
    private const string Output = "../mod-description.moddb.html";

    public override void Run(BuildContext context)
    {
        if (!File.Exists(Source))
        {
            throw new Exception($"Description source not found: {Path.GetFullPath(Source)}");
        }

        var html = File.ReadAllText(Source);
        html = Regex.Replace(html, "<!--.*?-->", "", RegexOptions.Singleline);   // drop all HTML comments
        html = Regex.Replace(html, @"<strong>v[0-9.]+</strong>", $"<strong>v{context.Version}</strong>"); // sync version pill
        html = Regex.Replace(html, @"(\r?\n){3,}", "\n\n").Trim() + "\n";        // collapse the gaps comments left behind

        File.WriteAllText(Output, html);
        context.Information($"Wrote {Path.GetFullPath(Output)} (v{context.Version}) - paste into the ModDB HTML editor.");
    }
}

[TaskName("Default")]
[IsDependentOn(typeof(PackageTask))]
public class DefaultTask : FrostingTask
{
}