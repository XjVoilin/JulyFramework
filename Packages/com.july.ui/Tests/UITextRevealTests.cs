using System.Collections;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;

namespace July.UI.Tests
{
    public sealed class UITextRevealTests
    {
        private GameObject _root;
        private TMP_FontAsset _font;
        private TMP_Text _text;
        private UITextReveal _effect;

        [SetUp]
        public void SetUp()
        {
            Assert.That(TMP_Settings.defaultFontAsset, Is.Not.Null, "The test host must configure a TMP default font.");
            _font = Object.Instantiate(TMP_Settings.defaultFontAsset);
            _root = new GameObject("RevealCanvas", typeof(RectTransform), typeof(Canvas));
            _root.SetActive(false);
            _root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var label = new GameObject("Text", typeof(RectTransform));
            label.transform.SetParent(_root.transform, false);
            _text = label.AddComponent<TextMeshProUGUI>();
            _text.font = _font;
            _text.alignment = TextAlignmentOptions.Center;
            _text.rectTransform.sizeDelta = new Vector2(800, 100);
            _text.maxVisibleCharacters = 99;
            _effect = label.AddComponent<UITextReveal>();
            Set("_characterInterval", 0.3f);
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Time.timeScale = 1f;
            Object.DestroyImmediate(_root);
            Object.DestroyImmediate(_font);
        }

        [UnityTest]
        public IEnumerator TailLoop_RevealsParsedCharactersWithoutChangingTextOrPrefixPosition()
        {
            const string source = "<b>12345...</b>";
            _text.text = source;
            _text.maxVisibleCharacters = 0;
            Set("_animatedCharacterCount", 3);
            Set("_loopCount", -1);
            _root.SetActive(true);
            yield return ReadyForPlayback();

            Assert.That(_text.textInfo.characterCount, Is.EqualTo(8));
            Assert.That(_text.maxVisibleCharacters, Is.EqualTo(5));
            _text.ForceMeshUpdate();
            var origin = _text.textInfo.characterInfo[0].origin;
            var elapsed = 0f;
            foreach (var expected in new[] { 6, 7, 8, 5 })
            {
                elapsed += 0.301f;
                SampleAt(elapsed);
                Assert.That(_text.maxVisibleCharacters, Is.EqualTo(expected));
                Assert.That(_text.text, Is.EqualTo(source));
                _text.ForceMeshUpdate();
                Assert.That(_text.textInfo.characterInfo[0].origin, Is.EqualTo(origin).Within(0.001f));
            }
        }

        [UnityTest]
        public IEnumerator PositiveLoopCount_PlaysExactlyThatManyCycles_ThenRemainsFullyVisible()
        {
            _text.text = "12345...";
            Set("_animatedCharacterCount", 3);
            Set("_loopCount", 2);
            _root.SetActive(true);
            yield return ReadyForPlayback();
            SampleAt(0.903f);
            Assert.That(_text.maxVisibleCharacters, Is.EqualTo(8));
            SampleAt(1.204f);
            Assert.That(_text.maxVisibleCharacters, Is.EqualTo(5));
            SampleAt(2.105f);
            Assert.That(_text.maxVisibleCharacters, Is.EqualTo(8));
            SampleAt(2.505f);
            Assert.That(_text.maxVisibleCharacters, Is.EqualTo(8));
            SampleAt(10f);
            Assert.That(_text.maxVisibleCharacters, Is.EqualTo(8));
        }

        [UnityTest]
        public IEnumerator WholeText_OnceCompletes_AndStopRestoresAuthoredVisibility()
        {
            _text.text = "12gy";
            _root.SetActive(true);
            yield return ReadyForPlayback();
            Assert.That(_text.maxVisibleCharacters, Is.Zero);
            Assert.That(_text.useMaxVisibleDescender, Is.False);
            _text.ForceMeshUpdate();
            var baseline = _text.textInfo.characterInfo[0].baseLine;
            SampleAt(0.301f);
            Assert.That(_text.maxVisibleCharacters, Is.EqualTo(1));
            SampleAt(1.301f);
            Assert.That(_text.maxVisibleCharacters, Is.EqualTo(4));
            _text.ForceMeshUpdate();
            Assert.That(_text.textInfo.characterInfo[0].baseLine, Is.EqualTo(baseline).Within(0.001f));
            _effect.Stop();
            Assert.That(_text.maxVisibleCharacters, Is.EqualTo(99));
            Assert.That(_text.useMaxVisibleDescender, Is.True);
        }

