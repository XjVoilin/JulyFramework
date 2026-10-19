using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace July.UI.Tests
{
    public sealed class WebImageRequestTests
    {
        private GameObject _host;
        private WebImage _image;
        private readonly List<Exception> _exceptions = new();
        private readonly List<string> _warnings = new();

        [SetUp]
        public void SetUp()
        {
            _exceptions.Clear();
            _warnings.Clear();
            WebImage.ClearCache();
            UniTaskScheduler.UnobservedTaskException += OnException;
            Application.logMessageReceived += OnLog;
            _host = new GameObject("WebImageTest", typeof(RectTransform), typeof(Image), typeof(WebImage));
            _image = _host.GetComponent<WebImage>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_host != null) Object.DestroyImmediate(_host);
            WebImage.ClearCache();
            UniTaskScheduler.UnobservedTaskException -= OnException;
            Application.logMessageReceived -= OnLog;
        }

        [UnityTest]
        public IEnumerator HttpFailure_IsHandledAsWarning()
        {
            using var server = new ImageServer(503, Array.Empty<byte>());
            _image.Load(server.Url);
            yield return WaitFor(() => _warnings.Count > 0 || _exceptions.Count > 0);
            Assert.That(_exceptions, Is.Empty, "A failed image request must not escape to UniTask's global exception handler.");
            Assert.That(_warnings, Has.Count.EqualTo(1));
            Assert.That(_warnings[0], Does.Contain(server.Url).And.Contain("503"));
            Assert.That(_host.GetComponent<Image>().overrideSprite, Is.Null);
        }

        [UnityTest]
        public IEnumerator SuccessfulDownload_AssignsSpriteAndCachesIt()
        {
            var texture = new Texture2D(2, 2);
            var png = texture.EncodeToPNG();
            Object.DestroyImmediate(texture);
            using var server = new ImageServer(200, png);
            _image.Load(server.Url);
            yield return WaitFor(() => _host.GetComponent<Image>().overrideSprite != null || _exceptions.Count > 0);
            var sprite = _host.GetComponent<Image>().overrideSprite;
            Assert.That(sprite, Is.Not.Null);
            _image.Clear();
            _image.Load(server.Url);
            Assert.That(_host.GetComponent<Image>().overrideSprite, Is.SameAs(sprite));
            Assert.That(_exceptions, Is.Empty);
            Assert.That(_warnings, Is.Empty);
        }

        [UnityTest]
        public IEnumerator ClearDuringDownload_CancelsWithoutWarningOrException()
        {
            using var server = new ImageServer(503, Array.Empty<byte>(), 300);
            _image.Load(server.Url);
            yield return WaitFor(() => server.Accepted);
            _image.Clear();
            yield return WaitFor(() => server.Finished);
            yield return null;
            Assert.That(_image.CurrentUrl, Is.Null);
            Assert.That(_host.GetComponent<Image>().overrideSprite, Is.Null);
            Assert.That(_exceptions, Is.Empty);
            Assert.That(_warnings, Is.Empty);
        }

        [UnityTest]
        public IEnumerator DestroyDuringDownload_CancelsWithoutWarningOrException()
        {
            using var server = new ImageServer(503, Array.Empty<byte>(), 300);
            _image.Load(server.Url);
            yield return WaitFor(() => server.Accepted);
            Object.DestroyImmediate(_host);
            yield return WaitFor(() => server.Finished);
            yield return null;
            Assert.That(_exceptions, Is.Empty);
            Assert.That(_warnings, Is.Empty);
        }

        private static IEnumerator WaitFor(Func<bool> condition)
        {
            var deadline = Time.realtimeSinceStartup + 10;
            while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(condition(), Is.True, "Timed out waiting for the local image request.");
        }

        private void OnException(Exception exception) => _exceptions.Add(exception);
        private void OnLog(string message, string stack, LogType type)
        {
            if (type == LogType.Warning && message.StartsWith("[WebImage]")) _warnings.Add(message);
        }

        private sealed class ImageServer : IDisposable
        {
            private readonly TcpListener _listener;
            private int _accepted;
            private int _finished;
            public string Url { get; }
            public bool Accepted => Volatile.Read(ref _accepted) != 0;
            public bool Finished => Volatile.Read(ref _finished) != 0;

            public ImageServer(int status, byte[] body, int delay = 0)
            {
                _listener = new TcpListener(IPAddress.Loopback, 0);
                _listener.Start();
                Url = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/avatar.png";
                Task.Run(async () =>
                {
                    try
                    {
                        using var client = await _listener.AcceptTcpClientAsync();
                        using var stream = client.GetStream();
                        using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true);
                        while (!string.IsNullOrEmpty(await reader.ReadLineAsync())) { }
                        Volatile.Write(ref _accepted, 1);
                        if (delay > 0) await Task.Delay(delay);
                        var header = Encoding.ASCII.GetBytes($"HTTP/1.1 {status} Test\r\nContent-Type: image/png\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
                        await stream.WriteAsync(header, 0, header.Length);
                        if (body.Length > 0) await stream.WriteAsync(body, 0, body.Length);
                    }
                    catch (IOException) { } // Client cancellation may close the socket before the response.
                    catch (SocketException) { } // Fixture disposal may stop an outstanding accept.
                    catch (ObjectDisposedException) { }
                    finally { Volatile.Write(ref _finished, 1); }
                });
            }

            public void Dispose() => _listener.Stop();
        }
    }
}
