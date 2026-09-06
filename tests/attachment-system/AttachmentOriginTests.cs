using System;
using System.Collections.Generic;
using System.Linq;
using ImmersiveBackpacks.points;
using ImmersiveModularBackpacks.Attachments;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

internal static class AttachmentOriginTests
{
    public static void Run()
    {
        const string json = """
            { "origin": { "x": 0.2, "y": 0.3, "z": -0.1 },
              "rotation": { "x": 180 }, "scale": 0.5 }
            """;
        var pivoted = Parse(json);
        Assert(pivoted.Origin.SequenceEqual(new[] { 0.2f, 0.3f, -0.1f }), "Explicit origin must be parsed.");
        Equal(new Matrixf().Identity().Values,
            Matrix(Parse("""{ "origin": { "x": 0.2, "y": 0.3, "z": -0.1 } }""")),
            "An origin alone must not change placement.");
        Equal(pivoted.Origin, Point(Matrix(pivoted), pivoted.Origin), "Rotation and scaling must leave their pivot fixed.");
        Equal([0.4f, 0.2f, -0.2f], Point(Matrix(pivoted), [0.6f, 0.5f, 0.1f]),
            "Geometry must rotate and scale around the supplied pivot.");

        var editorJson = JsonObject.FromJson(JObject.FromObject(new ModelTransform
        {
            Origin = new Vec3f(0.2f, 0.3f, -0.1f),
            Rotation = new Vec3f(180, 0, 0),
            Translation = new Vec3f(),
            Scale = 0.5f
        }).ToString());
        Equal(Matrix(pivoted), Matrix(AttachmentTransform.FromModelTransform(editorJson)),
            "The transform editor's PascalCase origin must match asset JSON.");

        foreach (string origin in new[] { "", ", \"origin\": { \"x\": 0, \"y\": 0, \"z\": 0 }" })
        {
            var legacy = Parse("""
                { "rotation": { "x": 25, "y": -35, "z": 15 }, "scale": 0.6,
                  "translation": { "x": 0.35, "y": 0.1, "z": -0.2 }
                """ + origin + "}");
            var expected = new Matrixf().Identity().RotateX(25 * D2R).RotateY(-35 * D2R).RotateZ(15 * D2R)
                .Scale(0.6f, 0.6f, 0.6f).Translate(0.35f, 0.1f, -0.2f).Values;
            Equal(expected, Matrix(legacy), "Omitted/zero origins must retain legacy placement.");
        }

        var parent = new AttachmentTransform
        {
            Origin = [-0.1f, 0.7f, 0.2f], Rotation = [25, -35, 15], Offset = [0.1f, -0.3f, 0.2f], Scale = 1.4f
        };
        pivoted.Offset = [0.3f, -0.1f, 0.2f];
        Equal(Mat4f.Mul(Mat4f.Create(), Matrix(parent), Matrix(pivoted)), Matrix(parent.CombinedWith(pivoted)),
            "Composition must preserve independent parent/child pivots and local offsets.");
        for (int mask = 0; mask < 8; mask++)
        {
            var reflection = new Matrixf().Identity().Scale((mask & 1) == 0 ? 1 : -1,
                (mask & 2) == 0 ? 1 : -1, (mask & 4) == 0 ? 1 : -1).Values;
            var expected = Mat4f.Mul(Mat4f.Create(), reflection,
                Mat4f.Mul(Mat4f.Create(), Matrix(pivoted), reflection));
            Equal(expected, Matrix(pivoted.Mirrored((AttachmentMirror)mask)), "Mirroring must reflect the pivot too.");
        }
        Assert(pivoted.Origin.SequenceEqual(new[] { 0.2f, 0.3f, -0.1f }), "Mirroring must not mutate the source origin.");

        var collapsed = Parse("""{ "origin": { "x": 0.1, "y": 0.4, "z": 0.2 }, "scale": 0 }""");
        Equal(Mat4f.Mul(Mat4f.Create(), Matrix(parent), Matrix(collapsed)), Matrix(parent.CombinedWith(collapsed)),
            "Zero scale must collapse at the transformed pivot, not at zero.");

        CheckRenderPaths(json, parent);
        CheckRenderPaths("""{ "origin": { "x": 0.1, "y": 0.4, "z": 0.2 }, "scale": 0 }""", parent);
        Console.WriteLine("Attachment transform origin checks passed.");
    }

