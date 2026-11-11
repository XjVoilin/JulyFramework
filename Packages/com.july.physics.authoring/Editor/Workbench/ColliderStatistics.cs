using System.Linq;
using UnityEngine;

namespace July.Physics.Authoring
{
    // Display-only component counts. Mesh geometry is reported separately.
    internal readonly struct ColliderCounts
    {
        internal readonly int Total, Native, Convex, Outside;
        internal ColliderCounts(int total, int native, int convex, int outside = 0)
        { Total = total; Native = native; Convex = convex; Outside = outside; }

        public static ColliderCounts operator +(ColliderCounts a, ColliderCounts b) =>
            new ColliderCounts(a.Total + b.Total, a.Native + b.Native, a.Convex + b.Convex, a.Outside + b.Outside);

        internal string Text => $"{Total} 原{Native} 凸{Convex}";
        internal string Note => Outside > 0 ? $"其它{Outside}" : "";
    }

    internal static class ColliderStatistics
    {
        internal const string GeometryNote = "按实例累计，不合并共用Mesh；三角面包含所有三角形子网格，不含线/点。碰撞网格顶点／三角面仅统计MeshCollider或候选凸网格；盒、球、胶囊没有固定网格面数，不计入。凸包顶点／三角面计数是源网格，不是PhysX烹制后数据。";

        internal static string SelectedGeometry(ColliderRow[] rows, int[] indices)
        {
            var models = default(ColliderGeometryCounts);
            var current = default(ColliderGeometryCounts);
            var candidate = default(ColliderGeometryCounts);
            int readable = 0, previews = 0, expired = 0;
            foreach (var row in rows)
            {
                if (row.Inspection.Ready) { models += row.Inspection.ModelGeometry; readable++; }
                foreach (int index in indices)
                {
                    if (row.Inspection.Ready) current += row.Inspection.Current[index].Geometry;
                    var preview = row.Candidates[index].Preview;
                    if (preview == null) continue;
                    candidate += ColliderGeometryCounts.Candidate(preview); previews++;
                    if (!row.IsCurrent(index)) expired++;
                }
            }
            int expected = rows.Length * indices.Length;
            string partial = readable == rows.Length ? "" : $"（已读取{readable}/{rows.Length}项）";
            string coverage = previews == expected ? "" : $"（{previews}/{expected}组）";
            string stale = expired == 0 ? "" : $"，含过期{expired}组";
            return $"原模型合计{partial}：{models.Text}\n" +
                $"所选输出组碰撞网格 · 当前{partial}：{current.Text}\n" +
                $"候选{coverage}：{(previews == 0 ? "未生成" : candidate.Text)}{stale}";
        }
        internal static ColliderCounts Current(ColliderRow row, int index)
        {
            var current = row.Inspection.Current[index];
            int convex = current.Shapes.OfType<MeshCollider>().Count(c => c.convex);
            return new ColliderCounts(current.Count, current.NativeCount, convex,
                current.Count - current.NativeCount - convex);
        }

        internal static ColliderCounts Candidate(ColliderGeneration.Preview preview) =>
            new ColliderCounts(preview.Count, preview.NativeCount, preview.Count - preview.NativeCount);

        internal static string Delta(int value) => value > 0 ? "+" + value : value < 0 ? "−" + -(long)value : "0";

        internal static string CandidateText(ColliderCounts current, ColliderCounts candidate, bool currentReadable) =>
            $"{candidate.Total}({Difference(candidate.Total, current.Total, currentReadable)}) " +
            $"原{candidate.Native}({Difference(candidate.Native, current.Native, currentReadable)}) " +
            $"凸{candidate.Convex}({Difference(candidate.Convex, current.Convex, currentReadable)})";

        // Used separately so an unreadable current result is never treated as zero.
        static string Difference(int value, int previous, bool readable) => readable ? Delta(value - previous) : "—";

        internal static string Change(ColliderCounts current, ColliderCounts candidate) =>
            $"总{current.Total}→{candidate.Total} ({Delta(candidate.Total - current.Total)})  " +
            $"原生{current.Native}→{candidate.Native} ({Delta(candidate.Native - current.Native)})  " +
            $"凸包{current.Convex}→{candidate.Convex} ({Delta(candidate.Convex - current.Convex)})";
    }
}
