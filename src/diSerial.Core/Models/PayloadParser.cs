using System.Globalization;
using System.Text;

namespace DiSerial.Core.Models;

// Parse outgoing payload bytes.
public static class PayloadParser
{
    // Normalize hexadecimal input.
    public static string StripSeparatorsAndHexPrefixes(string input)
    {
        var cleaned = new StringBuilder(input.Length);

        var atTokenStart = true;

        for (var i = 0; i < input.Length; i++)
        {
            var c = input[i];

            if (c is ' ' or '-' or ',')
            {
                atTokenStart = true;
                continue;
            }

            if (atTokenStart
                && c == '0'
                && i + 1 < input.Length
                && input[i + 1] is 'x' or 'X')
            {
                i++;
                atTokenStart = false;
                continue;
            }

            cleaned.Append(c);
            atTokenStart = false;
        }

        return cleaned.ToString();
    }

    // Parse a payload into bytes.
    public static bool TryParse(
        string input, bool isHex, LineEnding lineEnding, out byte[] payload)
    {
        payload = [];

        byte[] body;
        if (isHex)
        {
            var cleaned = StripSeparatorsAndHexPrefixes(input);
            if (cleaned.Length == 0 || cleaned.Length % 2 != 0) return false;

            body = new byte[cleaned.Length / 2];
            for (var i = 0; i < body.Length; i++)
            {
                if (!byte.TryParse(
                        cleaned.AsSpan(i * 2, 2), NumberStyles.HexNumber,
                        CultureInfo.InvariantCulture, out body[i]))
                {
                    return false;
                }
            }
        }
        else
        {
            foreach (var c in input)
            {
                if (char.IsAscii(c)) continue;

                return false;
            }

            body = Encoding.ASCII.GetBytes(input);
        }

        var suffix = lineEnding switch
        {
            LineEnding.Cr => "\r"u8.ToArray(),
            LineEnding.Lf => "\n"u8.ToArray(),
            LineEnding.CrLf => "\r\n"u8.ToArray(),
            _ => []
        };

        payload = suffix.Length == 0 ? body : [.. body, .. suffix];
        return true;
    }
}
