using July.Arch;

namespace July.Persistence
{
    public class NoEncryptionSystem : SystemBase, IEncryptionSystem
    {
        public byte[] Encrypt(byte[] data) => data;
        public byte[] Decrypt(byte[] encryptedData) => encryptedData;
    }
}
