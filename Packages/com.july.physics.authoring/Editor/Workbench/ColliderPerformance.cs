using System;
using System.Collections.Generic;
using System.Linq;

namespace July.Physics.Authoring
{
    internal enum ColliderPerformanceLevel { None, Reminder, Attention, RuleMismatch }
    internal enum ColliderPerformanceFilter { All, Reminder, Attention, RuleMismatch, AllConvex }

    // Authoring triage, not a device-time estimate. Reports do not change apply
    // eligibility or review decisions; source mesh counts are not cooked PhysX data.
    internal sealed class ColliderPerformanceReport
    {
        internal readonly bool Exists, AllConvex;
        internal readonly ColliderPerformanceLevel Level;
        internal readonly long MaxHullVertices;
        internal readonly string[] Reasons;
        internal ColliderPerformanceReport(bool exists, bool allConvex, ColliderPerformanceLevel level, long maxVertices, string[] reasons)
        { Exists=exists;AllConvex=allConvex;Level=level;MaxHullVertices=maxVertices;Reasons=reasons; }
        internal string Label => !Exists ? "" : Level switch {
            ColliderPerformanceLevel.Reminder => "性能提醒", ColliderPerformanceLevel.Attention => "重点检查",
            ColliderPerformanceLevel.RuleMismatch => "规则不符", _ => "未触发提醒" };
        internal string Summary => Label + (AllConvex ? " · 全凸包" : "");
        internal bool Matches(ColliderPerformanceFilter filter) => filter == ColliderPerformanceFilter.All || Exists && (filter switch {
            ColliderPerformanceFilter.Reminder => Level >= ColliderPerformanceLevel.Reminder,
            ColliderPerformanceFilter.Attention => Level >= ColliderPerformanceLevel.Attention,
            ColliderPerformanceFilter.RuleMismatch => Level == ColliderPerformanceLevel.RuleMismatch,
            ColliderPerformanceFilter.AllConvex => AllConvex, _ => false });
        internal string Details => string.Join("\n",Reasons) + (AllConvex ? "\n全部使用凸网格碰撞体；此属性本身不代表性能差。" : "") +
            "\n项目筛查阈值，非平台性能上限；按源网格统计，需结合启用实例数与真机耗时。";
    }

    internal static class ColliderPerformance
    {
        internal static ColliderPerformanceReport Assess(ColliderGeometryGoal goal, ColliderCounts counts,
            ColliderGeometryCounts geometry, long maxHullVertices, ColliderPerformanceSettings settings)
        {
            var reasons=new List<string>();var level=ColliderPerformanceLevel.None;
            void Add(ColliderPerformanceLevel value,string reason) { if(value>level)level=value;reasons.Add(reason); }
            if(counts.Total>0)
            {
                int normal=goal==ColliderGeometryGoal.Envelope?settings.EnvelopeReminder:settings.StructureReminder;
                int high=goal==ColliderGeometryGoal.Envelope?settings.EnvelopeAttention:settings.StructureAttention;
                if(settings.Enabled && counts.Total>normal)Add(counts.Total>high?ColliderPerformanceLevel.Attention:ColliderPerformanceLevel.Reminder,$"{(goal==ColliderGeometryGoal.Envelope?"整体包围":"结构贴合")}碰撞体 {counts.Total} 个，超过提醒阈值 {normal}");
                if(goal==ColliderGeometryGoal.Envelope && counts.Convex+counts.Outside>0)
                    Add(ColliderPerformanceLevel.RuleMismatch,"整体包围含非原生碰撞体，新生成仅使用盒、球、胶囊");
                if(goal==ColliderGeometryGoal.Structure)
                {
                    if(settings.Enabled && counts.Convex>=settings.ConvexReminder)Add(counts.Convex>=settings.ConvexAttention?ColliderPerformanceLevel.Attention:ColliderPerformanceLevel.Reminder,$"凸包 {counts.Convex} 个，达到提醒阈值 {settings.ConvexReminder}，检查是否还有可接受的近似");
                    if(counts.Outside>0)Add(ColliderPerformanceLevel.RuleMismatch,"含非凸网格或其它类型，检查碰撞用途与配置");
                    if(maxHullVertices>PickingProxy.GenerationSettings.HullVertexLimit)
                        Add(ColliderPerformanceLevel.RuleMismatch,$"单凸包最多 {maxHullVertices} 顶点，超过项目 {PickingProxy.GenerationSettings.HullVertexLimit} 顶点上限");
                    else if(settings.Enabled && maxHullVertices>settings.HullVerticesReminder)
                        Add(maxHullVertices>settings.HullVerticesAttention?ColliderPerformanceLevel.Attention:ColliderPerformanceLevel.Reminder,$"单凸包最多 {maxHullVertices} 顶点，超过提醒阈值 {settings.HullVerticesReminder}");
                    if(settings.Enabled && geometry.Vertices>settings.TotalVerticesReminder)Add(geometry.Vertices>settings.TotalVerticesAttention?ColliderPerformanceLevel.Attention:ColliderPerformanceLevel.Reminder,$"结构贴合碰撞网格合计 {geometry.Vertices} 顶点，超过提醒阈值 {settings.TotalVerticesReminder}，重复实例会累计成本");
                }
                if(geometry.MissingMeshes>0)Add(ColliderPerformanceLevel.RuleMismatch,$"缺少 {geometry.MissingMeshes} 个碰撞网格");
            }
            return new ColliderPerformanceReport(counts.Total>0,counts.Total>0&&counts.Convex==counts.Total,level,maxHullVertices,reasons.ToArray());
        }
        internal static ColliderPerformanceReport Current(ColliderRow row,int index)
        {
            var current=row.Inspection.Current[index];
            long max=current.Shapes.OfType<UnityEngine.MeshCollider>().Where(c=>c.convex&&c.sharedMesh!=null).Select(c=>(long)c.sharedMesh.vertexCount).DefaultIfEmpty(0).Max();
            return Assess(row.Profile.Targets[index].Settings.Goal,ColliderStatistics.Current(row,index),current.Geometry,max,row.Profile.Performance);
        }
        internal static ColliderPerformanceReport Candidate(ColliderGeneration.Preview preview,ColliderPerformanceSettings settings) => preview==null
            ? Assess(ColliderGeometryGoal.Structure,default,default,0,settings)
            : Assess(preview.Goal,ColliderStatistics.Candidate(preview),ColliderGeometryCounts.Candidate(preview),
                preview.Fit.Shapes.Where(s=>s.ConvexVertices!=null).Select(s=>(long)s.ConvexVertices.Length).DefaultIfEmpty(0).Max(),settings);
    }
}
