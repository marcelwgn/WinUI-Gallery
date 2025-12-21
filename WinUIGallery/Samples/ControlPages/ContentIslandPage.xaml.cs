// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using Microsoft.UI.Composition;
using Microsoft.UI.Content;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Shapes;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Composition;
using Microsoft.Graphics.DirectX;
using Microsoft.UI.Composition.Scenes;
using System;
using System.Runtime.InteropServices;
using System.Numerics;
using System.Threading.Tasks;

namespace WinUIGallery.ControlPages;

public sealed partial class ContentIslandPage : Page
{
    public ContentIslandPage()
    {
        this.InitializeComponent();
    }

    int idx = 0;

    Rectangle GetNextHostElement()
    {
        if (idx < _rectanglePanel.Children.Count)
        {
            return ((Rectangle)_rectanglePanel.Children[idx++]);
        }

        return null;
    }

    public async void LoadModel()
    {
        ContentIsland parentIsland = this.XamlRoot.ContentIsland;

        Rectangle rect = GetNextHostElement();
        if (rect == null)
        {
            return;
        }

        ContainerVisual placementVisual = (ContainerVisual)ElementCompositionPreview.GetElementVisual(rect);
        Vector2 size = rect.ActualSize;

        ChildSiteLink childSiteLink = ChildSiteLink.Create(parentIsland, placementVisual);

        // We also need to keep the offset of the ChildContentLink within the parent ContentIsland in sync
        // with that of the placementElement for UIA to work correctly.
        var layoutUpdatedEventHandler = new EventHandler<object>((s, e) =>
        {
            // NOTE: Do as little work in here as possible because it gets called for every
            // xaml layout change on this thread!
            var transform = rect.TransformToVisual(null);
            var point = transform.TransformPoint(new Windows.Foundation.Point(0, 0));
            childSiteLink.LocalToParentTransformMatrix = System.Numerics.Matrix4x4.CreateTranslation(
                (float)(point.X),
                (float)(point.Y),
                0);
        });
        rect.LayoutUpdated += layoutUpdatedEventHandler;
        layoutUpdatedEventHandler.Invoke(null, null);

        placementVisual.Size = size;
        childSiteLink.ActualSize = size;

        ContentIsland helmetIsland = CreatePyramidIsland(placementVisual.Compositor);

        childSiteLink.Connect(helmetIsland);
    }

    private void LoadModel_Click(object sender, RoutedEventArgs e)
    {
        _rectanglePanel.Visibility = Visibility.Visible;
        LoadModel();
    }

