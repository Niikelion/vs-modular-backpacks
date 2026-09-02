using System;
using System.Collections.Generic;
using ImmersiveModularBackpacks.Attachments;
using ImmersiveBackpacks.points;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

static ItemStack Stack(int id, int quantity = 1) => new()
{
    Class = EnumItemClass.Item,
    Id = id,
    StackSize = quantity
};

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static float[] Matrix(AttachmentTransform transform)
{
    const float d2r = MathF.PI / 180f;
    float[] matrix = Mat4f.Create();
    Mat4f.Identity(matrix);
    Mat4f.RotateByXYZ(matrix,
        transform.Rotation[0] * d2r,
        transform.Rotation[1] * d2r,
        transform.Rotation[2] * d2r);
    Mat4f.Scale(matrix, transform.Scale, transform.Scale, transform.Scale);
    Mat4f.Translate(matrix, transform.Offset[0], transform.Offset[1], transform.Offset[2]);
    return matrix;
}

static void AssertEquivalent(float[] expected, float[] actual, string message)
{
    for (int i = 0; i < 16; i++)
        if (MathF.Abs(expected[i] - actual[i]) > 1e-4f)
            throw new InvalidOperationException($"{message} Matrix element {i}: expected {expected[i]}, got {actual[i]}.");
}

SaveCompatibilityTests.Run();

var sparseShape = new Shape
{
    Elements =
    [
        new ShapeElement
        {
            Name = "root",
            FacesResolved = new ShapeElementFace[6]
        }
    ]
};
AttachmentComposer.PrefixShape(sparseShape, "addon-");
Assert(sparseShape.Elements[0].Name == "addon-root",
    "Sparse face arrays must not abort worn-shape composition.");

var sameA = new ItemAttachment(Stack(10, 1));
var sameB = new ItemAttachment(Stack(10, 1));
var differentQuantity = new ItemAttachment(Stack(10, 2));

Assert(sameA.RenderKey == sameB.RenderKey, "Equivalent stacks must produce the same key.");
Assert(sameA.RenderKey != differentQuantity.RenderKey, "Stack quantity must affect the key.");

var left = new CategoryAttachmentPoint("left", [], new Cuboidf());
var right = new CategoryAttachmentPoint("right", [], new Cuboidf());
var childA = new ItemAttachment(Stack(20));
var childB = new ItemAttachment(Stack(21));
var ordered = new TestAttachment(Stack(30), [left, right], childA, childB);
var swapped = new TestAttachment(Stack(30), [left, right], childB, childA);

Assert(ordered.RenderKey != swapped.RenderKey, "Child position must affect the key.");

var customA = new CustomStateAttachment(Stack(40), 1);
var customB = new CustomStateAttachment(Stack(40), 2);
Assert(customA.RenderKey != customB.RenderKey, "Specialized render state must be appendable.");

var transform = AttachmentTransform.FromModelTransform(new JsonObject(JObject.Parse("""
{
  "translation": { "x": 1.25, "y": -2.5, "z": 3.75 },
  "rotation": { "x": 10, "y": 20, "z": 30 },
  "scale": 0.5
}
""")));
Assert(transform.Offset is [1.25f, -2.5f, 3.75f], "ModelTransform translation must map to offset.");
Assert(transform.Rotation is [10f, 20f, 30f], "ModelTransform rotation must be preserved.");
Assert(transform.Scale == 0.5f, "ModelTransform scale must be preserved.");

var partial = AttachmentTransform.FromModelTransform(
    new JsonObject(JObject.Parse("""{ "rotation": { "z": 45 } }""")));
Assert(partial.Offset is [0f, 0f, 0f], "Missing translation must default to zero.");
Assert(partial.Rotation is [0f, 0f, 45f], "Partial rotation must receive defaults.");
Assert(partial.Scale == 1f, "Missing scale must default to one.");

