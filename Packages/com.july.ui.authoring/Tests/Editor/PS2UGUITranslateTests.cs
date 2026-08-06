using System;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace July.UI.Authoring.Editor.Tests
{
    public class PS2UGUITranslateTests
    {
        [Test]
        public void Translate_DoesNotMaterializeGroupPathNodesAndKeepsCanvasCoordinates()
        {
            var jsonPath = Path.Combine(Path.GetTempPath(), $"ps2ugui-{Guid.NewGuid():N}.json");
            File.WriteAllText(jsonPath, @"
{
  ""canvas"": { ""width"": 1080, ""height"": 1920 },
  ""layers"": [
    {
      ""id"": 0,
      ""name"": ""img_title_gongxihuode"",
      ""groupPath"": ""panel/header"",
      ""type"": ""image"",
      ""x"": 273,
      ""y"": 305,
      ""width"": 534,
      ""height"": 156,
      ""visible"": true,
      ""order"": 0,
      ""opacity"": 100,
      ""scaleX"": 1,
      ""scaleY"": 1
    },
    {
      ""id"": 1,
      ""name"": ""tile_catchgoose%~"",
      ""groupPath"": ""background"",
      ""type"": ""image"",
      ""x"": -1332,
      ""y"": -30,
      ""width"": 3859,
      ""height"": 1962,
      ""visible"": true,
      ""order"": 1,
      ""opacity"": 100,
      ""scaleX"": 1,
      ""scaleY"": 1
    }
  ]
}");

            try
            {
                var data = PS2UGUITranslate.Translate(jsonPath);

                var title = data.children.Single(node => node.name == "TitleGongxihuodeImg");
                var tile = data.children.Single(node => node.name == "CatchgooseTile");

                Assert.That(data.children.Count(node => node.type == "node"), Is.Zero);
                Assert.That(title.x, Is.EqualTo(0f).Within(0.001f));
                Assert.That(title.y, Is.EqualTo(577f).Within(0.001f));
                Assert.That(tile.children, Is.Empty);
            }
            finally
            {
                File.Delete(jsonPath);
            }
        }
    }
}
