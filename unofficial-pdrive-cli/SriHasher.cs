using System.Security.Cryptography;

namespace unofficial_pdrive_cli;

public static class SriHasher
{
    public static SHA256 CreateHashAlgo()
    {
        return SHA256.Create();
    }

    public static string GetHash(SHA256 hashAlgo)
    {
        var hash = hashAlgo.Hash ?? throw new InvalidOperationException("null hash");
        return FormatHash(hash);
    }

    public static string FormatHash(byte[] hash)
    {
        return "sha256-" + Convert.ToBase64String(hash);
    }
}
