using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace AttendanceBioAPI.Service.Helpers;

public class SecurityHelper
{
    private readonly IConfiguration _configuration;

    public SecurityHelper(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public string Encrypt(string plainText)
    {
        var key = _configuration["GeneralSettings:AesKey"];

        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException(
                "GeneralSettings:AesKey is not configured.");
        }

        var keyBytes = Encoding.UTF8.GetBytes(key);

        using var aes = Aes.Create();

        aes.Key = keyBytes;
        aes.IV = keyBytes;

        var plainTextBytes = Encoding.UTF8.GetBytes(plainText);

        using var memoryStream = new MemoryStream();

        using (var cryptoStream = new CryptoStream(
            memoryStream,
            aes.CreateEncryptor(),
            CryptoStreamMode.Write))
        {
            cryptoStream.Write(
                plainTextBytes,
                0,
                plainTextBytes.Length);

            cryptoStream.FlushFinalBlock();
        }

        return Convert.ToBase64String(
            memoryStream.ToArray());
    }

    public string Decrypt(string cipherText)
    {
        var key = _configuration["GeneralSettings:AesKey"];

        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException(
                "GeneralSettings:AesKey is not configured.");
        }

        if (cipherText.Contains(" "))
        {
            cipherText = cipherText
                .Trim()
                .Replace(" ", "+");
        }

        if (cipherText.Contains("%"))
        {
            cipherText = cipherText
                .Replace("%2F", "/");
        }

        var cipherTextBytes =
            Convert.FromBase64String(cipherText);

        var keyBytes =
            Encoding.UTF8.GetBytes(key);

        using var aes = Aes.Create();

        aes.Key = keyBytes;
        aes.IV = keyBytes;

        using var memoryStream =
            new MemoryStream(cipherTextBytes);

        using var cryptoStream = new CryptoStream(
            memoryStream,
            aes.CreateDecryptor(),
            CryptoStreamMode.Read);

        using var reader =
            new StreamReader(cryptoStream);

        return reader.ReadToEnd();
    }
}
