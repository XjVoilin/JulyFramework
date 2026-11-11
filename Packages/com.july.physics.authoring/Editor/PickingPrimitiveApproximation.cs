using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using July.Physics.Authoring.PickingProxy;
using UnityEngine;
using Kind = July.Physics.Authoring.PrimitiveColliderFitter.Kind;
using Shape = July.Physics.Authoring.PrimitiveColliderFitter.Shape;

namespace July.Physics.Authoring
{
    // Primitive-first, one-for-one export. All distances use the source's
    // longest-edge normalization, including the original (pre-budget) fit.
    internal static class PickingPrimitiveApproximation
    {
        const int Pixels = 48;
        internal sealed class Result
        {
            internal Shape[] Shapes;
            internal int Replaced;
            internal double MaximumDeviation;
        }
        sealed class Candidate
        {
            internal Shape Shape;
            internal Geometry Geometry;
            internal double Error;
        }
        readonly struct Witness
        {
            internal readonly Vec3 Start, Direction;
            internal readonly double Depth;
            internal readonly int View;
            internal readonly bool CanChange;
            internal Witness(Vec3 start, Vec3 direction, double depth, int view, bool canChange)
            { Start = start; Direction = direction; Depth = depth; View = view; CanChange = canChange; }
        }

        internal static Result Export(ProxyFit fit, ColliderGenerationSettings settings,
            CancellationToken token, Action<string> progress = null)
        {
            token.ThrowIfCancellationRequested();
            Shape MeshShape(TriangleMesh mesh) => new Shape { Type = Kind.Convex,
                ConvexVertices = mesh.Vertices.Select(p => Float(p*fit.Scale+fit.Center)).ToArray(), ConvexTriangles = mesh.Indices };
            Vec3[] Points(Shape shape) => shape.ConvexVertices.Select(p => (Double(p)-fit.Center)/fit.Scale).ToArray();
            var rawShapes = fit.Meshes.Select(MeshShape).ToArray();
            var shapes = (Shape[])rawShapes.Clone();
            var result = new Result { Shapes = shapes };
            double limit = settings.PickingInwardErrorPercent / 100.0;
            double nativeLimit = settings.NativeShapeErrorPercent / 100.0, clickChange = settings.NativeClickChangePercent / 100.0;
            var selected = new Candidate[shapes.Length];
            var current = new Geometry[shapes.Length]; var originals = new Geometry[shapes.Length];
            bool reportedCompression = false;
            void Compress(int i)
            {
                token.ThrowIfCancellationRequested();
                if (!reportedCompression) { progress?.Invoke("Exporting and cleaning convex meshes"); reportedCompression = true; }
                // Always reduce the original fit. A rejected native proposal is
                // never an intermediate input or an additional error allowance.
                shapes[i] = MeshShape(ConvexVertexBudget.Reduce(fit.Meshes[i],settings.MaxHullVertices,limit,token));
                current[i] = new Geometry(ConvexHull.Build(Points(shapes[i])).Planes);
            }
            if (settings.PickingShapes == PickingShapeMode.PreferPrimitives)
            {
                progress?.Invoke("Approximating native shapes");
                var candidates = new Candidate[shapes.Length][];
                // Search each original piece independently; acceptance below
                // remains sequential because neighbouring pieces affect picking.
                Parallel.For(0,shapes.Length,new ParallelOptions {
                    CancellationToken=token,MaxDegreeOfParallelism=Math.Min(8,Environment.ProcessorCount) },i =>
                {
                    token.ThrowIfCancellationRequested();
                    var hull = ConvexHull.Build(Points(rawShapes[i]));
                    originals[i] = current[i] = new Geometry(hull.Planes);
                    var raw = ConvexHull.Build(fit.Meshes[i].Vertices);
                    candidates[i] = Candidates(raw.Mesh,hull.Planes.Concat(raw.Planes).ToArray(),raw.Mesh.Vertices,fit,nativeLimit,token);
                });
                if (candidates.Any(c => c.Length > 0))
                {
                    var witnesses = Witnesses(rawShapes.SelectMany(Points).ToArray(),originals,
                        candidates.SelectMany(c => c).Select(c => c.Geometry).ToArray(),nativeLimit,token,out var hitsPerView);
                    // A piece/ray intersection stays valid until that piece is
                    // replaced. Reuse it across the other pieces' proposals.
                    var rayDepths = new double[current.Length][];
                    void CacheDepths(int piece)
                    {
                        var depths = rayDepths[piece] ?? (rayDepths[piece] = new double[witnesses.Length]);
                        for (int w = 0; w < witnesses.Length; w++)
                        {
                            if (w%1024 == 0) token.ThrowIfCancellationRequested();
                            depths[w] = current[piece].Raycast(witnesses[w].Start,witnesses[w].Direction);
                        }
                    }
                    Parallel.For(0,current.Length,new ParallelOptions {
                        CancellationToken=token,MaxDegreeOfParallelism=Math.Min(8,Environment.ProcessorCount) },CacheDepths);
                    for (int i = 0; i < shapes.Length; i++)
                    {
                        if (candidates[i].Length == 0) continue;
                        token.ThrowIfCancellationRequested();
                        var others = new double[witnesses.Length];
                        for (int w = 0; w < witnesses.Length; w++)
                        {
                            double nearest = double.PositiveInfinity;
                            for (int piece = 0; piece < current.Length; piece++)
                                if (piece != i) nearest = Math.Min(nearest,rayDepths[piece][w]);
                            others[w] = nearest;
                        }
                        foreach (var candidate in candidates[i])
                        {
                            token.ThrowIfCancellationRequested();
                            if (!PreservesPicking(candidate.Geometry,witnesses,others,hitsPerView,clickChange)) continue;
                            selected[i] = candidate; shapes[i] = candidate.Shape; current[i] = candidate.Geometry;
                            CacheDepths(i); break;
                        }
                    }
                }
            }
            // A successful primitive never needs a compressed convex mesh.
            for (int i = 0; i < shapes.Length; i++) if (selected[i] == null) Compress(i);
            if (selected.Any(c => c != null) && reportedCompression)
            {
                progress?.Invoke("Validating mixed shapes");
                // A later compressed piece must not expose a loss that was
                // hidden by its original shape during primitive selection.
                // Compare native pieces to their raw shapes, holding all final
                // fallback meshes fixed. Each failure removes one native piece.
                while (true)
                {
                    var baseline = current.Select((g,i) => selected[i] == null ? g : originals[i]).ToArray();
                    var points = shapes.Select((s,i) => selected[i] == null ? s : rawShapes[i]).SelectMany(Points).ToArray();
                    var witnesses = Witnesses(points,baseline,selected.Where(c => c != null).Select(c => c.Geometry).ToArray(),nativeLimit,token,out var hitsPerView);
                    int rejected = RejectedNative(current,originals,selected,witnesses,hitsPerView,clickChange,token);
                    if (rejected < 0) break;
                    selected[rejected] = null; Compress(rejected);
                    if (selected.All(c => c == null)) break;
                }
            }
            result.Replaced = selected.Count(c => c != null);
            result.MaximumDeviation = selected.Where(c => c != null).Select(c => c.Error).DefaultIfEmpty(0).Max();
            return result;
        }

