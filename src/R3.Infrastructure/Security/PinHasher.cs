using System.Security.Cryptography;

namespace R3.Infrastructure.Security;

internal static class PinHasher
{
    private const string Prefix = "R3PIN";
    private const int MinimumIterations = 100_000;
    private const int HashSize = 32;

    public static bool Verify(string pin, string encodedHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pin);
        ArgumentException.ThrowIfNullOrWhiteSpace(encodedHash);

        string[] parts = encodedHash.Split('$');
        if (parts.Length != 4 ||
            !string.Equals(parts[0], Prefix, StringComparison.Ordinal) ||
            !int.TryParse(parts[1], out int iterations) ||
            iterations < MinimumIterations)
        {
            return false;
        }

        try
        {
            byte[] salt = Convert.FromBase64String(parts[2]);
            byte[] expected = Convert.FromBase64String(parts[3]);
            byte[] actual = Rfc2898DeriveBytes.Pbkdf2(
                pin,
                salt,
                iterations,
                HashAlgorithmName.SHA256,
                HashSize);

            return expected.Length == actual.Length &&
                   CryptographicOperations.FixedTimeEquals(expected, actual);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
