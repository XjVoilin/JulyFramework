using System;
using System.Collections.Generic;

namespace July.Analytics.TencentDn
{
    /// <summary>
    /// 一次关卡行为发生时的状态快照；除关卡 ID 外，字段在玩法具有对应概念时才提供。
    /// Progress 是当前单关进度而非关卡序号，按协议保留两位小数。
    /// </summary>
    public readonly struct TencentDnLevelInfo
    {
        public readonly int LevelId;
        public readonly double? Progress;
        public readonly string GameMode;
        public readonly int? ChapterId;
        public readonly long? CoinAmount;
        public readonly int? StaminaValue;
        public readonly int? LevelValue;

        public TencentDnLevelInfo(int levelId, double? progress = null, string gameMode = null,
            int? chapterId = null, long? coinAmount = null, int? staminaValue = null,
            int? levelValue = null)
        {
            if (progress.HasValue && (double.IsNaN(progress.Value) ||
                progress.Value < 0 || progress.Value > 1))
                throw new ArgumentOutOfRangeException(nameof(progress), "关卡进度必须在 0 到 1 之间。");

            LevelId = levelId;
            Progress = progress.HasValue
                ? Math.Round(progress.Value, 2, MidpointRounding.AwayFromZero)
                : null;
            GameMode = gameMode;
            ChapterId = chapterId;
            CoinAmount = coinAmount;
            StaminaValue = staminaValue;
            LevelValue = levelValue;
        }

        internal Dictionary<string, object> CreateParameters()
        {
            var parameters = new Dictionary<string, object>(7) { { "level_id", LevelId } };
            if (Progress.HasValue) parameters.Add("enter_level_id", Progress.Value);
            if (GameMode != null) parameters.Add("game_mode", GameMode);
            if (ChapterId.HasValue) parameters.Add("chapter_id", ChapterId.Value);
            if (CoinAmount.HasValue) parameters.Add("coin_amount", CoinAmount.Value);
            if (StaminaValue.HasValue) parameters.Add("stamina_value", StaminaValue.Value);
            if (LevelValue.HasValue) parameters.Add("level_value", LevelValue.Value);
            return parameters;
        }
    }
}