        static int RejectedNative(Geometry[] current, Geometry[] originals, Candidate[] selected,
            Witness[] witnesses, int[] hitsPerView, double clickChange, CancellationToken token)
        {
            var missing = new int[hitsPerView.Length];
            for (int i = 0; i < witnesses.Length; i++)
            {
                if (i%1024 == 0) token.ThrowIfCancellationRequested();
                var w = witnesses[i]; double depth = FirstHit(current,w.Start,w.Direction);
                bool lost = ViolatesPicking(w,depth,missing,hitsPerView,clickChange);
                if (!lost) continue;
                for (int n = 0; n < selected.Length; n++)
                    if (selected[n] != null &&
                        ((!double.IsPositiveInfinity(w.Depth) && originals[n].Raycast(w.Start,w.Direction) <= w.Depth+1e-8) ||
                         (!double.IsPositiveInfinity(depth) && current[n].Raycast(w.Start,w.Direction) <= depth+1e-8))) return n;
                throw new InvalidOperationException("混合形状检查的原生命中归属不一致。");
            }
            return -1;
        }

        static Candidate[] Candidates(TriangleMesh mesh, Plane3[] planes, Vec3[] raw, ProxyFit fit,
            double limit, CancellationToken token)
        {
            var best = new Dictionary<(Kind,int), Candidate>(); var points = mesh.Vertices;
            var mean = points.Aggregate(new Vec3(), (sum,p) => sum+p)/points.Length;
            void Consider(Kind kind, Vec3 center, Quaternion rotation, Vec3 half, double radius, double segment)
            {
                token.ThrowIfCancellationRequested();
                // Keep an interior, a balanced and an expanded proposal per kind.
                // The smallest point error can still fail silhouette validation.
                for (int band = 0; band < 3; band++)
                {
                    var candidate = Inscribe(kind, center, rotation, half, radius, segment, planes, fit,limit*band/2);
                    if (candidate == null) continue;
                    double error = Math.Max(0,planes.Max(p => p.At(candidate.Geometry.Center)+candidate.Geometry.Support(p.Normal)));
                    foreach (var p in raw) error = Math.Max(error, candidate.Geometry.Distance(p));
                    if (error > limit+1e-8) continue;
                    candidate.Error = error;
                    var key = (kind,band);
                    if (!best.TryGetValue(key, out var previous) || error < previous.Error) best[key] = candidate;
                }
            }
            var rotations = Frames(mesh);
            foreach (var rotation in rotations)
            {
                var axes = Axes(rotation);
                var low = new Vec3(double.PositiveInfinity,double.PositiveInfinity,double.PositiveInfinity); var high = -low;
                foreach (var p in points)
                { var q = Local(p, axes); low = Vec3.Min(low,q); high = Vec3.Max(high,q); }
                var middle = (low+high)/2;
                var boxCenter = axes[0]*middle.X+axes[1]*middle.Y+axes[2]*middle.Z;
                foreach (var center in new[] { boxCenter, mean })
                {
                    var half = new Vec3();
                    foreach (var p in points)
                    { var q = Local(p-center,axes); half = Vec3.Max(half,new Vec3(Math.Abs(q.X),Math.Abs(q.Y),Math.Abs(q.Z))); }
                    Consider(Kind.Box,center,rotation,half,0,0);
                    // Six fixed lengths plus the tight radial-envelope length;
                    // zero is the sphere. No iterative primitive optimization.
                    for (int a = 0; a < 3; a++)
                    {
                        var axis = axes[a]; var side = axes[(a+1)%3];
                        var capRotation = SixPrimitiveMath.Rotation(Float(side),Float(axis),Float(Vec3.Cross(side,axis)));
                        double span = points.Max(p => Math.Abs(Vec3.Dot(p-center,axis)));
                        double radialSquared = points.Max(p => { var d = p-center; double t = Vec3.Dot(d,axis); return Math.Max(0,d.LengthSquared-t*t); });
                        double fittedSegment = points.Max(p => { var d = p-center; double t = Vec3.Dot(d,axis);
                            return Math.Abs(t)-Math.Sqrt(Math.Max(0,radialSquared-Math.Max(0,d.LengthSquared-t*t))); });
                        foreach (double fraction in new[] { 0, .25, .5, .75, .9, 1.0, Math.Max(0,fittedSegment)/span })
                        {
                            if (fraction == 0 && a != 0) continue;
                            double segment = fraction*span, radius = 0;
                            foreach (var p in points)
                            {
                                var d = p-center; double t = Math.Max(-segment,Math.Min(segment,Vec3.Dot(d,axis)));
                                radius = Math.Max(radius,(d-axis*t).Length);
                            }
                            Consider(fraction == 0 ? Kind.Sphere : Kind.Capsule,center,capRotation,new Vec3(),radius,segment);
                        }
                    }
                }
            }
            return best.Values.OrderBy(c => c.Error).ThenBy(c => c.Shape.Type).ToArray();
        }