        [UnityTest]
        public IEnumerator LateAssignment_IsReadAfterEnable()
        {
            _text.text = "";
            Set("_animatedCharacterCount", 3);
            Set("_loopCount", -1);
            _root.SetActive(true);
            _text.text = "12345...";
            yield return ReadyForPlayback();
            Assert.That(_text.maxVisibleCharacters, Is.EqualTo(5));
        }

        [UnityTest]
        public IEnumerator DisableAndReplay_RestoresVisibilityAndRestartsFromZero()
        {
            _text.text = "123...";
            Set("_animatedCharacterCount", 3);
            Set("_loopCount", -1);
            _root.SetActive(true);
            yield return ReadyForPlayback();
            _root.SetActive(false);
            Assert.That(_text.maxVisibleCharacters, Is.EqualTo(99));
            _root.SetActive(true);
            yield return ReadyForPlayback();
            _effect.Play();
            _effect.Play();
            yield return ReadyForPlayback();
            Assert.That(_text.maxVisibleCharacters, Is.EqualTo(3));
        }

        [UnityTest]
        public IEnumerator TimeScaleSelection_UsesTheConfiguredClock()
        {
            _text.text = "123";
            _root.SetActive(true);
            yield return ReadyForPlayback();
            UnityEngine.Time.timeScale = 0f;
            yield return new WaitForSecondsRealtime(0.35f);
            Assert.That(_text.maxVisibleCharacters, Is.GreaterThan(0));
            Set("_ignoreTimeScale", false);
            _effect.Play();
            yield return ReadyForPlayback();
            yield return new WaitForSecondsRealtime(0.35f);
            Assert.That(_text.maxVisibleCharacters, Is.Zero);
        }

        [UnityTest]
        public IEnumerator ManualPlayback_OnlyRestoresSettingsItActuallyOwns()
        {
            _text.text = "1234";
            Set("_playOnEnable", false);
            _root.SetActive(true);
            yield return null;
            _text.maxVisibleCharacters = 7;
            _text.useMaxVisibleDescender = false;
            _effect.Stop();
            _effect.enabled = false;
            Assert.That(_text.maxVisibleCharacters, Is.EqualTo(7));
            Assert.That(_text.useMaxVisibleDescender, Is.False);

            _effect.enabled = true;
            _text.maxVisibleCharacters = 13;
            _effect.Play();
            yield return ReadyForPlayback();
            _effect.Stop();
            Assert.That(_text.maxVisibleCharacters, Is.EqualTo(13));
            Assert.That(_text.useMaxVisibleDescender, Is.False);
            _text.maxVisibleCharacters = 2;
            _text.useMaxVisibleDescender = true;
            _effect.Stop();
            Assert.That(_text.maxVisibleCharacters, Is.EqualTo(2));
            Assert.That(_text.useMaxVisibleDescender, Is.True);
        }

        [UnityTest]
        public IEnumerator DestroyingComponent_RestoresTheText()
        {
            _text.text = "123...";
            Set("_animatedCharacterCount", 3);
            Set("_loopCount", -1);
            _root.SetActive(true);
            yield return ReadyForPlayback();
            Object.Destroy(_effect);
            yield return null;
            Assert.That(_text.maxVisibleCharacters, Is.EqualTo(99));
            Assert.That(_text.useMaxVisibleDescender, Is.True);
        }

        private IEnumerator ReadyForPlayback()
        {
            yield return null;
            yield return null;
            SampleAt(0f);
        }

        private void SampleAt(float elapsed)
        {
            var ignoreTimeScale = (bool)typeof(UITextReveal)
                .GetField("_ignoreTimeScale", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(_effect);
            var delta = ignoreTimeScale ? UnityEngine.Time.unscaledDeltaTime : UnityEngine.Time.deltaTime;
            Set("_elapsed", elapsed - delta);
            _effect.SendMessage("LateUpdate");
        }

        private void Set(string name, object value) => typeof(UITextReveal)
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(_effect, value);
    }
}
