using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace July.Rendering.URP.Editor
{
    public static class GameViewCapture
    {
        [MenuItem("JulyGF/渲染/Game 视图截图")]
        public static void Capture()
        {
            string path = SaveScreenshot();
            Debug.Log($"Game 视图截图已保存：{path}");
            EditorUtility.RevealInFinder(path);
        }

        static string SaveScreenshot()
        {
            var source = GetGameViewTexture();
            string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../Library/July/GameViewCapture"));
            string path = Path.Combine(directory, $"{DateTime.Now:yyyyMMdd_HHmmss_fffffff}.png");
            var oldActive = RenderTexture.active;
            bool oldSrgbWrite = GL.sRGBWrite;
            RenderTexture target = null;
            Texture2D image = null;
            try
            {
                target = RenderTexture.GetTemporary(source.width, source.height, 0,
                    RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                GL.sRGBWrite = QualitySettings.activeColorSpace == ColorSpace.Linear;
                // Match GameView's deviceFlippedTargetInView on top-origin graphics APIs.
                bool flip = SystemInfo.graphicsUVStartsAtTop;
                Graphics.Blit(source, target, flip ? new Vector2(1, -1) : Vector2.one,
                    flip ? Vector2.up : Vector2.zero);
                RenderTexture.active = target;
                // GameView displays its background without alpha blending; export the same opaque image.
                image = new Texture2D(source.width, source.height, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
                byte[] png = image.EncodeToPNG();
                Directory.CreateDirectory(directory);
                using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
                    stream.Write(png, 0, png.Length);
                return path;
            }
            finally
            {
                RenderTexture.active = oldActive;
                GL.sRGBWrite = oldSrgbWrite;
                if (image != null) UnityEngine.Object.DestroyImmediate(image);
                if (target != null) RenderTexture.ReleaseTemporary(target);
            }
        }

        static RenderTexture GetGameViewTexture()
        {
            // Unity 2022.3 exposes no public API for reading an already rendered, possibly paused GameView.
            // Keep the version-dependent access here; never advance the game or render a replacement camera.
            var assembly = typeof(EditorWindow).Assembly;
            var playModeView = assembly.GetType("UnityEditor.PlayModeView", true);
            var getMainView = playModeView.GetMethod("GetMainPlayModeView", BindingFlags.Static | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(playModeView.FullName, "GetMainPlayModeView");
            var view = (EditorWindow)getMainView.Invoke(null, null);
            var gameView = assembly.GetType("UnityEditor.GameView", true);
            if (view == null || !gameView.IsInstanceOfType(view))
                throw new InvalidOperationException("请先打开并选中要截图的 Game 视图，等待画面显示后再截图。");
            var textureField = gameView.GetField("m_RenderTexture", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingFieldException(gameView.FullName, "m_RenderTexture");
            var texture = (RenderTexture)textureField.GetValue(view);
            if (texture == null || !texture.IsCreated())
                throw new InvalidOperationException("Game 视图还没有渲染画面，请显示该视图后再截图。");
            return texture;
        }
    }
}
