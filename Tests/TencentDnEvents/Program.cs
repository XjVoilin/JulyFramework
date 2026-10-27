using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using July.Analytics;
using July.Analytics.TencentDn;

internal static class Program
{
    private static int _assertions;

    private static void Main()
    {
        CheckLifecycleEvents();
        CheckLevelEnter();
        CheckLevelResults();
        CheckOptionalFieldsAndZeros();
        CheckSnapshotIsolation();
        CheckJsonProtocol();
        CheckProgress();
        CheckAdEvents();
        Console.WriteLine($"PASS: 8 protocol scenarios, {_assertions} assertions.");
    }

    private static void CheckLifecycleEvents()
    {
        var events = new (IBIEvent Event, string Name)[]
        {
            (new TencentDnRegisterEvent(), "REGISTER"),
            (new TencentDnLoadFinishEvent(), "LOAD_FINISH"),
            (new TencentDnTutorialStartEvent(), "TUTORIAL_START"),
            (new TencentDnTutorialFinishEvent(), "TUTORIAL_FINISH")
        };
        foreach (var item in events)
        {
            Equal(item.Name, item.Event.EventName);
            Keys(item.Event.ToParams());
        }

        var reactive = new TencentDnReactiveEvent(30);
        Equal("RE_ACTIVE", reactive.EventName);
        Keys(reactive.ToParams(), "backFlowDay");
        Equal(30, reactive.ToParams()["backFlowDay"]);
    }

    private static void CheckLevelEnter()
    {
        var minimal = new TencentDnLevelEnterEvent(new TencentDnLevelInfo(12));
        Equal("LEVEL_ENTER", minimal.EventName);
        Keys(minimal.ToParams(), "level_id");
        Equal(12, minimal.ToParams()["level_id"]);

        var evt = new TencentDnLevelEnterEvent(FullLevel(), "第1关");
        var p = evt.ToParams();
        Keys(p, "level_id", "enter_level_id", "game_mode", "chapter_id", "coin_amount",
            "stamina_value", "level_value", "enter_level_name");
        Equal("第1关", p["enter_level_name"]);
        Equal(0.55d, p["enter_level_id"]);
        Equal("自定义模式", p["game_mode"]);
        Equal(1, p["chapter_id"]);
        Equal(100L, p["coin_amount"]);
        Equal(10, p["stamina_value"]);
        Equal(1, p["level_value"]);
    }

    private static void CheckLevelResults()
    {
        var result = FullResult();
        var events = new (IBIEvent Event, string Name)[]
        {
            (new TencentDnLevelExitEvent(result), "LEVEL_EXIT"),
            (new TencentDnLevelLoseEvent(result), "LEVEL_LOSE"),
            (new TencentDnLevelPassEvent(result), "LEVEL_PASS")
        };
        foreach (var item in events)
        {
            Equal(item.Name, item.Event.EventName);
            var p = item.Event.ToParams();
            Keys(p, "level_id", "enter_level_id", "game_mode", "chapter_id", "coin_amount",
                "stamina_value", "level_value", "ad_cnt", "duration", "items");
            Equal(1, p["ad_cnt"]);
            Equal(60, p["duration"]);
            Equal(0.55d, p["enter_level_id"]);
            Equal("{item_type:4,item_num:3}", ((string[])p["items"])[0]);
            Equal("{item_type:22,item_num:2}", ((string[])p["items"])[1]);
        }
    }

    private static void CheckOptionalFieldsAndZeros()
    {
        var minimal = new TencentDnLevelLoseEvent(
            new TencentDnLevelResult(new TencentDnLevelInfo(1), 0)).ToParams();
        Keys(minimal, "level_id", "ad_cnt");
        Equal(0, minimal["ad_cnt"]);

        var zeroLevel = new TencentDnLevelInfo(0, 0, "", 0, 0, 0, 0);
        var zeros = new TencentDnLevelExitEvent(
            new TencentDnLevelResult(zeroLevel, 0, 0, Array.Empty<TencentDnItemUsage>())).ToParams();
        Keys(zeros, "level_id", "enter_level_id", "game_mode", "chapter_id", "coin_amount",
            "stamina_value", "level_value", "ad_cnt", "duration", "items");
        Equal(0d, zeros["enter_level_id"]);
        Equal(0, zeros["chapter_id"]);
        Equal(0L, zeros["coin_amount"]);
        Equal(0, zeros["duration"]);
        Equal(0, ((string[])zeros["items"]).Length);
    }

