using System.Text;

namespace P2pCopy.Net.Pairing;

/// <summary>
/// Crockford base32: no padding, case-insensitive, and the lookalikes O/0, I/L/1 decode
/// to the same values, so a code can be read over the phone.
/// </summary>
internal static class Base32Crockford
{
    private const string Alphabet = "0123456789abcdefghjkmnpqrstvwxyz";

    public static string Encode(ReadOnlySpan<byte> data)
    {
        var sb = new StringBuilder((data.Length * 8 + 4) / 5);
        var buffer = 0;
        var bits = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                bits -= 5;
                sb.Append(Alphabet[(buffer >> bits) & 31]);
            }
        }
        if (bits > 0)
            sb.Append(Alphabet[(buffer << (5 - bits)) & 31]);
        return sb.ToString();
    }

    public static bool TryDecode(string text, out byte[] data)
    {
        data = Array.Empty<byte>();
        var values = new List<int>(text.Length);
        foreach (var raw in text)
        {
            if (raw == '-' || raw == ' ' || raw == '\t' || raw == '\r' || raw == '\n' || raw == '_' || raw == '.') continue;
            var c = char.ToLowerInvariant(raw);
            c = c switch
            {
                'o' => '0',
                'i' or 'l' => '1',
                _ => c,
            };
            var v = Alphabet.IndexOf(c);
            if (v < 0) return false;
            values.Add(v);
        }

        var result = new List<byte>(values.Count * 5 / 8);
        var buffer = 0;
        var bits = 0;
        foreach (var v in values)
        {
            buffer = (buffer << 5) | v;
            bits += 5;
            if (bits >= 8)
            {
                bits -= 8;
                result.Add((byte)((buffer >> bits) & 0xFF));
            }
        }
        data = result.ToArray();
        return true;
    }
}
