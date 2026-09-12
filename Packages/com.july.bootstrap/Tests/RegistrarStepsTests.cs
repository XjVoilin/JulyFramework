using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using July.Launch;
using NUnit.Framework;

namespace July.Bootstrap.Tests
{
    public class RegistrarStepsTests
    {
        [Test]
        public void RegisterCreatesOneInstanceAndLaunchUsesThatInstance()
        {
            TestRegistrar.Created = 0;
            var state = new AppRegistration();
            var options = RegistrarOptions(typeof(TestRegistrar));
            Assert.That(new RegisterAppSystemsStep(options, state).ExecuteAsync(default).GetAwaiter().GetResult(), Is.True);
            var registrar = (TestRegistrar)state.Registrar;
            var view = new TestView();
            Assert.That(new LaunchGameStep(state, view).ExecuteAsync(default).GetAwaiter().GetResult(), Is.True);
            Assert.That(TestRegistrar.Created, Is.EqualTo(1));
            Assert.That(registrar.Registered, Is.True);
            Assert.That(registrar.Launched, Is.True);
            Assert.That(view.Completed, Is.False); // 只有整条流水线成功后，才允许结束启动画面。
        }

        [Test]
        public void MissingRegistrarAssemblyStopsRegistration()
        {
            var options = RegistrarOptions(typeof(TestRegistrar));
            options.RegistrarAssembly = "Missing.Startup.Assembly";
            var state = new AppRegistration();
            Assert.Throws<InvalidOperationException>(() => new RegisterAppSystemsStep(options, state).ExecuteAsync(default));
            Assert.That(state.Registrar, Is.Null);
        }

        [Test]
        public void WrongRegistrarTypeFailsAtReflectionBoundary()
        {
            var options = RegistrarOptions(typeof(TestView));
            Assert.Throws<InvalidOperationException>(() => new RegisterAppSystemsStep(options, new AppRegistration()).ExecuteAsync(default));
        }

        [Test]
        public void FailedPreInitializationDoesNotContinueToArchitecture()
        {
            var error = new InvalidOperationException("Tables failed");
            var registrar = new TestRegistrar { PreInit = UniTask.FromException(error) };
            var state = new AppRegistration { Registrar = registrar }; // 刻意不创建 Arch，以验证预初始化失败后不会继续访问它。
            var task = new InitAppSystemsStep(state).ExecuteAsync(default);
            Assert.That(Assert.Throws<InvalidOperationException>(() => task.GetAwaiter().GetResult()), Is.SameAs(error));
        }

        [Test]
        public void CancellationReachesRegistrarAndDoesNotCompleteView()
        {
            using var ct = new CancellationTokenSource();
            var pending = new UniTaskCompletionSource();
            var registrar = new TestRegistrar { Launch = pending.Task };
            var view = new TestView();
            var task = new LaunchGameStep(new AppRegistration { Registrar = registrar }, view).ExecuteAsync(ct.Token);
            ct.Cancel();
            Assert.That(registrar.CancellationObserved, Is.True);
            Assert.Throws<OperationCanceledException>(() => task.GetAwaiter().GetResult());
            Assert.That(view.Completed, Is.False);
        }

        [Test]
        public void LaunchFailureDoesNotCompleteView()
        {
            var error = new InvalidOperationException("Business launch failed");
            var view = new TestView();
            var registrar = new TestRegistrar { Launch = UniTask.FromException(error) };
            var task = new LaunchGameStep(new AppRegistration { Registrar = registrar }, view).ExecuteAsync(default);
            Assert.That(Assert.Throws<InvalidOperationException>(() => task.GetAwaiter().GetResult()), Is.SameAs(error));
            Assert.That(view.Completed, Is.False);
        }

        private static HotUpdateConfig RegistrarOptions(Type type) => new()
        {
            RegistrarAssembly = type.Assembly.GetName().Name,
            RegistrarType = type.FullName,
        };

        public sealed class TestRegistrar : IHotUpdateRegistrar
        {
            public static int Created;
            public bool Registered;
            public bool Launched;
            private CancellationToken _launchCancellation;
            public bool CancellationObserved => _launchCancellation.IsCancellationRequested;
            public UniTask PreInit = UniTask.CompletedTask;
            public UniTask Launch = UniTask.CompletedTask;
            public TestRegistrar() => Created++;
            public void Register() => Registered = true;
            public UniTask PreInitializeAsync(CancellationToken ct = default) => PreInit;
            public UniTask OnGameLaunch(CancellationToken ct = default)
            {
                Launched = true;
                _launchCancellation = ct;
                return Launch.AttachExternalCancellation(ct);
            }
        }

        public sealed class TestView : IBootstrapView
        {
            public bool Completed;
            public void SetStepInfo(int currentStep, int totalSteps) { }
            public UniTask<LaunchFailureAction> ShowFailureAsync(LaunchFailure failure, CancellationToken ct) => UniTask.FromResult(LaunchFailureAction.Retry);
            public void PrepareForGame() { }
            public void Complete() => Completed = true;
        }
    }
}
