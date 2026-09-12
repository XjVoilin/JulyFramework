using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using July.Launch;

namespace July.Bootstrap
{
    internal sealed class DownloadStartupResourcesStep : ILaunchStep
    {
        private readonly List<string> _tags = new();
        public string Name => "Download Startup Resources";
        internal DownloadStartupResourcesStep(IEnumerable<string> tags)
        {
            foreach (var tag in tags)
                if (!_tags.Contains(tag)) _tags.Add(tag);
        }
        public UniTask<bool> ExecuteAsync(CancellationToken ct)
        {
            var resource = July.Arch.ArchContext.Current.GetSystem<July.Resource.IResourceSystem>();
            var downloads = new Func<CancellationToken, UniTask<bool>>[_tags.Count];
            for (var i = 0; i < _tags.Count; i++)
            {
                var tag = _tags[i];
                downloads[i] = token => resource.DownloadByTagWithRetryAsync(tag, ct: token);
            }
            return ParallelLaunchStep.RunAsync(ct, downloads);
        }
    }
}