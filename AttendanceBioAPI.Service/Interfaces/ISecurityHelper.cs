namespace AttendanceBioAPI.Service.Interfaces
{
    public interface ISecurityHelper : IAccount
    {
        string Decrypt(string cipherText);
        string EncryptionAlgorithm(string plainText);
    }
}

