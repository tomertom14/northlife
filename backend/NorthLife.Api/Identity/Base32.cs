using System.Text;

namespace NorthLife.Api.Identity;

/// <summary>RFC 4648 Base32 (alphabet A–Z, 2–7), the encoding authenticator apps expect for secrets.</summary>
public static class Base32
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    /// <summary>Encodes without padding, as used in otpauth:// URIs.</summary>
    public static string Encode(ReadOnlySpan<byte> data)
    {
        var output = new StringBuilder((data.Length * 8 + 4) / 5);
        int buffer = 0, bits = 0;
        foreach (var value in data)
        {
            buffer = (buffer << 8) | value;
            bits += 8;
            while (bits >= 5)
            {
                output.Append(Alphabet[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }

        if (bits > 0) output.Append(Alphabet[(buffer << (5 - bits)) & 31]);
        return output.ToString();
    }

    /// <summary>Decodes case-insensitively, ignoring padding, spaces and hyphens.</summary>
    public static byte[] Decode(string text)
    {
        var output = new List<byte>(text.Length * 5 / 8);
        int buffer = 0, bits = 0;
        foreach (var character in text)
        {
            if (character is '=' or ' ' or '-') continue;
            var index = Alphabet.IndexOf(char.ToUpperInvariant(character));
            if (index < 0) throw new FormatException($"'{character}' is not a Base32 character.");
            buffer = (buffer << 5) | index;
            bits += 5;
            if (bits >= 8)
            {
                output.Add((byte)((buffer >> (bits - 8)) & 0xFF));
                bits -= 8;
            }
        }

        return [.. output];
    }
}
