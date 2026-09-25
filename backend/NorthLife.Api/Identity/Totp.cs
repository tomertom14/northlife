using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace NorthLife.Api.Identity;

/// <summary>
/// Time-based one-time passwords (RFC 6238) on top of HOTP (RFC 4226).
/// code = Truncate(HMAC-SHA1(secret, ⌊unixTime / 30⌋)) mod 10^digits.
/// </summary>
public static class Totp
{
    public const int StepSeconds = 30;
    public const int DefaultDigits = 6;
    public const int SecretBytes = 20;

    public static long StepAt(DateTimeOffset time) => time.ToUnixTimeSeconds() / StepSeconds;

    public static byte[] NewSecret() => RandomNumberGenerator.GetBytes(SecretBytes);

    /// <summary>HOTP value for one counter: dynamic truncation of the HMAC, RFC 4226 §5.3.</summary>
    public static string Compute(ReadOnlySpan<byte> secret, long step, int digits = DefaultDigits)
    {
        Span<byte> counter = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counter, step);
        Span<byte> hash = stackalloc byte[HMACSHA1.HashSizeInBytes];
        HMACSHA1.HashData(secret, counter, hash);

        var offset = hash[^1] & 0x0F;
        var binary =
            ((hash[offset] & 0x7F) << 24) |
            (hash[offset + 1] << 16) |
            (hash[offset + 2] << 8) |
            hash[offset + 3];
        var otp = binary % (int)Math.Pow(10, digits);
        return otp.ToString().PadLeft(digits, '0');
    }

    /// <summary>
    /// Accepts a code from the current step or one step either side (clock drift), but never a
    /// step at or before <paramref name="lastUsedStep"/>, so each code works once.
    /// Returns the matched step, or null.
    /// </summary>
    public static long? Verify(
        ReadOnlySpan<byte> secret,
        string code,
        DateTimeOffset now,
        long? lastUsedStep,
        int window = 1,
        int digits = DefaultDigits)
    {
        var normalized = code.Replace(" ", string.Empty);
        if (normalized.Length != digits || !normalized.All(char.IsAsciiDigit)) return null;

        var current = StepAt(now);
        var submitted = Encoding.ASCII.GetBytes(normalized);
        long? matched = null;
        // Check every step in the window so timing does not reveal which one matched.
        for (var step = current - window; step <= current + window; step++)
        {
            var expected = Encoding.ASCII.GetBytes(Compute(secret, step, digits));
            if (CryptographicOperations.FixedTimeEquals(expected, submitted) &&
                (lastUsedStep is null || step > lastUsedStep))
            {
                matched ??= step;
            }
        }

        return matched;
    }

    /// <summary>Key URI understood by Google Authenticator and compatible apps.</summary>
    public static string ProvisioningUri(string issuer, string account, ReadOnlySpan<byte> secret) =>
        $"otpauth://totp/{Uri.EscapeDataString(issuer)}:{Uri.EscapeDataString(account)}" +
        $"?secret={Base32.Encode(secret)}&issuer={Uri.EscapeDataString(issuer)}" +
        $"&algorithm=SHA1&digits={DefaultDigits}&period={StepSeconds}";
}
