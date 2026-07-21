using July.Resource;
using UnityEngine;

namespace July.Audio
{
    /// <summary>
    /// 音频播放句柄
    /// </summary>
    public class AudioHandle
    {
        public bool IsValid { get; internal set; }
        public AudioClip AudioClip { get; internal set; }
        public string AudioIdentifier { get; internal set; }

        internal ResourceHandle<AudioClip> ResourceHandle { get; set; }
        internal AudioSource AudioSource { get; set; }
        internal int Priority { get; set; }
        internal float BaseVolume { get; set; } = 1f;
        internal float ExpectedEndTime { get; set; }
        internal bool IsPaused { get; set; }

        internal AudioHandle() { IsValid = true; }
    }

    public class BGMPlayOptions
    {
        public bool Loop { get; set; } = true;
        public float? Volume { get; set; }
        public float FadeInDuration { get; set; }
        public float FadeOutDuration { get; set; }
    }

    public class SfxPlayOptions
    {
        public float? Volume { get; set; }
        public int Priority { get; set; } = 128;
        public float Pitch { get; set; } = 1f;
        public float Delay { get; set; }
        public string Group { get; set; }
    }

    public class Sfx3DPlayOptions : SfxPlayOptions
    {
        public AudioPosition3D Position { get; set; }
        public float MinDistance { get; set; } = 1f;
        public float MaxDistance { get; set; } = 500f;
    }

    public struct AudioPosition3D : System.IEquatable<AudioPosition3D>
    {
        public float X;
        public float Y;
        public float Z;

        public static readonly AudioPosition3D Zero = new(0f, 0f, 0f);

        public AudioPosition3D(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public bool Equals(AudioPosition3D other) => X == other.X && Y == other.Y && Z == other.Z;
        public override bool Equals(object obj) => obj is AudioPosition3D o && Equals(o);
        public override int GetHashCode() => (X, Y, Z).GetHashCode();
        public static bool operator ==(AudioPosition3D a, AudioPosition3D b) => a.Equals(b);
        public static bool operator !=(AudioPosition3D a, AudioPosition3D b) => !a.Equals(b);
    }

    internal class AudioPlayOptions
    {
        public float Volume { get; set; } = 1f;
        public bool Loop { get; set; }
        public int Priority { get; set; } = 128;
        public float Pitch { get; set; } = 1f;
        public float Delay { get; set; }
        public float FadeInDuration { get; set; }
        public AudioPosition3D Position { get; set; }
        public float MinDistance { get; set; } = 1f;
        public float MaxDistance { get; set; } = 500f;
    }
}
