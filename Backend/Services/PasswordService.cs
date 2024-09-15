using Isopoh.Cryptography.Argon2;
using Isopoh.Cryptography.SecureArray;
using System.Security.Cryptography;
using System.Text;

public interface IPasswordService
{
    string HashPassword(string password, out byte[] saltBytes);
    bool VerifyPassword(string hashString, byte[] saltBytes, string password);
}

public class PasswordService : IPasswordService
{
    public string HashPassword(string password, out byte[] saltBytes)
    {
        saltBytes = new byte[16];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(saltBytes);
        }


        var config = new Argon2Config
        {
            Type = Argon2Type.DataIndependentAddressing,
            Version = Argon2Version.Nineteen,
            TimeCost = 10,
            MemoryCost = 32768,
            Lanes = 5,
            Threads = Environment.ProcessorCount,
            Password = Encoding.UTF8.GetBytes(password),
            Salt = saltBytes,
            HashLength = 20,
        };
        var argon2A = new Argon2(config);
        using (SecureArray<byte> hashA = argon2A.Hash())
        {
            var hashString = config.EncodeString(hashA.Buffer);
            return hashString;
        }
    }

    public bool VerifyPassword(string hashString, byte[] saltBytes, string password)
    {
        var configOfPasswordToVerify = new Argon2Config
        {
            Password = Encoding.UTF8.GetBytes(password),
            Threads = Environment.ProcessorCount,
            Salt = saltBytes
        };

        SecureArray<byte>? hashB = null;
        try
        {
            if (configOfPasswordToVerify.DecodeString(hashString, out hashB) && hashB != null)
            {
                var argon2ToVerify = new Argon2(configOfPasswordToVerify);
                using SecureArray<byte>? hashToVerify = argon2ToVerify.Hash();

                return Argon2.FixedTimeEquals(hashB, hashToVerify);
            }

            return false;
        }
        finally
        {
            hashB?.Dispose();
        }
    }
}

