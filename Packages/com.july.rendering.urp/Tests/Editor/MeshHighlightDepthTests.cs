using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace July.Rendering.URP.Tests
{
    public sealed class MeshHighlightDepthTests
    {
        [Test]
        public void VisibleMaskToleratesSameSurfaceRoundingAndPreservesOcclusion()
        {
            var owned = new List<Object>();
            var previousTarget = RenderTexture.active;
            bool previousAsyncCompilation = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;
            try
            {
                var reference = new Material(AssetDatabase.LoadAssetAtPath<Shader>(
                    "Packages/com.july.rendering.urp/Tests/Editor/Fixtures/DepthReference.shader"));
                var highlight = new Material(AssetDatabase.LoadAssetAtPath<Shader>(
                    "Packages/com.july.rendering.urp/Runtime/Shaders/MeshHighlight.shader"));
                owned.Add(reference); owned.Add(highlight);
                var color = new RenderTexture(new RenderTextureDescriptor(64, 64)
                {
                    graphicsFormat = GraphicsFormat.R8G8_UNorm,
                    depthStencilFormat = GraphicsFormat.None,
                    msaaSamples = 1
                });
                var depth = new RenderTexture(new RenderTextureDescriptor(64, 64)
                {
                    graphicsFormat = GraphicsFormat.None,
                    depthStencilFormat = GraphicsFormat.D24_UNorm_S8_UInt,
                    msaaSamples = 1
                });
                owned.Add(color); owned.Add(depth);
                Assert.IsTrue(color.Create()); Assert.IsTrue(depth.Create());
                var cameraObject = new GameObject("Highlight depth test camera", typeof(Camera));
                owned.Add(cameraObject);
                var camera = cameraObject.GetComponent<Camera>(); camera.enabled = false;
                camera.transform.position = new Vector3(0, 0, -2);
                camera.orthographic = true; camera.orthographicSize = 1; camera.aspect = 1;
                camera.nearClipPlane = .3f; camera.farClipPlane = 1000;
                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad); owned.Add(quad);
                var mesh = quad.GetComponent<MeshFilter>().sharedMesh;
                quad.transform.rotation = Quaternion.FromToRotation(mesh.normals[0], -camera.transform.forward);
                var image = new Texture2D(64, 64, TextureFormat.RGBA32, false, true); owned.Add(image);
                using (var commands = new CommandBuffer())
                {
                    foreach (float depthUnits in new[] { 0f, 1f, 64f })
                    {
                        reference.SetFloat("_DepthDelta", depthUnits / 16777216f);
                        commands.Clear();
                        commands.SetViewProjectionMatrices(camera.worldToCameraMatrix,
                            GL.GetGPUProjectionMatrix(camera.projectionMatrix, false));
                        commands.SetRenderTarget(new RenderTargetIdentifier(color), new RenderTargetIdentifier(depth));
                        commands.SetViewport(new Rect(0, 0, 64, 64));
                        commands.ClearRenderTarget(true, true, Color.clear);
                        commands.DrawMesh(mesh, quad.transform.localToWorldMatrix, reference, 0, 0);
                        commands.DrawMesh(mesh, quad.transform.localToWorldMatrix, highlight, 0, 0);
                        commands.DrawMesh(mesh, quad.transform.localToWorldMatrix, highlight, 0, 1);
                        Graphics.ExecuteCommandBuffer(commands);
                        RenderTexture.active = color;
                        image.ReadPixels(new Rect(0, 0, 64, 64), 0, 0); image.Apply();
                        int silhouette = 0, hidden = 0;
                        foreach (var pixel in image.GetPixels32())
                        {
                            if (pixel.r <= 127) continue;
                            silhouette++;
                            if (pixel.g <= 127) hidden++;
                        }
                        Assert.Greater(silhouette, 100, "The fixture must render a real silhouette.");
                        float hiddenRatio = hidden / (float)silhouette;
                        if (depthUnits <= 1)
                            Assert.Less(hiddenRatio, .01f, "Same-surface rounding must not create solid occluded patches.");
                        else
                            Assert.Greater(hiddenRatio, .3f, "The bias must preserve genuine depth separation.");
                    }
                }
            }
            finally
            {
                RenderTexture.active = previousTarget;
                ShaderUtil.allowAsyncCompilation = previousAsyncCompilation;
                for (int i = owned.Count - 1; i >= 0; i--) Object.DestroyImmediate(owned[i]);
            }
        }
    }
}
