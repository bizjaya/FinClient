using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Fin;

/// <summary>Verifies FinCopy webhook signatures.</summary>
public static class FinWebhook
{
    private static readonly TimeSpan AllowedSkew = TimeSpan.FromMinutes(5);

    /// <summary>Checks the timestamped HMAC-SHA256 signature over the exact UTF-8 request body.</summary>
    /// <param name="secret">The webhook secret returned when the webhook is configured.</param>
    /// <param name="timestamp">The X-FinCopy-Timestamp Unix-seconds header.</param>
    /// <param name="body">The raw request body as received, before JSON parsing.</param>
    /// <param name="signature">The X-FinCopy-Signature header value.</param>
    public static bool Verify(string secret, string timestamp, string body, string signature) =>
        Verify(secret, timestamp, body, signature, DateTimeOffset.UtcNow);

    internal static bool Verify(string secret, string timestamp, string body, string signature, DateTimeOffset now)
    {
        if (string.IsNullOrEmpty(secret) || body is null || string.IsNullOrEmpty(signature)
            || !long.TryParse(timestamp, NumberStyles.None, CultureInfo.InvariantCulture, out var unixSeconds)) return false;
        DateTimeOffset sentAt;
        try { sentAt = DateTimeOffset.FromUnixTimeSeconds(unixSeconds); }
        catch (ArgumentOutOfRangeException) { return false; }
        if (now - sentAt > AllowedSkew || sentAt - now > AllowedSkew) return false;

        var suppliedHex = signature.StartsWith("sha256=", StringComparison.Ordinal) ? signature.Substring(7) : signature;
        if (!TryHex(suppliedHex, out var supplied)) return false;
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var expected = hmac.ComputeHash(Encoding.UTF8.GetBytes(timestamp + "." + body));
        if (expected.Length != supplied.Length) return false;
        var difference = 0;
        for (var i = 0; i < expected.Length; i++) difference |= expected[i] ^ supplied[i];
        return difference == 0;
    }

    private static bool TryHex(string value, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();
        if (value.Length != 64) return false;
        var result = new byte[32];
        for (var i = 0; i < result.Length; i++)
        {
            var high = HexValue(value[i * 2]);
            var low = HexValue(value[i * 2 + 1]);
            if (high < 0 || low < 0) return false;
            result[i] = (byte)((high << 4) | low);
        }
        bytes = result;
        return true;
    }

    private static int HexValue(char value) => value switch
    {
        >= '0' and <= '9' => value - '0',
        >= 'a' and <= 'f' => value - 'a' + 10,
        >= 'A' and <= 'F' => value - 'A' + 10,
        _ => -1
    };
}
