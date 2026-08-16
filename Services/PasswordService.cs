using System.Security.Cryptography;

namespace CommunityFootballClubManager.Services;

public sealed record PasswordDigest(string Hash, string Salt, int Iterations);

public sealed class PasswordService
{
    public const int DefaultIterations = 210_000;

    public PasswordDigest Hash(string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        var salt = RandomNumberGenerator.GetBytes(24);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            DefaultIterations,
            HashAlgorithmName.SHA256,
            32);

        return new PasswordDigest(
            Convert.ToBase64String(hash),
            Convert.ToBase64String(salt),
            DefaultIterations);
    }

    public bool Verify(string password, string expectedHash, string salt, int iterations)
    {
        if (string.IsNullOrWhiteSpace(password)
            || string.IsNullOrWhiteSpace(expectedHash)
            || string.IsNullOrWhiteSpace(salt))
        {
            return false;
        }

        try
        {
            var expectedBytes = Convert.FromBase64String(expectedHash);
            var actualBytes = Rfc2898DeriveBytes.Pbkdf2(
                password,
                Convert.FromBase64String(salt),
                iterations,
                HashAlgorithmName.SHA256,
                expectedBytes.Length);

            return CryptographicOperations.FixedTimeEquals(actualBytes, expectedBytes);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public static string Validate(string password)
    {
        if (password.Length < 8)
        {
            return "Mật khẩu phải có ít nhất 8 ký tự.";
        }

        if (!password.Any(char.IsUpper)
            || !password.Any(char.IsLower)
            || !password.Any(char.IsDigit)
            || password.All(char.IsLetterOrDigit))
        {
            return "Mật khẩu cần có chữ hoa, chữ thường, số và ký tự đặc biệt.";
        }

        return string.Empty;
    }
}
