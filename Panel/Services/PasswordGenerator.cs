using System.Security.Cryptography;

namespace RealEstate.Panel.Services;

public static class PasswordGenerator
{
    /// <summary>12 caracteres con mayúsculas, minúsculas y números (sin caracteres ambiguos como 0/O o 1/l).</summary>
    public static string Generate()
    {
        const string upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
        const string lower = "abcdefghijkmnpqrstuvwxyz";
        const string digits = "23456789";

        var chars = new List<char>(12);
        for (var i = 0; i < 4; i++) chars.Add(upper[RandomNumberGenerator.GetInt32(upper.Length)]);
        for (var i = 0; i < 4; i++) chars.Add(lower[RandomNumberGenerator.GetInt32(lower.Length)]);
        for (var i = 0; i < 4; i++) chars.Add(digits[RandomNumberGenerator.GetInt32(digits.Length)]);

        for (var i = chars.Count - 1; i > 0; i--)   // Fisher–Yates
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }

        return new string(chars.ToArray());
    }
}