        static Candidate Inscribe(Kind kind, Vec3 center, Quaternion rotation, Vec3 half, double radius,
            double segment, Plane3[] planes, ProxyFit fit, double outwardLimit)
        {
            Shape ShapeAt(double factor)
            {
                var shape = new Shape { Type = kind, Center = Float(fit.Center+center*fit.Scale), Rotation = rotation,
                    Size = Float(half*(2*factor*fit.Scale)), Radius = (float)(radius*factor*fit.Scale) };
                shape.Height = Math.Max(2*shape.Radius,(float)(2*(segment+radius)*factor*fit.Scale));
                shape.Volume = kind == Kind.Box ? shape.Size.x*shape.Size.y*shape.Size.z :
                    (float)(Math.PI*shape.Radius*shape.Radius*(kind == Kind.Sphere ? 4.0/3*shape.Radius : shape.Height-2.0/3*shape.Radius));
                return shape;
            }
            var prototype = new Geometry(ShapeAt(1),fit); double scale = 1;
            foreach (var plane in planes)
            {
                double slack = outwardLimit-plane.At(prototype.Center), support = prototype.Support(plane.Normal);
                if (slack <= 0 || support <= 0) return null;
                scale = Math.Min(scale,slack/support);
            }
            // Check the exported float parameters, not just the double proposal.
            // A small inward margin accounts for float quaternion/size evaluation.
            for (int attempt = 0; attempt < 3; attempt++)
            {
                var shape = ShapeAt(scale); var geometry = new Geometry(shape,fit);
                if (planes.All(p => p.At(geometry.Center)+geometry.Support(p.Normal) <= outwardLimit))
                    return new Candidate { Shape = shape, Geometry = geometry };
                scale *= 1-2e-6;
            }
            return null;
        }

