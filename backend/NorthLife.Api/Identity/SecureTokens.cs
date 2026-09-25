using System.Security.Cryptography;
using System.Text;

namespace NorthLife.Api.Identity;

/// <summary>Random secrets for emailed links and backup codes; only their hashes are stored.</summary>
public static class SecureTokens
{
    private const string CodeAlphabet = "abcdefghjkmnpqrstuvwxyz23456789";

    /// <summary>256-bit URL-safe token and the SHA-256 hash to store.</summary>
    public static (string Token, string Hash) NewLinkToken()
    {
        var token = Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        return (token, Hash(token));
    }

    /// <summary>A backup code such as "k7m2-q9xa" (about 40 bits) and its hash.</summary>
    public static (string Code, string Hash) NewRecoveryCode()
    {
        var chars = new char[8];
        for (var index = 0; index < chars.Length; index++)
        {
            chars[index] = CodeAlphabet[RandomNumberGenerator.GetInt32(CodeAlphabet.Length)];
        }

        var code = $"{new string(chars, 0, 4)}-{new string(chars, 4, 4)}";
        return (code, Hash(NormalizeRecoveryCode(code)));
    }

    public static string NormalizeRecoveryCode(string code) =>
        code.Trim().Replace("-", string.Empty).Replace(" ", string.Empty).ToLowerInvariant();

    public static string Hash(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
