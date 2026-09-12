using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using July.Arch;
using July.Launch;
using July.Platform;

namespace July.Bootstrap
{
    /// <summary>配置完整的标准启动流水线，并封闭步骤列表。</summary>
    public static class Bootstrap
    {
        public static void Configure(LaunchPipeline pipeline, BootstrapConfig config, IBootstrapView view,
            IReadOnlyList<string> generatedAotAssemblies)
        {
            if (pipeline == null) throw new ArgumentNullException(nameof(pipeline));
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (view == null) throw new ArgumentNullException(nameof(view));
            if (pipeline.Count != 0) throw new InvalidOperationException("Bootstrap requires an empty pipeline.");
            pipeline.OnFailed = async (stage, error, ct) =>
            {
                var action = await view.ShowFailureAsync(new LaunchFailure(stage, false, error), ct);
                if (action != LaunchFailureAction.Restart)
                    throw new InvalidOperationException("This startup failure cannot be retried.");
                RestartApplication();
            };
            config.Validate();
            var aot = BootstrapAssemblyLoader.Normalize(generatedAotAssemblies);
            foreach (var assembly in BootstrapAssemblyLoader.Normalize(config.HotUpdate.AdditionalAotMetadataAssemblies))
                if (!aot.Contains(assembly)) aot.Add(assembly);
            var registration = new AppRegistration();

            pipeline.OnStepBegin = (_, current, total) => view.SetStepInfo(current - 1, total);
            pipeline.OnCompleted = view.Complete;
            Add(new PresentLaunchFrameStep());
            Add(new BootArchStep(config.Platform, config.Analytics, config.Release));
            Add(new ParallelLaunchStep("Platform Login & Fetch Config",
                new PlatformLoginStep(), new FetchConfigStep(config.Release, config.Resource.PlayMode, config.EnabledLogChannels)),
                "platform_config", retry: true);
            Add(new InitResourceStep(config.Resource), "init_resource");
            Add(new DownloadStartupResourcesStep(config.Resource.RequiredDownloadTags), retry: true);
            Add(new LoadStartupAssembliesStep(aot, config.HotUpdate.RegistrarAssembly), "hot_update");
            Add(new RegisterAppSystemsStep(config.HotUpdate, registration), "register_systems");
            Add(new InitAppSystemsStep(registration), "init_systems");
            Add(new LaunchGameStep(registration, view));
            Add(new InitializeDeferredServicesStep());
            pipeline.Seal();

            void Add(ILaunchStep step, string telemetryId = null, bool retry = false)
            {
                var name = step.Name;
                if (retry)
                    step = new RetryLaunchStep(step, async (_, ct) =>
                    {
                        var action = await view.ShowFailureAsync(new LaunchFailure(name, true), ct);
                        if (action == LaunchFailureAction.Retry) return true;
                        if (action != LaunchFailureAction.Restart)
                            throw new InvalidOperationException("Unknown launch failure action.");
                        RestartApplication();
                        throw new OperationCanceledException("Application restart requested.");
                    });
                if (telemetryId != null)
                    step = new ObservedLaunchStep(step, observation =>
                    {
                        if (observation.Outcome == LaunchStepOutcome.Succeeded)
                            LaunchTelemetry.Report(telemetryId);
                    });
                pipeline.Add(step);
            }
        }

        private static void RestartApplication()
        {
            // 启动可能在平台服务创建前失败；此时由默认适配器处理桌面端或编辑器退出。
            var lifecycle = ArchContext.Current?.TryGetSystem<IPlatformSystem>()?.GetService<ILifecycleService>();
            if (lifecycle != null) lifecycle.Restart();
            else new DefaultLifecycleService().Restart();
        }
    }

    /// <summary>注册、初始化和进入游戏三个阶段共用同一个热更入口实例。</summary>
    internal sealed class AppRegistration
    {
        internal IHotUpdateRegistrar Registrar;
    }
}