        static Quaternion[] Frames(TriangleMesh mesh)
        {
            var pca = Numerics.Pca(mesh.Vertices);
            var result = new List<Quaternion> { Quaternion.identity,
                SixPrimitiveMath.Rotation(Float(pca[0]),Float(pca[1]),Float(pca[2])) };
            var faces = new List<(Vec3 Normal,double Area)>();
            for (int i = 0; i < mesh.Indices.Length; i += 3)
            {
                var normal = Vec3.Cross(mesh.Vertices[mesh.Indices[i+1]]-mesh.Vertices[mesh.Indices[i]],
                    mesh.Vertices[mesh.Indices[i+2]]-mesh.Vertices[mesh.Indices[i]]);
                faces.Add((normal.Normalized,normal.Length));
            }
            var normals = new List<Vec3>();
            foreach (var face in faces.OrderByDescending(f => f.Area))
            {
                if (normals.Any(n => Math.Abs(Vec3.Dot(n,face.Normal)) > .98)) continue;
                normals.Add(face.Normal); if (normals.Count == 6) break;
            }
            for (int i = 0; i < normals.Count; i++) for (int j = i+1; j < normals.Count; j++)
            {
                var x = normals[i]; var y = (normals[j]-x*Vec3.Dot(normals[j],x)).Normalized;
                result.Add(SixPrimitiveMath.Rotation(Float(x),Float(y),Float(Vec3.Cross(x,y))));
            }
            return result.ToArray(); // At most 17 frames; no iterative global fitting.
        }

