using System.Linq;
using NUnit.Framework;

namespace July.Physics.Authoring
{
    internal sealed class ColliderPerformanceTests
    {
        [Test] public void CustomThresholdsUseTheirDocumentedBoundaries()
        {
            var settings=new ColliderPerformanceSettings { EnvelopeReminder=1,EnvelopeAttention=2,
                StructureReminder=1,StructureAttention=2,ConvexReminder=2,ConvexAttention=3,
                HullVerticesReminder=8,HullVerticesAttention=12,TotalVerticesReminder=10,TotalVerticesAttention=20 };
            foreach(var goal in new[]{ColliderGeometryGoal.Envelope,ColliderGeometryGoal.Structure})
            {
                Assert.That(ColliderPerformance.Assess(goal,new ColliderCounts(1,1,0),default,0,settings).Level,Is.EqualTo(ColliderPerformanceLevel.None));
                Assert.That(ColliderPerformance.Assess(goal,new ColliderCounts(2,2,0),default,0,settings).Level,Is.EqualTo(ColliderPerformanceLevel.Reminder));
                Assert.That(ColliderPerformance.Assess(goal,new ColliderCounts(3,3,0),default,0,settings).Level,Is.EqualTo(ColliderPerformanceLevel.Attention));
            }
            settings.StructureReminder=20;settings.StructureAttention=30;
            Assert.That(ColliderPerformance.Assess(ColliderGeometryGoal.Structure,new ColliderCounts(2,0,2),default,0,settings).Level,Is.EqualTo(ColliderPerformanceLevel.Reminder));
            Assert.That(ColliderPerformance.Assess(ColliderGeometryGoal.Structure,new ColliderCounts(3,0,3),default,0,settings).Level,Is.EqualTo(ColliderPerformanceLevel.Attention));
            Assert.That(ColliderPerformance.Assess(ColliderGeometryGoal.Structure,new ColliderCounts(1,0,1),default,12,settings).Level,Is.EqualTo(ColliderPerformanceLevel.Reminder));
            Assert.That(ColliderPerformance.Assess(ColliderGeometryGoal.Structure,new ColliderCounts(1,0,1),default,13,settings).Level,Is.EqualTo(ColliderPerformanceLevel.Attention));
            Assert.That(ColliderPerformance.Assess(ColliderGeometryGoal.Structure,new ColliderCounts(1,0,1),new ColliderGeometryCounts(20,36),0,settings).Level,Is.EqualTo(ColliderPerformanceLevel.Reminder));
            Assert.That(ColliderPerformance.Assess(ColliderGeometryGoal.Structure,new ColliderCounts(1,0,1),new ColliderGeometryCounts(21,38),0,settings).Level,Is.EqualTo(ColliderPerformanceLevel.Attention));
        }
        [Test] public void DisablingSoftRemindersKeepsRulesAndAllConvexAttribute()
        {
            var settings=new ColliderPerformanceSettings { Enabled=false };
            var report=ColliderPerformance.Assess(ColliderGeometryGoal.Structure,new ColliderCounts(20,0,20),new ColliderGeometryCounts(400,720),24,settings);
            Assert.That(report.Level,Is.EqualTo(ColliderPerformanceLevel.None));Assert.That(report.AllConvex);
            Assert.That(ColliderPerformance.Assess(ColliderGeometryGoal.Structure,new ColliderCounts(1,0,1),default,25,settings).Level,Is.EqualTo(ColliderPerformanceLevel.RuleMismatch));
            Assert.That(ColliderPerformance.Assess(ColliderGeometryGoal.Envelope,new ColliderCounts(1,0,1),default,8,settings).Level,Is.EqualTo(ColliderPerformanceLevel.RuleMismatch));
            Assert.That(ColliderPerformance.Assess(ColliderGeometryGoal.Structure,new ColliderCounts(1,0,1),new ColliderGeometryCounts(0,0,1),0,settings).Level,Is.EqualTo(ColliderPerformanceLevel.RuleMismatch));
        }
        [TestCase(3,ColliderPerformanceLevel.None)]
        [TestCase(4,ColliderPerformanceLevel.Reminder)]
        [TestCase(6,ColliderPerformanceLevel.Reminder)]
        [TestCase(7,ColliderPerformanceLevel.Attention)]
        public void EnvelopeCountsHaveSeparateThresholds(int count,ColliderPerformanceLevel expected)
        {
            var report=ColliderPerformance.Assess(ColliderGeometryGoal.Envelope,new ColliderCounts(count,count,0),default,0,new ColliderPerformanceSettings());
            Assert.That(report.Level,Is.EqualTo(expected));Assert.That(report.AllConvex,Is.False);
        }
        [Test] public void SmallAllConvexIsAnAttributeRatherThanAWarning()
        {
            var report=ColliderPerformance.Assess(ColliderGeometryGoal.Structure,new ColliderCounts(1,0,1),new ColliderGeometryCounts(8,12),8,new ColliderPerformanceSettings());
            Assert.That(report.Level,Is.EqualTo(ColliderPerformanceLevel.None));Assert.That(report.AllConvex);
            Assert.That(report.Matches(ColliderPerformanceFilter.AllConvex));Assert.That(report.Matches(ColliderPerformanceFilter.Reminder),Is.False);
        }
        [TestCase(4,16,64,ColliderPerformanceLevel.Reminder)]
        [TestCase(8,16,128,ColliderPerformanceLevel.Attention)]
        [TestCase(1,24,24,ColliderPerformanceLevel.Reminder)]
        [TestCase(1,25,25,ColliderPerformanceLevel.RuleMismatch)]
        [TestCase(3,16,129,ColliderPerformanceLevel.Attention)]
        public void StructureCombinesCountAndGeometryWithoutDoubleReportingTriangles(int count,int max,long total,ColliderPerformanceLevel level)
        {
            var report=ColliderPerformance.Assess(ColliderGeometryGoal.Structure,new ColliderCounts(count,0,count),new ColliderGeometryCounts(total,total*2-4*count),max,new ColliderPerformanceSettings());
            Assert.That(report.Level,Is.EqualTo(level));Assert.That(report.Reasons.All(r=>!r.Contains("三角")));
            Assert.That(report.Matches(ColliderPerformanceFilter.Attention),Is.EqualTo(level>=ColliderPerformanceLevel.Attention));
        }
        [Test] public void LegacyEnvelopeMeshesAndMissingMeshesAreConfigurationReminders()
        {
            var legacy=ColliderPerformance.Assess(ColliderGeometryGoal.Envelope,new ColliderCounts(2,0,2),new ColliderGeometryCounts(16,24),8,new ColliderPerformanceSettings());
            Assert.That(legacy.Level,Is.EqualTo(ColliderPerformanceLevel.RuleMismatch));Assert.That(legacy.Matches(ColliderPerformanceFilter.RuleMismatch));
            var missing=ColliderPerformance.Assess(ColliderGeometryGoal.Structure,new ColliderCounts(1,0,1),new ColliderGeometryCounts(0,0,1),0,new ColliderPerformanceSettings());
            Assert.That(missing.Level,Is.EqualTo(ColliderPerformanceLevel.RuleMismatch));
        }
        [Test] public void UnavailablePreviewDoesNotMatchAttentionOrAllConvex()
        {
            var report=ColliderPerformance.Candidate(null,new ColliderPerformanceSettings());
            Assert.That(report.Exists,Is.False);Assert.That(report.Matches(ColliderPerformanceFilter.All));
            Assert.That(report.Matches(ColliderPerformanceFilter.Reminder),Is.False);Assert.That(report.Matches(ColliderPerformanceFilter.AllConvex),Is.False);
        }
    }
}
