#nullable enable
using System;
using System.Threading;

namespace July.Physics.Authoring.PickingProxy
{
    // Geometry is normalized and the BVH is read-only. The owner supplies the
    // accelerator; the numerical core does not own a device or Editor loop.
    public sealed class VisibilityGeometry
    {
        public float[] Nodes {get;}
        public float[] Triangles {get;}
        internal VisibilityGeometry(MeshQueries query){query.GpuArrays(out var nodes,out var triangles);Nodes=nodes;Triangles=triangles;}
    }
    public sealed class VisibilityMask
    {
        public uint[] Words {get;}
        public int WordsPerPoint {get;}
        public int PointCount {get;}
        public int DirectionCount {get;}
        public VisibilityMask(uint[] words,int points,int directions)
        {
            WordsPerPoint=(directions+31)/32;
            if(words.Length!=points*WordsPerPoint*2)throw new ArgumentException("Invalid visibility mask dimensions.");
            Words=words;PointCount=points;DirectionCount=directions;
        }
        internal bool Visible(int point,int direction)=> (Words[(point*WordsPerPoint+direction/32)*2]&(1u<<(direction%32)))!=0;
        internal bool Uncertain(int point,int direction)=> (Words[(point*WordsPerPoint+direction/32)*2+1]&(1u<<(direction%32)))!=0;
    }
    public interface IVisibilityBackend
    {
        VisibilityMask Compute(VisibilityGeometry geometry,Vec3[] origins,Vec3[] directions,CancellationToken cancellationToken,Action<string>? progress);
    }
}