        static Witness[] Witnesses(Vec3[] points, Geometry[] original, Geometry[] proposals, double boundaryWidth, CancellationToken token, out int[] hitsPerView)
        {
            var directions = Numerics.SphereDirections(24).Concat(new[] { new Vec3(1,0,0),new Vec3(-1,0,0),
                new Vec3(0,1,0),new Vec3(0,-1,0),new Vec3(0,0,1),new Vec3(0,0,-1) }).ToArray();
            var hits = new int[directions.Length]; hitsPerView = hits;
            var views = new Witness[directions.Length][];
            // Views have no shared decisions. Preserve view/pixel order when
            // joining so native selection uses exactly the same witnesses.
            Parallel.For(0,directions.Length,new ParallelOptions {
                CancellationToken=token,MaxDegreeOfParallelism=Math.Min(8,Environment.ProcessorCount) },v =>
            {
                token.ThrowIfCancellationRequested(); var direction = directions[v];
                var result = new List<Witness>(Pixels*Pixels);
                Numerics.ProjectionBasis(direction,out var x,out var y);
                double lx = points.Min(p => Vec3.Dot(p,x)), hx = points.Max(p => Vec3.Dot(p,x));
                double ly = points.Min(p => Vec3.Dot(p,y)), hy = points.Max(p => Vec3.Dot(p,y));
                foreach (var proposal in proposals)
                {
                    double px = Vec3.Dot(proposal.Center,x), py = Vec3.Dot(proposal.Center,y);
                    lx = Math.Min(lx,px-proposal.Support(x)); hx = Math.Max(hx,px+proposal.Support(x));
                    ly = Math.Min(ly,py-proposal.Support(y)); hy = Math.Max(hy,py+proposal.Support(y));
                }
                // Include background around the source: expansion must be checked
                // as well as missed hits, including inside holes and open gaps.
                lx -= boundaryWidth; hx += boundaryWidth; ly -= boundaryWidth; hy += boundaryWidth;
                double dx = (hx-lx)/Pixels, dy = (hy-ly)/Pixels;
                var starts = new Vec3[Pixels*Pixels]; var depths = new double[starts.Length];
                for (int i = 0; i < Pixels; i++) for (int j = 0; j < Pixels; j++)
                {
                    var start = x*(lx+(i+.5)*(hx-lx)/Pixels)+y*(ly+(j+.5)*(hy-ly)/Pixels)-2*direction;
                    int index = i*Pixels+j; starts[index] = start; depths[index] = FirstHit(original,start,direction);
                }
                var edges = new List<(double X,double Y,bool Vertical)>();
                bool Hit(int i,int j) => i >= 0 && j >= 0 && i < Pixels && j < Pixels && !double.IsPositiveInfinity(depths[i*Pixels+j]);
                for (int i = 0; i <= Pixels; i++) for (int j = 0; j < Pixels; j++)
                    if (Hit(i-1,j) != Hit(i,j)) edges.Add((i*dx,j*dy,true));
                for (int i = 0; i < Pixels; i++) for (int j = 0; j <= Pixels; j++)
                    if (Hit(i,j-1) != Hit(i,j)) edges.Add((i*dx,j*dy,false));
                for (int i = 0; i < Pixels; i++) for (int j = 0; j < Pixels; j++)
                {
                    int index = i*Pixels+j; double depth = depths[index];
                    double px = (i+.5)*dx, py = (j+.5)*dy;
                    bool canChange = false;
                    foreach (var edge in edges)
                    {
                        double ex = edge.Vertical ? edge.X : Math.Max(edge.X,Math.Min(edge.X+dx,px));
                        double ey = edge.Vertical ? Math.Max(edge.Y,Math.Min(edge.Y+dy,py)) : edge.Y;
                        if ((px-ex)*(px-ex)+(py-ey)*(py-ey) <= boundaryWidth*boundaryWidth+1e-16) { canChange = true; break; }
                    }
                    result.Add(new Witness(starts[index],direction,depth,v,canChange));
                    if (!double.IsPositiveInfinity(depth)) hits[v]++;
                }
                views[v] = result.ToArray();
            });
            return views.SelectMany(view => view).ToArray();
        }
        static bool PreservesPicking(Geometry candidate, Witness[] witnesses, double[] others, int[] hitsPerView, double clickChange)
        {
            var missing = new int[hitsPerView.Length];
            for (int i = 0; i < witnesses.Length; i++)
            {
                var w = witnesses[i]; double depth = Math.Min(others[i],candidate.Raycast(w.Start,w.Direction));
                if (ViolatesPicking(w,depth,missing,hitsPerView,clickChange)) return false;
            }
            return true;
        }
        static bool ViolatesPicking(Witness w, double depth, int[] changed, int[] hitsPerView, double clickChange)
        {
            bool hadHit = !double.IsPositiveInfinity(w.Depth), hasHit = !double.IsPositiveInfinity(depth);
            if (hadHit != hasHit)
                return !w.CanChange || ++changed[w.View] > hitsPerView[w.View]*clickChange;
            // Shape-space deviation, rather than ray-depth difference, controls
            // fit. Grazing rays can change depth arbitrarily for a small spatial
            // change; imposing both would prevent the requested coarse fit.
            return false;
        }
        static double FirstHit(Geometry[] group, Vec3 start, Vec3 direction, int excluded = -1)
        {
            double hit = double.PositiveInfinity;
            for (int i = 0; i < group.Length; i++) if (i != excluded) hit = Math.Min(hit,group[i].Raycast(start,direction));
            return hit;
        }

