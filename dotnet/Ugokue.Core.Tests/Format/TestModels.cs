using System.Collections.Generic;
using Ugokue.Core.Format;

namespace Ugokue.Core.Tests.Format
{
    /// <summary>
    /// テストで使う、すべての要素を含む小さなモデルを作る。
    /// </summary>
    internal static class TestModels
    {
        internal static readonly byte[] HeadPng = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x01, 0x02, 0xFF };

        internal static readonly byte[] EyePng = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x10, 0x20, 0x0D, 0x0A };

        /// <summary>
        /// パーツ、メッシュ、ビュー、対応づけ、変形、物理、トラッキングを持つモデルを作る。
        /// </summary>
        internal static Model Build()
        {
            return new Model
            {
                Canvas = new Canvas { Width = 512, Height = 768 },
                Parameters = new List<Parameter>
                {
                    new Parameter { Id = "AngleX", Min = -30, Max = 30, Default = 0 },
                    new Parameter { Id = "EyeOpenL", Min = 0, Max = 1, Default = 0.85 },
                    new Parameter { Id = "HairFront", Min = -1, Max = 1, Default = 0 },
                },
                Parts = new List<Part>
                {
                    new Part { Id = "head", Role = "head", Parent = null, Anchor = new[] { 256.5, 300.25 }, DrawOrder = 10, Mesh = "head", ClipBy = new List<string>() },
                    new Part { Id = "eye_l", Role = "eye_white", Parent = "head", Anchor = new[] { 200.125, 280.0 }, DrawOrder = 20, Mesh = "eye_l", ClipBy = new List<string> { "head" } },
                    new Part { Id = "face_group", Role = "face", Parent = "head", Anchor = new[] { 256.0, 290.0 }, DrawOrder = 15, Mesh = null, ClipBy = new List<string>() },
                },
                Meshes = new List<Mesh>
                {
                    new Mesh
                    {
                        Id = "head",
                        Part = "head",
                        Texture = "textures/head.png",
                        Vertices = new[] { new[] { 100.5, 100.25 }, new[] { 400.75, 100.0 }, new[] { 250.0, 500.125 } },
                        Uvs = new[] { new[] { 0.0, 0.0 }, new[] { 1.0, 0.0 }, new[] { 0.5, 1.0 } },
                        Triangles = new[] { new[] { 0, 1, 2 } },
                    },
                    new Mesh
                    {
                        Id = "eye_l",
                        Part = "eye_l",
                        Texture = "textures/eyes/eye_l.png",
                        Vertices = new[] { new[] { 190.0, 270.0 }, new[] { 210.0, 270.0 }, new[] { 210.0, 290.0 }, new[] { 190.0, 290.0 } },
                        Uvs = new[] { new[] { 0.1, 0.1 }, new[] { 0.9, 0.1 }, new[] { 0.9, 0.9 }, new[] { 0.1, 0.9 } },
                        Triangles = new[] { new[] { 0, 1, 2 }, new[] { 0, 2, 3 } },
                    },
                    new Mesh
                    {
                        Id = "eye_l_side",
                        Part = "eye_l",
                        Texture = "textures/eyes/eye_l.png",
                        Vertices = new[] { new[] { 195.0, 270.0 }, new[] { 208.0, 271.5 }, new[] { 200.0, 289.0 } },
                        Uvs = new[] { new[] { 0.0, 0.0 }, new[] { 1.0, 0.0 }, new[] { 0.5, 1.0 } },
                        Triangles = new[] { new[] { 0, 1, 2 } },
                    },
                },
                Views = new List<View>
                {
                    new View { Id = "front", Yaw = 0, Pitch = 0, Overrides = new List<ViewOverride>() },
                    new View
                    {
                        Id = "right",
                        Yaw = 22.5,
                        Pitch = -7.25,
                        Overrides = new List<ViewOverride>
                        {
                            new ViewOverride { Part = "eye_l", Mesh = "eye_l_side", DrawOrder = 25 },
                            new ViewOverride { Part = "face_group", Visible = false },
                        },
                    },
                },
                Correspondence = new Correspondence
                {
                    Landmarks = new List<Landmark>
                    {
                        new Landmark { Id = "eye_l_outer", Part = "eye_l", View = "front", Position = new[] { 190.5, 280.75 }, Confidence = 0.93, Source = "auto" },
                        new Landmark { Id = "eye_l_outer", Part = "eye_l", View = "right", Position = new[] { 196.0, 281.0 }, Confidence = 1, Source = "manual" },
                    },
                    VertexMatches = new List<VertexMatch>
                    {
                        new VertexMatch { Mesh = "head", Vertex = 2, View = "right", Position = new[] { 262.3, 498.9 }, Confidence = 0.4, Source = "auto" },
                    },
                },
                Deformation = new Deformation
                {
                    KeyformGroups = new List<KeyformGroup>
                    {
                        new KeyformGroup
                        {
                            Id = "head_angle_x",
                            Mesh = "head",
                            Parameters = new List<string> { "AngleX", "EyeOpenL" },
                            Keys = new[] { new[] { -30.0, 0.0, 30.0 }, new[] { 0.0, 1.0 } },
                            Interpolation = "linear",
                            Keyforms = BuildKeyforms(),
                        },
                    },
                    Procedural = new List<ProceduralMotion>
                    {
                        new ProceduralMotion { Type = "rotate", Parameter = "AngleX", Part = "head", DegreesPerUnit = 0.333 },
                        new ProceduralMotion { Type = "translate", Parameter = "AngleX", Part = "eye_l", OffsetPerUnit = new[] { 0.125, -1e-7 } },
                    },
                },
                Physics = new Physics
                {
                    Pendulums = new List<Pendulum>
                    {
                        new Pendulum
                        {
                            Id = "hair_front",
                            Inputs = new List<PendulumInput>
                            {
                                new PendulumInput { Parameter = "AngleX", Weight = 0.6 },
                                new PendulumInput { Parameter = "EyeOpenL", Weight = 0.4 },
                            },
                            Output = "HairFront",
                            Stiffness = 0.35,
                            Damping = 0.1,
                            Scale = 1.5,
                        },
                    },
                },
                Tracking = new Tracking
                {
                    Parameters = new List<TrackingParameter>
                    {
                        new TrackingParameter { Parameter = "AngleX", Smoothing = 0.25 },
                    },
                },
            };
        }

        /// <summary>
        /// <see cref="Build"/> のモデルが参照するテクスチャを持つパッケージを作る。
        /// </summary>
        internal static ModelPackage BuildPackage()
        {
            var package = new ModelPackage(Build());
            package.Textures["textures/head.png"] = HeadPng;
            package.Textures["textures/eyes/eye_l.png"] = EyePng;
            return package;
        }

        private static List<Keyform> BuildKeyforms()
        {
            var keyforms = new List<Keyform>();
            foreach (double angle in new[] { -30.0, 0.0, 30.0 })
            {
                foreach (double eye in new[] { 0.0, 1.0 })
                {
                    keyforms.Add(new Keyform
                    {
                        At = new[] { angle, eye },
                        Offsets = new[] { new[] { angle / 7.0, eye * 0.5 }, new[] { 0.0, 0.0 }, new[] { -angle * 0.1, 2.75 } },
                    });
                }
            }

            return keyforms;
        }
    }
}