var parentTransform = new AttachmentTransform
{
    Scale = 1.4f,
    Offset = [0.25f, -0.4f, 0.1f],
    Rotation = [25f, -35f, 15f]
};
var localTransform = new AttachmentTransform
{
    Scale = 0.65f,
    Offset = [-0.2f, 0.3f, 0.45f],
    Rotation = [-10f, 20f, 40f]
};
float[] expectedComposition = Mat4f.Mul(Mat4f.Create(), Matrix(parentTransform), Matrix(localTransform));
float[] actualComposition = Matrix(parentTransform.CombinedWith(localTransform));
AssertEquivalent(expectedComposition, actualComposition,
    "CombinedWith must equal sequential parent/local matrix composition.");

var normalLanternPoint = AttachmentTransform.FromRotation([-38f, 0f, 0f]);
var shieldTransform = AttachmentTransform.FromRotation([0f, 0f, 8f]);
AssertEquivalent(
    Mat4f.Mul(Mat4f.Create(), Matrix(normalLanternPoint), Matrix(shieldTransform)),
    Matrix(normalLanternPoint.CombinedWith(shieldTransform)),
    "Placed shield must use sequential affine composition.");

var wornRoot = AttachmentTransform.FromRotation([-90f, 83f, 90f]);
var bedrollTransform = new AttachmentTransform
{
    Scale = 0.6f,
    Offset = [0.35f, 0f, 0.2f],
    Rotation = [0f, -80f, 0f]
};
var wornTopPoint = wornRoot.CombinedWith(AttachmentTransform.FromRotation([0f, -10f, 0f]));
AssertEquivalent(
    Mat4f.Mul(Mat4f.Create(), Matrix(wornRoot), Matrix(shieldTransform)),
    Matrix(wornRoot.CombinedWith(shieldTransform)),
    "Worn shield must use sequential affine composition.");
AssertEquivalent(
    Mat4f.Mul(Mat4f.Create(), Matrix(wornRoot), Matrix(bedrollTransform)),
    Matrix(wornRoot.CombinedWith(bedrollTransform)),
    "Worn front bedroll must use sequential affine composition.");
AssertEquivalent(
    Mat4f.Mul(Mat4f.Create(), Matrix(wornTopPoint), Matrix(bedrollTransform)),
    Matrix(wornTopPoint.CombinedWith(bedrollTransform)),
    "Worn top bedroll must use sequential affine composition.");

var interactionParent = new CategoryAttachmentPoint(
    "strap", [], new Cuboidf(), origin: new Vec3f(0.5f, 0.5f, 0.5f));
var interactionBox = AttachmentComposer.TransformChildBox(
    interactionParent,
    new ItemAttachment(Stack(50)),
    new Cuboidf(0.4f, 0f, 0.4f, 0.6f, 1f, 0.6f));
Assert(MathF.Abs(interactionBox.X1 - 0.4f) < 1e-4f
       && MathF.Abs(interactionBox.Y1 - 0.5f) < 1e-4f
       && MathF.Abs(interactionBox.Z1 - 0.4f) < 1e-4f
       && MathF.Abs(interactionBox.X2 - 0.6f) < 1e-4f
       && MathF.Abs(interactionBox.Y2 - 1.5f) < 1e-4f
       && MathF.Abs(interactionBox.Z2 - 0.6f) < 1e-4f,
    "Nested interaction boxes must use the same child placement transform as rendering.");

Console.WriteLine("Attachment and save-compatibility checks passed.");

sealed class TestAttachment(
    ItemStack stack,
    IReadOnlyList<IAttachmentPoint> points,
    IAttachment first,
    IAttachment second) : AttachmentBase(stack)
{
    public override IReadOnlyList<IAttachmentPoint> Points => points;

    public override IAttachment GetAttached(string pointCode) => pointCode switch
    {
        "left" => first,
        "right" => second,
        _ => null
    };
}

sealed class CustomStateAttachment(ItemStack stack, int state) : AttachmentBase(stack)
{
    public override IReadOnlyList<IAttachmentPoint> Points => [];

    public override IAttachment GetAttached(string pointCode) => null;

    protected override void AppendOwnRenderState(ref AttachmentRenderKeyBuilder key)
    {
        base.AppendOwnRenderState(ref key);
        key.Add(state);
    }
}
