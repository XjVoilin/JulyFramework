#nullable enable
namespace July.Physics.Authoring.PickingProxy
{
    // Geometry-only stage data; policy grids are released once training is built.
    internal sealed class ProxyAnalysis
    {
        internal readonly Vec3 Center;
        internal readonly double Scale;
        internal readonly TriangleMesh Hull;
        internal readonly Plane3[] HullPlanes;
        internal readonly TrainingData Training;
        internal ProxyAnalysis(Vec3 center,double scale,TriangleMesh hull,Plane3[] planes,TrainingData training)
        {Center=center;Scale=scale;Hull=hull;HullPlanes=planes;Training=training;}
    }
    internal sealed class ProxyFit
    {
        internal readonly Vec3 Center;
        internal readonly double Scale;
        internal readonly TriangleMesh[] Meshes;
        internal ProxyFit(Vec3 center,double scale,TriangleMesh[] meshes)
        {Center=center;Scale=scale;Meshes=meshes;}
    }
}
