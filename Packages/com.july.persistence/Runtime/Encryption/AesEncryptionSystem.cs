using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Cysharp.Threading.Tasks;
using July.Arch;
using July.Logging;
using UnityEngine;

namespace July.Persistence
{
    public class AesEncryptionSystem : SystemBase, IEncryptionSystem
    {
        private byte[] _key;
        private byte[] _iv;

        private string _configKey;

        public AesEncryptionSystem(string encryptionKey = null)
        {
            _configKey = encryptionKey;
        }

        protected override UniTask OnInitializeAsync()
        {
            var rawKey = _configKey ?? "JulyGF_Default_Encryption_Key_32Bytes!!";
            if (string.IsNullOrEmpty(_configKey))
                JLogger.LogWarning("[AesEncryption] Using default key — configure a custom key for production");

            var keyBytes = Encoding.UTF8.GetBytes(rawKey);
            using var sha256 = SHA256.Create();
            _key = sha256.ComputeHash(keyBytes);

            _iv = new byte[16];
            Array.Copy(_key, 0, _iv, 0, 16);
            return UniTask.CompletedTask;
        }

        public byte[] Encrypt(byte[] data)
        {
            if (data == null || data.Length == 0) return Array.Empty<byte>();

            try
            {
                using var aes = Aes.Create();
                aes.Key = _key;
                aes.IV = _iv;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                using var encryptor = aes.CreateEncryptor();
                using var ms = new MemoryStream();
                using var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write);
                cs.Write(data, 0, data.Length);
                cs.FlushFinalBlock();
                return ms.ToArray();
            }
            catch (Exception ex)
            {
                JLogger.LogError($"[AesEncryption] Encrypt failed: {ex.Message}");
                return null;
            }
        }

        public byte[] Decrypt(byte[] encryptedData)
        {
            if (encryptedData == null || encryptedData.Length == 0) return Array.Empty<byte>();

            try
            {
                using var aes = Aes.Create();
                aes.Key = _key;
                aes.IV = _iv;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                using var decryptor = aes.CreateDecryptor();
                using var ms = new MemoryStream(encryptedData);
                using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
                using var result = new MemoryStream();
                cs.CopyTo(result);
                return result.ToArray();
            }
            catch (Exception ex)
            {
                JLogger.LogError($"[AesEncryption] Decrypt failed: {ex.Message}");
                return null;
            }
        }
    }
}
