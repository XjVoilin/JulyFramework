namespace July.Persistence
{
    public interface IEncryptionSystem
    {
        byte[] Encrypt(byte[] data);
        byte[] Decrypt(byte[] encryptedData);
    }
}
