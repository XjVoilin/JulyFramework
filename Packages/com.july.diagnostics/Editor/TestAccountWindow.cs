using System;
using UnityEditor;
using UnityEngine;

namespace July.Diagnostics.Editor
{
    internal sealed class TestAccountWindow : EditorWindow
    {
        private string _customId = string.Empty;
        private Vector2 _scrollPosition;

        [MenuItem("JulyGF/工具/测试账号 UUID", priority = 100)]
        private static void Open()
        {
            var window = GetWindow<TestAccountWindow>("测试账号 UUID");
            window.minSize = new Vector2(620f, 420f);
        }

        private void OnEnable() => EditorApplication.playModeStateChanged += OnPlayModeStateChanged;

        private void OnDisable() => EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;

        private void OnPlayModeStateChanged(PlayModeStateChange state) => Repaint();

        private void OnGUI()
        {
            var readOnly = EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode;
            var currentId = EditorTestDeviceId.GetDeviceId();
            var hasOverride = !string.IsNullOrEmpty(EditorTestAccountStore.Override);

            EditorGUILayout.HelpBox("设置仅对接入 July 测试设备 ID 的项目生效，切换后在下次启动游戏并登录时使用。",
                MessageType.Info);
            if (readOnly)
                EditorGUILayout.HelpBox("请退出 Play Mode 后修改测试账号。", MessageType.Warning);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel(hasOverride ? "当前账号（已覆盖）" : "当前账号（本机默认）");
                DrawCopyableId(currentId);
            }

            using (new EditorGUI.DisabledScope(readOnly))
            {
                DrawSwitchActions(hasOverride);
            }

            EditorGUILayout.Space();
            DrawHistory(currentId, readOnly);
        }

        private void DrawSwitchActions(bool hasOverride)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                _customId = EditorGUILayout.TextField("自定义 ID", _customId);
                using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_customId)))
                {
                    if (GUILayout.Button("切换", GUILayout.Width(60f)))
                    {
                        SwitchTo(_customId.Trim());
                        _customId = string.Empty;
                        GUI.FocusControl(null);
                    }
                }
            }

            if (GUILayout.Button("生成并切换到新账号"))
                SwitchTo(Guid.NewGuid().ToString());

            using (new EditorGUI.DisabledScope(!hasOverride))
            {
                if (GUILayout.Button("恢复本机默认账号"))
                {
                    EditorTestAccountStore.ClearOverride();
                    ShowNotification(new GUIContent("已恢复本机默认账号"));
                }
            }
        }

        private void DrawHistory(string currentId, bool readOnly)
        {
            EditorGUILayout.LabelField("历史账号", EditorStyles.boldLabel);
            using var scroll = new EditorGUILayout.ScrollViewScope(_scrollPosition);
            _scrollPosition = scroll.scrollPosition;

            string toSwitch = null;
            var toRemove = -1;
            var accounts = EditorTestAccountStore.History;
            for (var index = 0; index < accounts.Count; index++)
            {
                var entry = accounts[index];
                using var row = new EditorGUILayout.HorizontalScope(EditorStyles.helpBox);
                var isCurrent = string.Equals(entry.DeviceId, currentId, StringComparison.Ordinal);
                EditorGUILayout.LabelField(isCurrent ? "当前" : string.Empty, GUILayout.Width(30f));
                DrawCopyableId(entry.DeviceId);

                using (new EditorGUI.DisabledScope(readOnly))
                {
                    EditorGUI.BeginChangeCheck();
                    EditorGUILayout.LabelField("备注", GUILayout.Width(30f));
                    var note = EditorGUILayout.TextField(entry.Note ?? string.Empty);
                    if (EditorGUI.EndChangeCheck())
                        EditorTestAccountStore.SetNote(index, note);

                    using (new EditorGUI.DisabledScope(isCurrent))
                    {
                        if (GUILayout.Button("切换", GUILayout.Width(46f)))
                            toSwitch = entry.DeviceId;
                    }

                    if (GUILayout.Button("删除", GUILayout.Width(46f)))
                        toRemove = index;
                }
            }

            if (toSwitch != null)
                SwitchTo(toSwitch);
            else if (toRemove >= 0)
                EditorTestAccountStore.RemoveHistoryEntry(toRemove);
        }

        private void DrawCopyableId(string deviceId)
        {
            EditorGUILayout.SelectableLabel(deviceId, EditorStyles.textField,
                GUILayout.Height(EditorGUIUtility.singleLineHeight));
            if (GUILayout.Button("复制", GUILayout.Width(46f)))
            {
                EditorGUIUtility.systemCopyBuffer = deviceId;
                ShowNotification(new GUIContent("UUID 已复制"));
            }
        }

        private void SwitchTo(string deviceId)
        {
            EditorTestAccountStore.SwitchTo(deviceId);
            ShowNotification(new GUIContent("已切换测试账号"));
        }
    }
}