    public static ContentIsland CreatePyramidIsland(Compositor compositor)
    {
        // Win2D device initialization matches the pattern used by HelmetScenario.
        // (Required to ensure Scene rendering has a graphics device available.)
        _ = CanvasComposition.CreateCompositionGraphicsDevice(compositor, new CanvasDevice());

        //
        // --- Geometry ---
        // Three-sided pyramid: triangular base (y = -1) + apex (y = 1)
        //
        // Build an outline effect by duplicating the pyramid vertices.
        // The first set is used for a black "expanded" backface pass, the second is the actual pyramid.
        var basePositions = new Vector3[]
        {
            new(-1, -1, -1),
            new( 1, -1, -1),
            new( 0, -1,  1),
            new( 0,  1,  0),
        };

        const float outlineScale = 1.0f;

        var positions = new Vector3[basePositions.Length * 2];
        for (int i = 0; i < basePositions.Length; i++)
        {
            positions[i] = basePositions[i] * outlineScale; // outline shell
            positions[i + basePositions.Length] = basePositions[i]; // main mesh
        }

        // Approximate per-vertex normals (smooth shading).
        var baseNormals = new Vector3[]
        {
            Vector3.Normalize(new(-1, 0, -1)),
            Vector3.Normalize(new( 1, 0, -1)),
            Vector3.Normalize(new( 0, 0,  1)),
            Vector3.Normalize(new( 0, 1,  0)),
        };

        var normals = new Vector3[baseNormals.Length * 2];
        for (int i = 0; i < baseNormals.Length; i++)
        {
            normals[i] = baseNormals[i];
            normals[i + baseNormals.Length] = baseNormals[i];
        }

        var baseUvs = new Vector2[]
        {
            new(0, 1),
            new(1, 1),
            new(0.5f, 0),
            new(0.5f, 0.5f),
        };

        var uvs = new Vector2[baseUvs.Length * 2];
        for (int i = 0; i < baseUvs.Length; i++)
        {
            uvs[i] = baseUvs[i];
            uvs[i + baseUvs.Length] = baseUvs[i];
        }

        // Indices are provided as a 16-bit index buffer.
        // Base (winding chosen to face down/up depending on culling; we render double-sided).
        // Sides: (base edge) -> apex
        // Build a single index buffer containing:
        // 1) Outline shell triangles with reversed winding (render backfaces in black)
        // 2) Main mesh triangles with normal winding (render in green)

        // Outline edges for 4-vertex pyramid (line list).
        // Base edges: 0-1, 1-2, 2-0. Side edges: 0-3, 1-3, 2-3.
        var outlineEdgeIndices = new ushort[]
        {
            0, 1,
            1, 2,
            2, 0,
            0, 3,
            1, 3,
            2, 3,
        };

        var fillIndices = new ushort[]
        {
            0, 2, 1,
            0, 1, 3,
            1, 2, 3,
            2, 0, 3,
        };


        //
        // --- Mesh ---
        //
        // Outline mesh uses ONLY the expanded vertex set.
        var outlinePositions = new Vector3[basePositions.Length];
        Array.Copy(positions, 0, outlinePositions, 0, basePositions.Length);
        var outlineNormals = new Vector3[baseNormals.Length];
        Array.Copy(normals, 0, outlineNormals, 0, baseNormals.Length);
        var outlineUvs = new Vector2[baseUvs.Length];
        Array.Copy(uvs, 0, outlineUvs, 0, baseUvs.Length);

        var outlineMesh = SceneMesh.Create(compositor);
        outlineMesh.PrimitiveTopology = DirectXPrimitiveTopology.LineList;
        outlineMesh.FillMeshAttribute(SceneAttributeSemantic.Vertex, DirectXPixelFormat.R32G32B32Float, SceneNodeCommon.CopyToMemoryBuffer(ToBytes(outlinePositions)));
        outlineMesh.FillMeshAttribute(SceneAttributeSemantic.Index, DirectXPixelFormat.R16UInt, SceneNodeCommon.CopyToMemoryBuffer(ToBytes(outlineEdgeIndices)));

        // Fill mesh uses ONLY the unexpanded vertex set.
        var fillPositions = new Vector3[basePositions.Length];
        Array.Copy(positions, basePositions.Length, fillPositions, 0, basePositions.Length);
        var fillNormals = new Vector3[baseNormals.Length];
        Array.Copy(normals, baseNormals.Length, fillNormals, 0, baseNormals.Length);
        var fillUvs = new Vector2[baseUvs.Length];
        Array.Copy(uvs, baseUvs.Length, fillUvs, 0, baseUvs.Length);

        var fillMesh = SceneMesh.Create(compositor);
        fillMesh.PrimitiveTopology = DirectXPrimitiveTopology.TriangleList;
        fillMesh.FillMeshAttribute(SceneAttributeSemantic.Vertex, DirectXPixelFormat.R32G32B32Float, SceneNodeCommon.CopyToMemoryBuffer(ToBytes(fillPositions)));
        fillMesh.FillMeshAttribute(SceneAttributeSemantic.Normal, DirectXPixelFormat.R32G32B32Float, SceneNodeCommon.CopyToMemoryBuffer(ToBytes(fillNormals)));
        fillMesh.FillMeshAttribute(SceneAttributeSemantic.TexCoord0, DirectXPixelFormat.R32G32Float, SceneNodeCommon.CopyToMemoryBuffer(ToBytes(fillUvs)));
        fillMesh.FillMeshAttribute(SceneAttributeSemantic.Index, DirectXPixelFormat.R16UInt, SceneNodeCommon.CopyToMemoryBuffer(ToBytes(fillIndices)));

        //
        // --- Material ---
        //
        //
        // --- Material ---
        // Solid green fill material.
        var material = SceneMetallicRoughnessMaterial.Create(compositor);
        material.BaseColorFactor = new Vector4(0.0f, 0.8f, 0.0f, 1.0f);
        material.EmissiveFactor = new Vector3(0.0f, 0.0f, 0.0f);
        material.RoughnessFactor = 0f;
        material.MetallicFactor = 0.0f;
        material.IsDoubleSided = true;

        // Black outline material.
        var outlineMaterial = SceneMetallicRoughnessMaterial.Create(compositor);
        outlineMaterial.BaseColorFactor = new Vector4(0.0f, 0.0f, 0.0f, 1.0f);
        outlineMaterial.EmissiveFactor = new Vector3(0.0f, 0.0f, 0.0f);
        outlineMaterial.RoughnessFactor = 0f;
        outlineMaterial.MetallicFactor = 0.0f;
        outlineMaterial.IsDoubleSided = true;

        //
        // --- Scene graph ---
        //
        var sceneVisual = SceneVisual.Create(compositor);
        sceneVisual.RelativeOffsetAdjustment = new Vector3(0.5f, 0.5f, 0.0f);

        var worldNode = SceneNode.Create(compositor);
        sceneVisual.Root = worldNode;

        // Render outline shell first (slightly behind), then solid pyramid.
        // Use two separate nodes, each with its own mesh/material, which is supported.

        var outlineNode = SceneNode.Create(compositor);
        outlineNode.Transform.Scale = new Vector3(60);
        worldNode.Children.Add(outlineNode);

        var outlineRenderer = SceneMeshRendererComponent.Create(compositor);
        outlineRenderer.Mesh = outlineMesh;
        outlineRenderer.Material = outlineMaterial;
        outlineNode.Components.Add(outlineRenderer);

        var meshNode = SceneNode.Create(compositor);
        meshNode.Transform.Scale = new Vector3(60);
        worldNode.Children.Add(meshNode);

        var renderer = SceneMeshRendererComponent.Create(compositor);
        renderer.Mesh = fillMesh;
        renderer.Material = material;
        meshNode.Components.Add(renderer);

        //
        // --- Rotation Animation ---
        //
        var rotateAngleAnimation = compositor.CreateScalarKeyFrameAnimation();
        rotateAngleAnimation.InsertKeyFrame(0.0f, 0.0f);
        rotateAngleAnimation.InsertKeyFrame(1.0f, 360.0f);
        rotateAngleAnimation.Duration = TimeSpan.FromSeconds(10);
        rotateAngleAnimation.IterationBehavior = AnimationIterationBehavior.Forever;
        worldNode.Transform.RotationAxis = new Vector3(0, 1, 0);
        worldNode.Transform.StartAnimation("RotationAngleInDegrees", rotateAngleAnimation);

        return ContentIsland.Create(sceneVisual);
    }

    private static byte[] ToBytes(Vector3[] data)
    {
        return MemoryMarshal.AsBytes<Vector3>(data.AsSpan()).ToArray();
    }

    private static byte[] ToBytes(Vector2[] data)
    {
        return MemoryMarshal.AsBytes<Vector2>(data.AsSpan()).ToArray();
    }

    private static byte[] ToBytes(ushort[] data)
    {
        return MemoryMarshal.AsBytes<ushort>(data.AsSpan()).ToArray();
    }

}
