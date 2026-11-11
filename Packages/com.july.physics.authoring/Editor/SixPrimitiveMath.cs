using System;
using UnityEngine;

namespace July.Physics.Authoring
{
    internal static class SixPrimitiveMath
    {
        internal enum PrimitiveKind { Box, Sphere, Capsule, Cylinder, Frustum, Prism }

        internal sealed class Primitive
        {
            internal PrimitiveKind Kind;
            internal Vector3 Center, HalfSize;
            internal Quaternion Rotation = Quaternion.identity;
            // Height is the cylinder segment length for capsules; full height for cylinders/frustums.
            internal float Radius, Height, Bottom, Top;

            internal double Volume
            {
                get
                {
                    switch (Kind)
                    {
                        case PrimitiveKind.Box: return 8.0 * HalfSize.x * HalfSize.y * HalfSize.z;
                        case PrimitiveKind.Sphere: return 4.0 / 3 * Math.PI * Radius * Radius * Radius;
                        case PrimitiveKind.Capsule: return Math.PI * Radius * Radius * (Height + 4.0 / 3 * Radius);
                        case PrimitiveKind.Cylinder: return Math.PI * Radius * Radius * Height;
                        case PrimitiveKind.Frustum: return Math.PI * Height / 3 * (Top * Top + Top * Bottom + Bottom * Bottom);
                        case PrimitiveKind.Prism: return 4.0 * HalfSize.x * HalfSize.y * (Top + Bottom);
                        default: throw new ArgumentOutOfRangeException();
                    }
                }
            }

        }

        internal static Quaternion Rotation(Vector3 x, Vector3 y, Vector3 z)
        {
            float a = x.x, b = y.y, c = z.z;
            Quaternion q;
            if (a + b + c > 0)
            { float s = Mathf.Sqrt(1 + a + b + c) * 2; q = new Quaternion((y.z - z.y) / s, (z.x - x.z) / s, (x.y - y.x) / s, s * .25f); }
            else if (a > b && a > c)
            { float s = Mathf.Sqrt(1 + a - b - c) * 2; q = new Quaternion(s * .25f, (y.x + x.y) / s, (z.x + x.z) / s, (y.z - z.y) / s); }
            else if (b > c)
            { float s = Mathf.Sqrt(1 + b - a - c) * 2; q = new Quaternion((y.x + x.y) / s, s * .25f, (z.y + y.z) / s, (z.x - x.z) / s); }
            else
            { float s = Mathf.Sqrt(1 + c - a - b) * 2; q = new Quaternion((z.x + x.z) / s, (z.y + y.z) / s, s * .25f, (x.y - y.x) / s); }
            float norm = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            return new Quaternion(q.x / norm, q.y / norm, q.z / norm, q.w / norm);
        }
    }
}
