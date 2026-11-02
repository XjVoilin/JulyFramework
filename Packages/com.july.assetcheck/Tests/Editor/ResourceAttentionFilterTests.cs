using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace July.AssetCheck.Tests
{
    public sealed class ResourceAttentionFilterTests
    {
        static IEnumerable<TestCaseData> States => Enumerable.Range(0, 16).Select(bits =>
            new TestCaseData((bits & 1) != 0, (bits & 2) != 0, (bits & 4) != 0, (bits & 8) != 0));

        [TestCaseSource(nameof(States))]
        public void AttentionIsExactlyTheComplementOfPassedIncludingMixedStates(bool complete, bool problem, bool unverified, bool candidate)
        {
            var data = new AssetCheckFileData { Id = "a", Path = "Assets/a.asset", Status = "original status", Complete = complete,
                Unverified = unverified, Candidate = candidate, Issues = problem ? new[] {
                    new AssetCheckIssue("Assets/a.asset", "a", AssetCheckKind.Model, "rule", "problem", "1", "0", false)
                } : Array.Empty<AssetCheckIssue>() };
            var model = new AssetCheckResultsModel(); model.BindFiles(new object(), "category", 1, new[] { data });
            model.Query("", "通过"); bool passed = model.Files.Count == 1;
            Assert.AreEqual(complete && !problem && !unverified && !candidate, passed);
            model.Query("", "需要关注"); Assert.AreEqual(passed ? 0 : 1, model.Files.Count);
            Assert.AreEqual("original status", data.Status); Assert.IsEmpty(model.AllFixable());
        }

        [Test] public void AttentionIncludesIssueLevelUnverifiedAndCombinesWithSearchAndRuleFilters()
        {
            var a = new AssetCheckFileData { Id = "a", Path = "Assets/Folder/a.asset", Complete = true, Issues = new[] {
                new AssetCheckIssue("Assets/Folder/a.asset", "a", AssetCheckKind.Model, "verify", "unknown", "?", "0", false, unverified: true)
            } };
            var b = new AssetCheckFileData { Id = "b", Path = "Assets/Other/b.asset", Complete = true, Candidate = true };
            var model = new AssetCheckResultsModel(); model.BindFiles(new object(), "category", 1, new[] { a, b });
            model.Query("", "需要关注"); Assert.AreEqual(2, model.Files.Count);
            model.Query("Folder", "需要关注"); Assert.AreEqual("a", model.Files.Single().Id);
            model.Filter("verify"); model.Query("", "需要关注"); Assert.AreEqual("a", model.Files.Single().Id);
            model.Query("", "通过"); Assert.IsEmpty(model.Files);
        }
    }
}
