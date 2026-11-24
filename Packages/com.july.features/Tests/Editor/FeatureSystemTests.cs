using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using July.Arch;
using July.Events;
using July.Persistence;
using July.Platform;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace July.Features.Tests
{
    public sealed class FeatureSystemTests
    {
        private ArchContext _arch;
        private Action<Exception> _previousEventErrorHandler;
        private TestPlatform _platform;
        private ProjectStateStore _store;
        private ProjectFeatureSystem _projectFeatures;
        private IFeatureSystem _features;

        [SetUp]
        public void SetUp()
        {
            _previousEventErrorHandler = EventBus.ErrorHandler;
            _arch = new ArchContext();
            _platform = new TestPlatform { PlatformType = 4 };
            _store = new ProjectStateStore();
            _projectFeatures = new ProjectFeatureSystem();
            _arch.RegisterSystem(_platform);
            _arch.RegisterSystem(new JsonSerializeSystem());
            _arch.RegisterSystem(_projectFeatures);
            _arch.RegisterStore(_store);
            _features = _arch.GetSystem<IFeatureSystem>();
        }

        [TearDown]
        public void TearDown()
        {
            try { _arch.Shutdown(); }
            finally { EventBus.ErrorHandler = _previousEventErrorHandler; }
        }

        [Test]
        public void LoadsThroughInterfaceBeforeSystemInitialization()
        {
            Assert.That(_arch.Initialized, Is.False);
            _features.LoadConfig(@"[
                {""id"": 0, ""conditions"": []}
            ]");

            Assert.That(_features.CanUse(0), Is.True);
            Assert.That(_features.CanUse(101), Is.False);
        }

        [Test]
        public void CanUseRequiresEveryConditionUsingCurrentState()
        {
            _features.LoadConfig(@"[
                {
                    ""id"": 101,
                    ""conditions"": [{""type"": 1, ""arg"": ""4""}, {""type"": 2, ""arg"": ""3""}]
                }
            ]");

            Assert.That(_features.CanUse(101), Is.False);

            _store.GetData().LoginDays = 1;
            Assert.That(_features.CanUse(101), Is.False);

            _store.GetData().LoginDays = 3;
            Assert.That(_features.CanUse(101), Is.True);

            _platform.PlatformType = 3;
            Assert.That(_features.CanUse(101), Is.False);
        }

        [Test]
        public void NegatesTheEntireConditionGroup()
        {
            _features.LoadConfig(@"[
                {""id"": 101, ""negate"": true, ""conditions"": [
                    {""type"": 1, ""arg"": ""4""}, {""type"": 2, ""arg"": ""3""}
                ]},
                {""id"": 102, ""negate"": true, ""conditions"": []}
            ]");
            _store.GetData().LoginDays = 3;

            Assert.That(_features.CanUse(101), Is.False);
            Assert.That(_features.CanUse(102), Is.False);

            _store.GetData().LoginDays = 2;
            Assert.That(_features.CanUse(101), Is.True);

            _store.GetData().LoginDays = 3;
            _platform.PlatformType = 3;
            Assert.That(_features.CanUse(101), Is.True);
        }

        [Test]
        public void SharedRuleParsesEachConditionArgumentWhenQueried()
        {
            var state = _store.GetData();
            state.LoginDays = 4;
            _features.LoadConfig(@"[
                {""id"": 101, ""conditions"": [{""type"": 2, ""arg"": ""3""}]},
                {""id"": 102, ""conditions"": [{""type"": 2, ""arg"": ""7""}]}
            ]");

            Assert.That(state.LoginArgumentParses, Is.Zero);
            Assert.That(_features.CanUse(101), Is.True);
            Assert.That(_features.CanUse(102), Is.False);
            Assert.That(state.LoginArgumentParses, Is.EqualTo(2));

            state.LoginDays = 7;
            Assert.That(_features.CanUse(102), Is.True);
            Assert.That(state.LoginArgumentParses, Is.EqualTo(3));
        }

        [Test]
        public void SkipsLaterRulesAfterFailureAndForMissingFeatures()
        {
            var state = _store.GetData();
            state.Region = "global";
            _features.LoadConfig(@"[
                {
                    ""id"": 101,
                    ""conditions"": [{""type"": 1, ""arg"": ""3""}, {""type"": 0, ""arg"": ""global""}]
                }
            ]");

            Assert.That(_features.CanUse(101), Is.False);
            Assert.That(_features.CanUse(404), Is.False);
            Assert.That(state.RegionChecks, Is.Zero);

            _platform.PlatformType = 3;
            Assert.That(_features.CanUse(101), Is.True);
            Assert.That(state.RegionChecks, Is.EqualTo(1));
        }

        [Test]
        public void ReadsProjectExtendedRowsAndPassesStringArguments()
        {
            _features.LoadConfig(@"[
                {
                    ""id"": 101,
                    ""conditions"": [
                        {""type"": 0, ""arg"": ""global"", ""authorNote"": {""tags"": [""test""]}}
                    ],
                    ""priority"": 10,
                    ""prefabKey"": ""entry/example"",
                    ""tracking"": {""placement"": ""home""}
                },
                {
                    ""id"": 102,
                    ""conditions"": [{""type"": 0, ""arg"": """"}]
                }
            ]");

            Assert.That(_features.CanUse(101), Is.False);
            Assert.That(_features.CanUse(102), Is.True);

            _store.GetData().Region = "global";
            Assert.That(_features.CanUse(101), Is.True);
            Assert.That(_features.CanUse(102), Is.False);
        }

        [TestCase("[{\"id\": 101}]")]
        [TestCase("[{\"id\": 101, \"conditions\": null}]")]
        public void RequiresConditionsForConfiguredFeatures(string json)
        {
            var error = Assert.Throws<FormatException>(() => _features.LoadConfig(json));
            Assert.That(error.Message, Does.Contain("conditions"));
        }

        [Test]
        public void QueriesRequireAConfigThatLoadedSuccessfully()
        {
            Assert.Throws<InvalidOperationException>(() => _features.CanUse(101));

            Assert.Throws<FormatException>(() => _features.LoadConfig("[{}]"));
            Assert.Throws<InvalidOperationException>(() => _features.CanUse(101));
        }

        [Test]
        public void RejectsDuplicateProjectRuleIds()
        {
            _projectFeatures.DuplicateLoginDaysRule = true;
            var error = Assert.Throws<ArgumentException>(() => _features.LoadConfig("[]"));
            Assert.That(error.Message, Does.Contain("规则 ID 2"));
        }

        [Test]
        public void ReportsDuplicateFeatureIds()
        {
            var error = Assert.Throws<FormatException>(() => _features.LoadConfig(@"[
                {""id"": 101, ""conditions"": []},
                {""id"": 101, ""conditions"": []}
            ]"));
            Assert.That(error.Message, Does.Contain("101"));
        }

        [TestCase("")]
        [TestCase("[null]")]
        [TestCase("[{}]")]
        [TestCase("[{\"id\": -1}]")]
        public void RejectsInvalidFeatureRowsWhenLoaded(string json)
        {
            Assert.Throws<FormatException>(() => _features.LoadConfig(json));
        }

        [TestCase("[{\"id\": 101, \"conditions\": [null]}]")]
        [TestCase("[{\"id\": 101, \"conditions\": [{\"arg\": \"\"}]}]")]
        [TestCase("[{\"id\": 101, \"conditions\": [{\"type\": -1, \"arg\": \"\"}]}]")]
        public void RejectsInvalidConditionsWhenQueried(string json)
        {
            _features.LoadConfig(json);
            Assert.Throws<FormatException>(() => _features.CanUse(101));
        }

        [TestCase("[")]
        [TestCase("[{\"id\": null}]")]
        [TestCase("[{\"id\": 101, \"conditions\": [{\"type\": null, \"arg\": \"4\"}]}]")]
        [TestCase("[{\"id\": 101, \"conditions\": [{\"type\": 1, \"arg\": 4}]}]")]
        [TestCase("[{\"id\": 101, \"conditions\": [{\"type\": \"1\", \"arg\": \"4\"}]}]")]
        public void ReportsInvalidJsonThroughTheSharedSerializer(string json)
        {
            LogAssert.Expect(LogType.Exception, new Regex("JsonException"));
            Assert.Catch(() => _features.LoadConfig(json));
        }

        [TestCase("[{\"id\": 101, \"conditions\": [{\"type\": 1, \"arg\": \"abc\"}]}]")]
        [TestCase("[{\"id\": 101, \"conditions\": [{\"type\": 1}]}]")]
        [TestCase("[{\"id\": 101, \"conditions\": [{\"type\": 1, \"arg\": null}]}]")]
        public void ReportsInvalidPlatformArgumentWhenQueried(string json)
        {
            _features.LoadConfig(json);
            var error = Assert.Throws<ArgumentException>(() => _features.CanUse(101));
            Assert.That(error.Message, Does.Contain("平台编号必须是整数"));
        }

        [Test]
        public void ReportsUnknownRuleWhenQueried()
        {
            _features.LoadConfig("[{\"id\": 101, \"conditions\": [{\"type\": 999, \"arg\": \"1\"}]}]");
            Assert.Throws<KeyNotFoundException>(() => _features.CanUse(101));
        }

        [Test]
        public void ParsesLaterArgumentsOnlyAfterEarlierConditionsPass()
        {
            _features.LoadConfig(@"[
                {
                    ""id"": 101,
                    ""conditions"": [{""type"": 1, ""arg"": ""3""}, {""type"": 2, ""arg"": ""-1""}]
                }
            ]");

            Assert.That(_features.CanUse(101), Is.False);
            _platform.PlatformType = 3;
            var error = Assert.Throws<ArgumentException>(() => _features.CanUse(101));
            Assert.That(error.Message, Does.Contain("登录天数必须是非负整数"));
        }

        [Test]
        public void ReplacesConfigAtomicallyAndReusesTheRegisteredRules()
        {
            _store.GetData().LoginDays = 3;
            _features.LoadConfig(@"[
                {""id"": 101, ""conditions"": [{""type"": 2, ""arg"": ""3""}]}
            ]");

            var error = Assert.Throws<FormatException>(() => _features.LoadConfig(@"[
                {""id"": 102, ""conditions"": [{""type"": 2, ""arg"": ""7""}]},
                null
            ]"));
            Assert.That(error.Message, Does.Contain("第 2 项").And.Contain("不能为空"));
            Assert.That(_features.CanUse(101), Is.True);
            Assert.That(_features.CanUse(102), Is.False);

            _features.LoadConfig(@"[{""id"": 102, ""conditions"": []}]");
            Assert.That(_features.CanUse(101), Is.False);
            Assert.That(_features.CanUse(102), Is.True);
            Assert.That(_projectFeatures.RuleCollectionCount, Is.EqualTo(1));
        }

        private enum ProjectRuleType
        {
            LoginDays = 2,
            Region = 0
        }

        private sealed class ProjectFeatureSystem : FeatureSystemBase
        {
            public bool DuplicateLoginDaysRule { get; set; }
            public int RuleCollectionCount { get; private set; }

            protected override IEnumerable<IFeatureRule> CreateProjectRules()
            {
                RuleCollectionCount++;
                var store = GetStore<ProjectStateStore>();
                yield return new LoginDaysRule(store);
                yield return new RegionRule(store);
                if (DuplicateLoginDaysRule)
                    yield return new LoginDaysRule(store);
            }
        }

        private sealed class ProjectStateStore : StoreBase<ProjectState> { }

        private sealed class ProjectState
        {
            public int LoginDays;
            public int LoginArgumentParses;
            public string Region = "";
            public int RegionChecks;
        }

        private sealed class LoginDaysRule : IFeatureRule
        {
            private readonly ProjectStateStore _store;

            public LoginDaysRule(ProjectStateStore store) => _store = store;

            public int Id => (int)ProjectRuleType.LoginDays;

            public bool IsSatisfied(string arg)
            {
                if (!int.TryParse(arg, NumberStyles.Integer, CultureInfo.InvariantCulture, out var days) || days < 0)
                    throw new ArgumentException("登录天数必须是非负整数。", nameof(arg));
                var state = _store.GetData();
                state.LoginArgumentParses++;
                return state.LoginDays >= days;
            }
        }

        private sealed class RegionRule : IFeatureRule
        {
            private readonly ProjectStateStore _store;

            public RegionRule(ProjectStateStore store) => _store = store;

            public int Id => (int)ProjectRuleType.Region;

            public bool IsSatisfied(string arg)
            {
                var state = _store.GetData();
                state.RegionChecks++;
                return state.Region == arg;
            }
        }

        private sealed class TestPlatform : SystemBase, IPlatformSystem
        {
            public int PlatformType { get; set; }

            public T GetService<T>() where T : class => throw new NotSupportedException();
            public void DeferAllServices() => throw new NotSupportedException();
            public void VibrateShort(VibrateType type = VibrateType.Light) => throw new NotSupportedException();
            public void VibrateLong() => throw new NotSupportedException();
        }
    }
}
