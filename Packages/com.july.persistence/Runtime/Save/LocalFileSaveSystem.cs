using System;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using July.Logging;
using UnityEngine;

namespace July.Persistence
{
    /// <summary>
    /// 文件存储实现 — 搬运自 JulyCore LocalFileSaveProvider + SaveProviderBase。
    /// 特性：文件 IO、序列化+加密、失败重试、自动备份/恢复。
    /// </summary>
    public class LocalFileSaveSystem : SaveSystem
    {
        private const int MaxRetryCount = 3;
        private const int RetryDelayMs = 100;

        private string _saveRootPath;
        private string _backupRootPath;
        private bool _pathsInitialized;

        #region Lifecycle

        protected override UniTask OnInitializeAsync()
        {
            var initTask = base.OnInitializeAsync();
            EnsurePathsInitialized();
            return initTask;
        }

        private void EnsurePathsInitialized()
        {
            if (_pathsInitialized) return;
            _pathsInitialized = true;

            _saveRootPath = Path.Combine(Application.persistentDataPath, "Save");
            _backupRootPath = Path.Combine(Application.persistentDataPath, "Save", "Backup");

            if (!Directory.Exists(_saveRootPath))
                Directory.CreateDirectory(_saveRootPath);

            if (!Directory.Exists(_backupRootPath))
                Directory.CreateDirectory(_backupRootPath);
        }

        #endregion

        #region Abstract IO Implementation

        protected override async UniTask<bool> WriteDataAsync(string key, byte[] data, CancellationToken ct)
        {
            EnsurePathsInitialized();

            var filePath = GetSavePath(key);
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            await File.WriteAllBytesAsync(filePath, data, ct);
            return true;
        }

        protected override async UniTask<byte[]> ReadDataAsync(string key, CancellationToken ct)
        {
            EnsurePathsInitialized();

            var filePath = GetSavePath(key);
            if (!File.Exists(filePath))
                return null;

            return await File.ReadAllBytesAsync(filePath, ct);
        }

        protected override bool DataExists(string key)
        {
            EnsurePathsInitialized();

            var filePath = GetSavePath(key);
            return File.Exists(filePath);
        }

        protected override bool DeleteData(string key)
        {
            EnsurePathsInitialized();

            var filePath = GetSavePath(key);
            if (!File.Exists(filePath)) return false;

            File.Delete(filePath);
            return true;
        }

        public override string GetSavePath(string key)
        {
            EnsurePathsInitialized();

            if (string.IsNullOrEmpty(key))
                throw new ArgumentException("存档key不能为空", nameof(key));

            var invalidChars = Path.GetInvalidFileNameChars();
            var segments = key.Split('/');
            for (var i = 0; i < segments.Length; i++)
            {
                segments[i] = string.Join("_", segments[i].Split(invalidChars));
            }

            var relativePath = Path.Combine(segments);
            return Path.Combine(_saveRootPath, $"{relativePath}.dat");
        }

        #endregion

        #region Retry + Backup Override

        protected override async UniTask<SaveResult> SaveWithRetryAsync(
            string key, byte[] processedData, CancellationToken ct)
        {
            var backupPath = await BackupSaveDataAsync(key, ct);

            Exception lastException = null;
            for (int attempt = 0; attempt < MaxRetryCount; attempt++)
            {
                if (ct.IsCancellationRequested)
                {
                    await RestoreBackupAsync(key, backupPath, ct);
                    return SaveResult.CreateFailure(SaveFailureReason.Cancelled);
                }

                try
                {
                    var success = await WriteDataAsync(key, processedData, ct);
                    if (success)
                    {
                        DeleteBackup(backupPath);
                        return SaveResult.CreateSuccess();
                    }
                }
                catch (OperationCanceledException)
                {
                    await RestoreBackupAsync(key, backupPath, ct);
                    return SaveResult.CreateFailure(SaveFailureReason.Cancelled);
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    JLogger.LogWarning(
                        $"[LocalFileSaveSystem] 保存失败 (尝试 {attempt + 1}/{MaxRetryCount}): {key}, 错误: {ex.Message}");
                }

                if (attempt < MaxRetryCount - 1)
                {
                    await UniTask.Delay(RetryDelayMs * (attempt + 1), cancellationToken: ct);
                }
            }

            await RestoreBackupAsync(key, backupPath, ct);

            var reason = lastException != null ? ClassifyException(lastException) : SaveFailureReason.Unknown;
            return SaveResult.CreateFailure(reason, lastException?.Message);
        }

        #endregion

        #region Backup

        private async UniTask<string> BackupSaveDataAsync(string key, CancellationToken ct)
        {
            try
            {
                var filePath = GetSavePath(key);
                if (!File.Exists(filePath))
                    return null;

                var backupPath = Path.Combine(_backupRootPath, $"{Path.GetFileName(filePath)}.bak");
                var backupDir = Path.GetDirectoryName(backupPath);
                if (!string.IsNullOrEmpty(backupDir) && !Directory.Exists(backupDir))
                    Directory.CreateDirectory(backupDir);

                var bytes = await File.ReadAllBytesAsync(filePath, ct);
                await File.WriteAllBytesAsync(backupPath, bytes, ct);
                return backupPath;
            }
            catch (Exception ex)
            {
                JLogger.LogWarning($"[LocalFileSaveSystem] 备份失败: {key}, 错误: {ex.Message}");
                return null;
            }
        }

        private async UniTask RestoreBackupAsync(string key, string backupPath, CancellationToken ct)
        {
            try
            {
                if (string.IsNullOrEmpty(backupPath) || !File.Exists(backupPath))
                    return;

                var filePath = GetSavePath(key);
                var directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                var bytes = await File.ReadAllBytesAsync(backupPath, ct);
                await File.WriteAllBytesAsync(filePath, bytes, ct);
            }
            catch (Exception ex)
            {
                JLogger.LogError($"[LocalFileSaveSystem] 恢复备份失败: {key}, 错误: {ex.Message}");
            }
        }

        private void DeleteBackup(string backupPath)
        {
            try
            {
                if (string.IsNullOrEmpty(backupPath) || !File.Exists(backupPath))
                    return;

                File.Delete(backupPath);
            }
            catch (Exception ex)
            {
                JLogger.LogWarning($"[LocalFileSaveSystem] 删除备份失败: {backupPath}, 错误: {ex.Message}");
            }
        }

        #endregion

        #region Exception Classification

        private static SaveFailureReason ClassifyException(Exception ex)
        {
            if (ex is OperationCanceledException)
                return SaveFailureReason.Cancelled;

            if (ex is UnauthorizedAccessException || ex is System.Security.SecurityException)
                return SaveFailureReason.PermissionDenied;

            if (ex is IOException ioEx)
            {
                var errorCode = System.Runtime.InteropServices.Marshal.GetHRForException(ioEx);
                if (errorCode == unchecked((int)0x80070070) || errorCode == unchecked((int)0x80070027))
                    return SaveFailureReason.DiskFull;

                if (errorCode == unchecked((int)0x80070020))
                    return SaveFailureReason.FileInUse;
            }

            if (ex is IOException || ex is SystemException)
                return SaveFailureReason.DeviceError;

            return SaveFailureReason.Unknown;
        }

        #endregion
    }
}