    private static void CheckRenderPaths(string transformJson, AttachmentTransform pointTransform)
    {
        var item = new Item
        {
            Code = new AssetLocation("game:test-tool"),
            Attributes = JsonObject.FromJson("""
                { "immersiveBackpackAttachment": {
                    "origin": [0.09, 0.03, 0.19],
                    "placed": { "rotation": { "y": 20 } },
                    "worn": { "rotation": { "y": 20 } }
                  }, "immersiveAttachedTransform":
                """ + transformJson + "}")
        };
        var child = new ShapeNode(new ItemStack(item));
        var point = new CategoryAttachmentPoint("tool", [], new Cuboidf(), pointTransform,
            new Vec3f(0.4f, 0.6f, 0.2f), AttachmentMirror.Z);
        var host = new ShapeNode(new ItemStack(item), [point], child);
        var shape = new Shape { Elements = [] };
        AttachmentComposer.ComposeChildrenInto(null, shape, host);
        var wrapper = shape.Elements.Single();

        var expected = new Matrixf().Identity().Translate(point.Origin.X, point.Origin.Y, point.Origin.Z)
            .Mul(Matrix(pointTransform))
            .Mul(Matrix(AttachmentTransform.ForItem(item, "placed").Mirrored(point.Mirror)))
            .Translate(-0.09f, -0.03f, -0.19f).Values;
        var worn = new Matrixf().Identity()
            .Translate((float)wrapper.From[0] / 16, (float)wrapper.From[1] / 16, (float)wrapper.From[2] / 16)
            .RotateX((float)wrapper.RotationX * D2R).RotateY((float)wrapper.RotationY * D2R).RotateZ((float)wrapper.RotationZ * D2R)
            .Scale((float)wrapper.ScaleX, (float)wrapper.ScaleY, (float)wrapper.ScaleZ).Values;

        foreach (float[] vertex in new float[][] { [0.1f, 0.2f, 0.3f], [0.7f, 0.9f, 0.8f] })
        {
            var placed = AttachmentComposer.TransformChildBox(point, child,
                new Cuboidf(vertex[0], vertex[1], vertex[2], vertex[0], vertex[1], vertex[2]));
            var shifted = wrapper.Children[0].From;
            float[] local = [vertex[0] + (float)shifted[0] / 16,
                vertex[1] + (float)shifted[1] / 16, vertex[2] + (float)shifted[2] / 16];
            Equal(Point(expected, vertex), [placed.X1, placed.Y1, placed.Z1], "Placed composition must honor transform pivots.");
            Equal(Point(expected, vertex), Point(worn, local), "Worn composition must match placed composition.");
        }
    }

    private const float D2R = MathF.PI / 180;
    private static AttachmentTransform Parse(string json) => AttachmentTransform.FromModelTransform(JsonObject.FromJson(json));
    private static float[] Matrix(AttachmentTransform t) => new Matrixf().Identity()
        .Translate(t.Origin[0], t.Origin[1], t.Origin[2])
        .RotateX(t.Rotation[0] * D2R).RotateY(t.Rotation[1] * D2R).RotateZ(t.Rotation[2] * D2R)
        .Scale(t.Scale, t.Scale, t.Scale)
        .Translate(t.Offset[0] - t.Origin[0], t.Offset[1] - t.Origin[1], t.Offset[2] - t.Origin[2]).Values;
    private static float[] Point(float[] m, float[] p) => [
        m[0] * p[0] + m[4] * p[1] + m[8] * p[2] + m[12],
        m[1] * p[0] + m[5] * p[1] + m[9] * p[2] + m[13],
        m[2] * p[0] + m[6] * p[1] + m[10] * p[2] + m[14]];
    private static void Equal(float[] a, float[] b, string message)
    {
        for (int i = 0; i < a.Length; i++) Assert(MathF.Abs(a[i] - b[i]) < 1e-4f, $"{message} [{i}]: {a[i]} != {b[i]}");
    }
    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class ShapeNode(ItemStack stack, IReadOnlyList<IAttachmentPoint> points = null, IAttachment child = null) : IAttachment
    {
        public ItemStack Stack => stack;
        public ulong RenderKey => 0;
        public IReadOnlyList<IAttachmentPoint> Points => points ?? [];
        public IAttachment GetAttached(string code) => child;
        public void OnAttached(IAttachmentHost host) { }
        public void OnDetached() { }
        public Shape GetShape(ICoreAPI api) => new()
        {
            Elements = [new ShapeElement { Name = "tool", From = [0, 0, 0], To = [16, 16, 16],
                RotationOrigin = [0, 0, 0], FacesResolved = new ShapeElementFace[6] }]
        };
    }
}
