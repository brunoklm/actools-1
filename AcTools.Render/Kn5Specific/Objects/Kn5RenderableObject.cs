﻿using System;
using System.Collections.Generic;
using System.Linq;
using AcTools.Kn5File;
using AcTools.Render.Base;
using AcTools.Render.Base.Cameras;
using AcTools.Render.Base.Materials;
using AcTools.Render.Base.Objects;
using AcTools.Render.Base.Structs;
using AcTools.Render.Base.Utils;
using AcTools.Render.Kn5Specific.Materials;
using AcTools.Utils.Helpers;
using JetBrains.Annotations;
using SlimDX;

namespace AcTools.Render.Kn5Specific.Objects {
    public interface IKn5RenderableObject : IRenderableObject {
        [NotNull]
        Kn5Node OriginalNode { get; }

        void SetMirrorMode(IDeviceContextHolder holder, bool enabled);

        void SetDebugMode(IDeviceContextHolder holder, bool enabled);

        void SetEmissive(Vector3? color);
    }

    public sealed class Kn5RenderableObject : TrianglesRenderableObject<InputLayouts.VerticePNTG>, IKn5RenderableObject {
        public static bool FlipByX = true;

        public readonly bool IsCastingShadows;
        
        public Kn5Node OriginalNode { get; }

        private static InputLayouts.VerticePNTG[] Convert(Kn5Node.Vertice[] vertices) {
            var size = vertices.Length;
            var result = new InputLayouts.VerticePNTG[size];

            if (FlipByX) {
                for (var i = 0; i < size; i++) {
                    var x = vertices[i];
                    result[i] = new InputLayouts.VerticePNTG(
                            x.Co.ToVector3FixX(),
                            x.Normal.ToVector3FixX(),
                            x.Uv.ToVector2(),
                            x.Tangent.ToVector3Tangent());
                }
            } else {
                for (var i = 0; i < size; i++) {
                    var x = vertices[i];
                    result[i] = new InputLayouts.VerticePNTG(
                            x.Co.ToVector3(),
                            x.Normal.ToVector3(),
                            x.Uv.ToVector2(),
                            x.Tangent.ToVector3());
                }
            }

            return result;
        }

        private static ushort[] Convert(ushort[] indices) {
            return FlipByX ? indices.ToIndicesFixX() : indices;
        }

        public Kn5RenderableObject(Kn5Node node) : base(node.Name, Convert(node.Vertices), Convert(node.Indices)) {
            OriginalNode = node;
            IsCastingShadows = node.CastShadows;

            if (IsEnabled && (!OriginalNode.Active || !OriginalNode.IsVisible || !OriginalNode.IsRenderable)) {
                IsEnabled = false;
            }

            if (OriginalNode.IsTransparent) {
                IsReflectable = false;
            }
        }

        private IRenderableMaterial Material => _debugMaterial ?? _mirrorMaterial ?? _material;

        [CanBeNull]
        private IRenderableMaterial _mirrorMaterial;
        private bool _mirrorMaterialInitialized;

        public void SetMirrorMode(IDeviceContextHolder holder, bool enabled) {
            if (enabled == (_mirrorMaterial != null)) return;

            if (enabled) {
                _mirrorMaterial = holder.GetMaterial(BasicMaterials.MirrorKey);
                if (IsInitialized) {
                    _mirrorMaterial.Initialize(holder);
                    _mirrorMaterialInitialized = true;
                }
            } else {
                _mirrorMaterialInitialized = false;
                DisposeHelper.Dispose(ref _mirrorMaterial);
            }
        }

        [CanBeNull]
        private IRenderableMaterial _debugMaterial;
        private bool _debugMaterialInitialized;

        public void SetDebugMode(IDeviceContextHolder holder, bool enabled) {
            if (enabled == (_debugMaterial != null)) return;

            if (enabled) {
                _debugMaterial = holder.Get<SharedMaterials>().GetMaterial(new Tuple<object, uint>(BasicMaterials.DebugKey, OriginalNode.MaterialId));
                if (_debugMaterial == null) return;

                if (IsInitialized) {
                    _debugMaterial.Initialize(holder);
                    _debugMaterialInitialized = true;
                }
            } else {
                _debugMaterialInitialized = false;
                DisposeHelper.Dispose(ref _debugMaterial);
            }
        }