    private static void CheckSnapshotIsolation()
    {
        var input = new[] { new TencentDnItemUsage(4, 3) };
        var evt = new TencentDnLevelPassEvent(
            new TencentDnLevelResult(new TencentDnLevelInfo(1), 1, items: input));
        input[0] = new TencentDnItemUsage(22, 100);
        var first = evt.ToParams();
        Equal("{item_type:4,item_num:3}", ((string[])first["items"])[0]);
        ((string[])first["items"])[0] = "changed";
        first["ad_cnt"] = 999;
        var second = evt.ToParams();
        Equal("{item_type:4,item_num:3}", ((string[])second["items"])[0]);
        Equal(1, second["ad_cnt"]);
    }

    private static void CheckJsonProtocol()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(
                new TencentDnLevelLoseEvent(FullResult()).ToParams()));
            var root = json.RootElement;
            Equal(0.55d, root.GetProperty("enter_level_id").GetDouble());
            Equal(JsonValueKind.Number, root.GetProperty("ad_cnt").ValueKind);
            Equal(JsonValueKind.Number, root.GetProperty("duration").ValueKind);
            Equal("自定义模式", root.GetProperty("game_mode").GetString());
            var items = root.GetProperty("items");
            Equal(JsonValueKind.Array, items.ValueKind);
            Equal(JsonValueKind.String, items[0].ValueKind);
            Equal("{item_type:4,item_num:3}", items[0].GetString());
            Equal("{item_type:22,item_num:2}", items[1].GetString());
        }
        finally { CultureInfo.CurrentCulture = originalCulture; }
    }

    private static void CheckProgress()
    {
        Equal(0.56d, new TencentDnLevelEnterEvent(new TencentDnLevelInfo(1, 0.555)).ToParams()["enter_level_id"]);
        Equal(0d, new TencentDnLevelEnterEvent(new TencentDnLevelInfo(1, 0)).ToParams()["enter_level_id"]);
        Equal(1d, new TencentDnLevelEnterEvent(new TencentDnLevelInfo(1, 1)).ToParams()["enter_level_id"]);
        foreach (var progress in new[] { -0.01d, 1.01d, double.NaN, double.NegativeInfinity, double.PositiveInfinity })
        {
            var rejected = false;
            try { _ = new TencentDnLevelInfo(1, progress); }
            catch (ArgumentOutOfRangeException) { rejected = true; }
            Require(rejected, $"Invalid progress was accepted: {progress}");
        }
    }

    private static void CheckAdEvents()
    {
        IBIEvent show = new TencentDnAdPlacementShowEvent(3);
        IBIEvent click = new TencentDnAdClickEvent(1);
        IBIEvent finish = new TencentDnAdVideoFinishEvent(2);
        Equal("AD_PLACEMENT_SHOW", show.EventName);
        Equal("AD_CLICK", click.EventName);
        Equal("AD_VIDEO_FINISH", finish.EventName);
        Keys(show.ToParams(), "ad_placement_name");
        Keys(click.ToParams(), "ad_placement_name");
        Keys(finish.ToParams(), "ad_placement_name");
        Equal(3, show.ToParams()["ad_placement_name"]);
        Equal(1, click.ToParams()["ad_placement_name"]);
        Equal(2, finish.ToParams()["ad_placement_name"]);
    }

    private static TencentDnLevelInfo FullLevel() =>
        new(1, 0.55, "自定义模式", 1, 100, 10, 1);

    private static TencentDnLevelResult FullResult() => new(FullLevel(), 1, 60,
        new[] { new TencentDnItemUsage(4, 3), new TencentDnItemUsage(22, 2) });

    private static void Keys(Dictionary<string, object> parameters, params string[] expected)
    {
        Require(parameters.Keys.OrderBy(x => x).SequenceEqual(expected.OrderBy(x => x)),
            "Protocol fields differ: " + string.Join(",", parameters.Keys));
    }

    private static void Equal(object expected, object actual) =>
        Require(Equals(expected, actual), $"Expected {expected}, got {actual}");

    private static void Require(bool condition, string message)
    {
        _assertions++;
        if (!condition) throw new InvalidOperationException(message);
    }
}
