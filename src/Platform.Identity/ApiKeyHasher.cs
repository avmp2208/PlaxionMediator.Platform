using System.Security.Cryptography;
using System.Text;

namespace Platform.Identity;

/// <summary>
/// Hashes API keys with SHA-256 (keys are already high-entropy random
/// secrets, not user-chosen passwords, so a slow KDF like PBKDF2/bcrypt is
/// not required - a fast, constant-time-compared cryptographic hash is
/// standard practice for API key storage) and compares hashes in constant
/// time to avoid timing side-channels.
/// </summary>
public static class ApiKeyHasher
{
    public static string Hash(string plaintextKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plaintextKey);
        var bytes = Encoding.UTF8.GetBytes(plaintextKey);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }

    public static bool Matches(string plaintextKey, string hashedKey)
    {
        if (string.IsNullOrEmpty(plaintextKey) || string.IsNullOrEmpty(hashedKey))
        {
            return false;
        }

        var candidateHash = Hash(plaintextKey);
        var candidateBytes = Encoding.UTF8.GetBytes(candidateHash);
        var storedBytes = Encoding.UTF8.GetBytes(hashedKey);

        if (candidateBytes.Length != storedBytes.Length)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(candidateBytes, storedBytes);
    }

    /// <summary>Generates a new high-entropy plaintext API key.</summary>
    public static string GenerateKey()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return "pk_" + Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