        // Analytic native shapes and convex half-spaces share normalized queries.
        // These are authoring checks; gameplay continues to use Unity colliders.
        sealed class Geometry
        {
            readonly Kind kind;
            readonly Plane3[] planes;
            readonly Vec3[] axes;
            readonly Vec3 half;
            readonly double radius, segment;
            internal readonly Vec3 Center;
            internal Geometry(Plane3[] planes) { kind = Kind.Convex; this.planes = planes; }
            internal Geometry(Shape shape, ProxyFit fit)
            {
                kind = shape.Type; Center = (Double(shape.Center)-fit.Center)/fit.Scale; axes = Axes(shape.Rotation);
                half = Double(shape.Size)/(2*fit.Scale); radius = shape.Radius/fit.Scale;
                segment = Math.Max(0,shape.Height/(2*fit.Scale)-radius);
            }
            internal double Support(Vec3 normal) => kind == Kind.Box ? Math.Abs(Vec3.Dot(normal,axes[0]))*half.X+
                Math.Abs(Vec3.Dot(normal,axes[1]))*half.Y+Math.Abs(Vec3.Dot(normal,axes[2]))*half.Z :
                radius*normal.Length+(kind == Kind.Capsule ? segment*Math.Abs(Vec3.Dot(normal,axes[1])) : 0);
            internal double Distance(Vec3 point)
            {
                var q = Local(point-Center,axes);
                if (kind == Kind.Box)
                    return new Vec3(Math.Max(0,Math.Abs(q.X)-half.X),Math.Max(0,Math.Abs(q.Y)-half.Y),Math.Max(0,Math.Abs(q.Z)-half.Z)).Length;
                if (kind == Kind.Capsule) q -= new Vec3(0,Math.Max(-segment,Math.Min(segment,q.Y)),0);
                return Math.Max(0,q.Length-radius);
            }
            internal double Raycast(Vec3 start, Vec3 direction)
            {
                if (kind == Kind.Convex)
                    return Numerics.SegmentInterval(start,direction,4,planes,out double entry,out _) ? entry : double.PositiveInfinity;
                var p = Local(start-Center,axes); var d = Local(direction,axes);
                if (kind == Kind.Box)
                {
                    double lo = 0, hi = 4;
                    for (int a = 0; a < 3; a++) if (!Slab(p[a],d[a],half[a],ref lo,ref hi)) return double.PositiveInfinity;
                    return lo;
                }
                if (kind == Kind.Sphere) return Ball(p,d,radius);
                double first = Math.Min(Ball(p-new Vec3(0,segment,0),d,radius),Ball(p+new Vec3(0,segment,0),d,radius));
                double a2 = d.X*d.X+d.Z*d.Z, b = p.X*d.X+p.Z*d.Z, c = p.X*p.X+p.Z*p.Z-radius*radius;
                double low = 0, high = 4;
                if (a2 < 1e-15) { if (c > 0) return first; }
                else
                {
                    double disc = b*b-a2*c; if (disc < 0) return first;
                    double root = Math.Sqrt(disc); low = Math.Max(0,(-b-root)/a2); high = Math.Min(4,(-b+root)/a2);
                }
                if (low <= high && Slab(p.Y,d.Y,segment,ref low,ref high)) first = Math.Min(first,low);
                return first;
            }
            static bool Slab(double p, double d, double halfSize, ref double lo, ref double hi)
            {
                if (Math.Abs(d) < 1e-14) return Math.Abs(p) <= halfSize;
                double a = (-halfSize-p)/d, b = (halfSize-p)/d;
                lo = Math.Max(lo,Math.Min(a,b)); hi = Math.Min(hi,Math.Max(a,b)); return lo <= hi;
            }
            static double Ball(Vec3 p, Vec3 d, double r)
            {
                double a = d.LengthSquared, b = Vec3.Dot(p,d), c = p.LengthSquared-r*r, disc = b*b-a*c;
                if (disc < 0) return double.PositiveInfinity;
                double root = Math.Sqrt(disc), enter = (-b-root)/a, exit = (-b+root)/a;
                return exit >= 0 && enter <= 4 ? Math.Max(0,enter) : double.PositiveInfinity;
            }
        }
        static Vec3[] Axes(Quaternion rotation) => new[] { Double(rotation*Vector3.right).Normalized,
            Double(rotation*Vector3.up).Normalized,Double(rotation*Vector3.forward).Normalized };
        static Vec3 Local(Vec3 p, Vec3[] axes) => new Vec3(Vec3.Dot(p,axes[0]),Vec3.Dot(p,axes[1]),Vec3.Dot(p,axes[2]));
        static Vec3 Double(Vector3 p) => new Vec3(p.x,p.y,p.z);
        static Vector3 Float(Vec3 p) => new Vector3((float)p.X,(float)p.Y,(float)p.Z);
    }
}
