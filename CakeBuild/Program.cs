using System;
using System.IO;
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
    public string BuildConfiguration { get; }
    public string Version { get; }
    public string Name { get; }
    public bool SkipJsonValidation { get; }

    public BuildContext(ICakeContext context)
        : base(context)
    {
        BuildConfiguration = context.Argument("configuration", "Release");
        SkipJsonValidation = context.Argument("skipJsonValidation", false);
        var modInfo = context.DeserializeJsonFromFile<ModInfo>($"../{ProjectName}/modinfo.json");
        Version = modInfo.Version;
        Name = modInfo.ModID;
    }
}

// Regenerates each worn "-attached" bag shape from its held source shape: an exact copy of the geometry with
// the root element repositioned/rotated onto the player's UpperTorso. Lets the held shape be the single source
// of truth - edit it in Blockbench, build, and the worn variant follows. Runs before validation so the
// generated file is checked too. The normal bag is intentionally excluded (its worn shape has a hand tweak).
[TaskName("PortAttachedShapes")]
public sealed class PortAttachedShapesTask : FrostingTask<BuildContext>
{
    private static readonly string[] Variants = { "sturdy" };

    // Held-root -> worn-root offsets (from the original hand port). Size is preserved; children ride the root.
    private static readonly double[] FromDelta = { 0.25, -4.7, -4.7 };
    private static readonly double[] RotationOriginDelta = { 0.0, -4.7, -4.7 };
    private static readonly double[] Rotation = { -90, 82, 90 };
    private const string StepParent = "UpperTorso";

    public override void Run(BuildContext context)
    {
        var dir = $"../{BuildContext.ProjectName}/assets/game/shapes/item/bag";
        foreach (var v in Variants)
        {
            var heldPath = $"{dir}/backpack-{v}.json";
            var attachedPath = $"{dir}/backpack-{v}-attached.json";
            if (!File.Exists(heldPath))
            {
                continue;
            }

            var shape = JObject.Parse(File.ReadAllText(heldPath));
            var root = (JObject)((JArray)shape["elements"])[0];

            var from = ToVec(root["from"]);
            var to = ToVec(root["to"]);
            var rotO = root["rotationOrigin"] != null
                ? ToVec(root["rotationOrigin"])
                : new[] { (from[0] + to[0]) / 2, (from[1] + to[1]) / 2, (from[2] + to[2]) / 2 };

            var newFrom = Add(from, FromDelta);
            var newTo = new[] { newFrom[0] + (to[0] - from[0]), newFrom[1] + (to[1] - from[1]), newFrom[2] + (to[2] - from[2]) };

            root["from"] = Vec(newFrom);
            root["to"] = Vec(newTo);
            root["rotationOrigin"] = Vec(Add(rotO, RotationOriginDelta));
            root["rotationX"] = Rotation[0];
            root["rotationY"] = Rotation[1];
            root["rotationZ"] = Rotation[2];
            root["stepParentName"] = StepParent;

            File.WriteAllText(attachedPath, shape.ToString(Formatting.Indented));
            context.Information($"Ported backpack-{v}.json -> backpack-{v}-attached.json");
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

        var jsonFiles = context.GetFiles($"../{BuildContext.ProjectName}/assets/**/*.json");
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
    }
}

[TaskName("Default")]
[IsDependentOn(typeof(PackageTask))]
public class DefaultTask : FrostingTask
{
}