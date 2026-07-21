using System.Collections;
using July.Arch;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;

namespace July.UI.Tests
{
    [TestFixture]
    public sealed class UIEventSystemLifecycleTests
    {
        private ArchContext _context;
        private GameObject _launchEventSystem;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            DestroyExistingUIInfrastructure();
            yield return null;

            _launchEventSystem = new GameObject(
                "LaunchEventSystem",
                typeof(EventSystem),
                typeof(StandaloneInputModule));

            _context = new ArchContext();
            _context.RegisterSystem(new UISystem());
            _context.InitializeAsync().GetAwaiter().GetResult();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            _context?.Shutdown();
            _context = null;

            if (_launchEventSystem != null)
                Object.Destroy(_launchEventSystem);
            _launchEventSystem = null;

            DestroyExistingUIInfrastructure();
            yield return null;
        }

        [UnityTest]
        public IEnumerator ExistingLaunchEventSystem_IsAdoptedByUISystem()
        {
            var launchEventSystem = _launchEventSystem.GetComponent<EventSystem>();
            var uiRoot = GameObject.Find("[UIRoot]");

            Assert.That(EventSystem.current, Is.SameAs(launchEventSystem));
            Assert.That(EventSystem.current.GetComponent<StandaloneInputModule>(), Is.Not.Null);
            Assert.That(EventSystem.current.transform.IsChildOf(uiRoot.transform), Is.True,
                "UISystem should adopt the launch scene EventSystem into its persistent UI root.");
            Assert.That(Object.FindObjectsOfType<EventSystem>(true), Has.Length.EqualTo(1));

            yield return null;

            Assert.That(EventSystem.current, Is.SameAs(launchEventSystem),
                "UISystem should keep using the original launch scene EventSystem.");
        }

        private static void DestroyExistingUIInfrastructure()
        {
            foreach (var eventSystem in Object.FindObjectsOfType<EventSystem>(true))
            {
                if (eventSystem != null)
                    Object.DestroyImmediate(eventSystem.gameObject);
            }

            DestroyByName("[UIRoot]");
            DestroyByName("[UI_Staging]");
            DestroyByName("[UI Mask]");
        }

        private static void DestroyByName(string objectName)
        {
            foreach (var transform in Object.FindObjectsOfType<Transform>(true))
            {
                if (transform != null && transform.name == objectName)
                    Object.DestroyImmediate(transform.gameObject);
            }
        }
    }
}