        public Vector3? Emissive { get; set; }

        public void SetEmissive(Vector3? color) {
            Emissive = color;
        }

        private bool _isTransparent;
        private IRenderableMaterial _material;

        protected override void Initialize(IDeviceContextHolder contextHolder) {
            base.Initialize(contextHolder);

            _material = contextHolder.Get<SharedMaterials>().GetMaterial(OriginalNode.MaterialId);
            _material.Initialize(contextHolder);
            _isTransparent = OriginalNode.IsTransparent && _material.IsBlending;

            if (_mirrorMaterial != null && !_mirrorMaterialInitialized) {
                _mirrorMaterialInitialized = true;
                _mirrorMaterial.Initialize(contextHolder);
            }

            if (_debugMaterial != null && !_debugMaterialInitialized) {
                _debugMaterialInitialized = true;
                _debugMaterial.Initialize(contextHolder);
            }
        }

        protected override void DrawOverride(IDeviceContextHolder contextHolder, ICamera camera, SpecialRenderMode mode) {
            if (_isTransparent &&
                    mode != SpecialRenderMode.Outline &&
                    mode != SpecialRenderMode.SimpleTransparent &&
                    mode != SpecialRenderMode.DeferredTransparentForw &&
                    mode != SpecialRenderMode.DeferredTransparentDef &&
                    mode != SpecialRenderMode.DeferredTransparentMask) return;

            if (mode == SpecialRenderMode.Shadow && !IsCastingShadows) return;

            var material = Material;
            if (!material.Prepare(contextHolder, mode)) return;

            base.DrawOverride(contextHolder, camera, mode);

            if (Emissive.HasValue) {
                (material as IEmissiveMaterial)?.SetEmissiveNext(Emissive.Value);
            }

            material.SetMatrices(ParentMatrix, camera);
            material.Draw(contextHolder, Indices.Length, mode);
        }

        public override BaseRenderableObject Clone() {
            return new ClonedKn5RenderableObject(this);
        }

        public override void Dispose() {
            DisposeHelper.Dispose(ref _material);
            DisposeHelper.Dispose(ref _mirrorMaterial);
            DisposeHelper.Dispose(ref _debugMaterial);
            base.Dispose();
        }

        internal class ClonedKn5RenderableObject : TrianglesRenderableObject<InputLayouts.VerticePNTG> {
            private readonly Kn5RenderableObject _original;

            internal ClonedKn5RenderableObject(Kn5RenderableObject original) : base(original.Name + "_copy", original.Vertices, original.Indices) {
                _original = original;
            }

            public override bool IsEnabled => _original.IsEnabled;

            public override bool IsReflectable => _original.IsReflectable;

            protected override void DrawOverride(IDeviceContextHolder contextHolder, ICamera camera, SpecialRenderMode mode) {
                if (_original._isTransparent &&
                        mode != SpecialRenderMode.Outline &&
                        mode != SpecialRenderMode.SimpleTransparent &&
                        mode != SpecialRenderMode.DeferredTransparentForw &&
                        mode != SpecialRenderMode.DeferredTransparentDef &&
                        mode != SpecialRenderMode.DeferredTransparentMask) return;

                if (mode == SpecialRenderMode.Shadow && !_original.IsCastingShadows || _original._material == null) return;

                var material = _original.Material;
                if (!material.Prepare(contextHolder, mode)) return;

                base.DrawOverride(contextHolder, camera, mode);

                if (_original.Emissive.HasValue) {
                    (material as IEmissiveMaterial)?.SetEmissiveNext(_original.Emissive.Value);
                }

                material.SetMatrices(ParentMatrix, camera);
                material.Draw(contextHolder, Indices.Length, mode);
            }

            public override BaseRenderableObject Clone() {
                return new ClonedKn5RenderableObject(_original);
            }
        }
    }
}
