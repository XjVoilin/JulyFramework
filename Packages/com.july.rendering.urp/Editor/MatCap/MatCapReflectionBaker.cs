using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

namespace July.Rendering.URP.Editor
{
    internal enum MatCapLightShape { Rectangle, Ellipse, Soft }

    [Serializable]
    internal struct MatCapLightCard
    {
        public bool hidden;
        public MatCapLightShape shape;
        public Vector2 position;
        public Vector2 size;
        public float angle;
        public float intensity;
        public Color color;
        public float softness;
        public float softRange;

        internal const float CaptureRadius = .97f;
        internal const float MinSoftRange = 3f;
        internal const float MaxSoftRange = 85f;
        internal float SoftRange => softRange > 0 ? softRange : 24f;

        internal static MatCapLightCard New => new MatCapLightCard
        {
            size = new Vector2(.16f, .34f), intensity = .85f, color = Color.white, softness = .045f
        };
    }

    internal static class MatCapReflectionBaker
    {
        internal const int MaxSize = 512;
        static int cachedSize;
        static SphereSample[] cachedSamples;

        readonly struct SphereSample
        {
            internal readonly Vector3 normal, ray;

            internal SphereSample(float x, float y, float z)
            {
                normal = new Vector3(x, y, z);
                ray = Reflect(x, y, z);
            }
        }

        readonly struct Reflector
        {
            readonly Vector3 direction, horizontal, vertical, energy;
            readonly float softness;
            readonly bool ellipse;

            internal Reflector(MatCapLightCard card)
            {
                var center = Vector2.ClampMagnitude(card.position, MatCapLightCard.CaptureRadius);
                float x = center.x, y = center.y;
                float z = Mathf.Sqrt(1 - center.sqrMagnitude);
                direction = Reflect(x, y, z);

                // Align the panel's horizontal axis with the increase in x on the sphere projection.
                var right = new Vector3(z - x * x / z, -x * y / z, -2 * x).normalized;
                var up = Vector3.Cross(direction, right);
                float angle = card.angle * Mathf.Deg2Rad;
                float c = Mathf.Cos(angle), s = Mathf.Sin(angle);
                horizontal = (right * c + up * s) / Mathf.Max(.001f, card.size.x);
                vertical = (up * c - right * s) / Mathf.Max(.001f, card.size.y);
                softness = Mathf.Clamp01(card.softness);
                ellipse = card.shape == MatCapLightShape.Ellipse;
                energy = new Vector3(card.color.r, card.color.g, card.color.b) * card.intensity;
            }

            internal Vector3 Sample(Vector3 ray)
            {
                float forward = Vector3.Dot(ray, direction);
                if (forward <= 0) return Vector3.zero;

                // Intersect the reflected ray from the sphere center with the plane through direction.
                float x = Math.Abs(Vector3.Dot(ray, horizontal));
                if (x >= forward) return Vector3.zero;
                float y = Math.Abs(Vector3.Dot(ray, vertical));
                if (y >= forward) return Vector3.zero;
                float distance = (ellipse ? (float)Math.Sqrt(x * x + y * y) : Math.Max(x, y)) / forward;
                if (distance >= 1) return Vector3.zero;
                if (softness <= 0) return energy;

                float coverage = Math.Min(1, (1 - distance) / softness);
                return energy * (coverage * coverage * (3 - 2 * coverage));
            }
        }

        readonly struct SoftLight
        {
            readonly Vector3 direction, energy;
            readonly float exponent;

            internal SoftLight(MatCapLightCard card)
            {
                var center = Vector2.ClampMagnitude(card.position, MatCapLightCard.CaptureRadius);
                direction = new Vector3(center.x, center.y, Mathf.Sqrt(1 - center.sqrMagnitude));
                energy = new Vector3(card.color.r, card.color.g, card.color.b) * card.intensity;
                float degrees = Mathf.Clamp(card.SoftRange, MatCapLightCard.MinSoftRange, MatCapLightCard.MaxSoftRange);
                // The range is the angle from the highlight center where its strength falls to one half.
                exponent = Mathf.Log(.5f) / Mathf.Log(Mathf.Cos(degrees * Mathf.Deg2Rad));
            }

            internal Vector3 Sample(Vector3 normal) => energy *
                (float)Math.Pow(Math.Max(0, Vector3.Dot(normal, direction)), exponent);
        }

        internal static Texture2D Bake(IReadOnlyList<MatCapLightCard> cards, int size, bool srgb, float fill)
        {
            var plates = cards.Where(card => !card.hidden && card.shape != MatCapLightShape.Soft)
                .Select(card => new Reflector(card)).ToArray();
            var softLights = cards.Where(card => !card.hidden && card.shape == MatCapLightShape.Soft)
                .Select(card => new SoftLight(card)).ToArray();
            float fillIntensity = Mathf.Clamp01(fill);
            var pixels = new byte[size * size * 3];
            var samples = CaptureSamples(size);
            // Workers only read immutable samples and lights. The texture is created on the calling thread.
            Parallel.For(0, size, row =>
            {
                int end = (row + 1) * size;
                for (int pixel = row * size; pixel < end; pixel++)
                {
                    var color = Vector3.zero;
                    for (int j = 0; j < 4; j++)
                    {
                        var sample = samples[pixel * 4 + j];
                        foreach (var plate in plates) color += plate.Sample(sample.ray);
                        foreach (var light in softLights) color += light.Sample(sample.normal);
                        if (fillIntensity > 0) color += Vector3.one * (fillIntensity * sample.normal.z);
                    }
                    int index = pixel * 3;
                    pixels[index] = Channel(color.x * .25f, srgb);
                    pixels[index + 1] = Channel(color.y * .25f, srgb);
                    pixels[index + 2] = Channel(color.z * .25f, srgb);
                }
            });
            return MatCapTextureAssets.MakeRgbTexture(size, srgb, pixels);
        }

        static SphereSample[] CaptureSamples(int size)
        {
            if (cachedSize == size) return cachedSamples;
            var samples = new SphereSample[size * size * 4];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    for (int sample = 0; sample < 4; sample++)
                    {
                        float px = (x + ((sample & 1) == 0 ? .25f : .75f)) * 2 / size - 1;
                        float py = (y + (sample < 2 ? .25f : .75f)) * 2 / size - 1;
                        float depth = 1 - px * px - py * py;
                        if (depth > 0) samples[(y * size + x) * 4 + sample] = new SphereSample(px, py, Mathf.Sqrt(depth));
                    }
                }
            }
            cachedSize = size;
            return cachedSamples = samples;
        }

        static Vector3 Reflect(float x, float y, float z) => new Vector3(2 * x * z, 2 * y * z, 2 * z * z - 1);

        static byte Channel(float linear, bool srgb)
        {
            float value = Math.Min(1, Math.Max(0, linear));
            if (srgb) value = value <= .0031308f ? 12.92f * value : (float)(1.055 * Math.Pow(value, 1.0 / 2.4) - .055);
            return (byte)(255 * value + .5f);
        }
    }
}
