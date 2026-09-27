using System;

namespace July.Guide
{
    /// <summary>A complete interaction, including observation, presentation and cleanup.</summary>
    public sealed class GuideStepDefinition
    {
        public int Id { get; }
        public int Type { get; }
        public int ParamId { get; }

        public GuideStepDefinition(int id, int type, int paramId = 0)
        {
            if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));
            if (type <= 0) throw new ArgumentOutOfRangeException(nameof(type));
            Id = id;
            Type = type;
            ParamId = paramId;
        }
    }
}
