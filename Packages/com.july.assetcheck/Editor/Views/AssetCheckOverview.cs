using System.Linq;
using UnityEngine.UIElements;

namespace July.AssetCheck
{
    internal sealed class AssetCheckOverview : VisualElement
    {
        private readonly Label _problem, _unverified, _passed, _ignored;
        private readonly Label _metadata, _state;

        internal AssetCheckOverview()
        {
            AddToClassList("overview");
            var metrics = new VisualElement();
            metrics.AddToClassList("metrics");
            _problem = Metric(metrics, "问题文件", "problem-count");
            _unverified = Metric(metrics, "仅未验证", "unverified-count");
            _passed = Metric(metrics, "通过", "passed-count");
            _ignored = Metric(metrics, "忽略", "ignored-count");
            _metadata = new Label();
            _metadata.AddToClassList("scan-metadata");
            metrics.Add(_metadata);
            Add(metrics);
            _state = new Label { name = "report-state" };
            _state.AddToClassList("report-state");
            Add(_state);
        }

        internal void Bind(AssetCheckReport report)
        {
            _problem.text = report?.ProblemAssets.ToString("N0") ?? "—";
            _unverified.text = report?.UnverifiedOnlyFiles.ToString("N0") ?? "—";
            _passed.text = report?.PassedAssets.ToString("N0") ?? "—";
            _ignored.text = report?.IgnoredFiles.ToString("N0") ?? "—";
            _metadata.text = report == null ? "尚未检查" :
                $"已检查 {report.CheckedFiles:N0} / {report.TotalFiles:N0}\n{report.StartedAt:MM-dd HH:mm}";
            _metadata.tooltip = report == null ? "选择范围后点击检查。" :
                $"扫描范围：{report.Scope}\n问题 {report.Issues.Count(issue => issue.HasProblem)} 项；{report.UnverifiedAssets} 个文件含未验证项。";
            var state = report == null ? "选择扫描范围，开始检查资源。" :
                report.Failure != null ? "检查失败，请查看底部错误信息后重新检查。" :
                report.Stale ? "资源或检查标准已变化，请重新检查。" :
                report.Cancelled ? "检查已取消，当前保留部分结果。" :
                !report.Complete ? report.IsSnapshot ? "上次检查未完成，请重新检查。" : "正在检查，结果持续更新。" : "";
            if (report?.IsSnapshot == true) state = "上次结果 · 只读，重新检查后可修改。" + (state.Length > 0 ? " " + state : "");
            _state.text = state;
            _state.tooltip = state;
            _state.style.display = state.Length == 0 ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private static Label Metric(VisualElement parent, string caption, string name)
        {
            var metric = new VisualElement(); metric.AddToClassList("metric");
            var value = new Label("—") { name = name }; value.AddToClassList("metric-value");
            metric.Add(value);
            var label = new Label(caption); label.AddToClassList("metric-caption"); metric.Add(label);
            parent.Add(metric);
            return value;
        }
    }
}
