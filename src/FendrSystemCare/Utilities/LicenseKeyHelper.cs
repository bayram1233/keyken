using System.Security.Cryptography;
using System.Text;

namespace FendrSystemCare.Utilities;

/// <summary>
/// API anahtarı üretimi ve doğrulaması. Web sitesi ile aynı algoritmayı kullanır.
/// </summary>
public static class LicenseKeyHelper
{
    /// <summary>Sahip için doğrudan giriş anahtarı (reklam atlanır).</summary>
    public const string MasterBypassKey = "FENDR-FENDR-MASTER-KEY";

    private const string Prefix = "FENDR";
    private const string Secret = "FendrSystemCare2025!";

    /// <summary>Anahtarın geçerli olup olmadığını kontrol eder.</summary>
    public static bool IsValid(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return false;
        key = key.Trim().ToUpperInvariant();

        if (key == MasterBypassKey) return true;

        // Format: FENDR-XXXXXXXX-CCCC (8 hex + 4 checksum)
        var parts = key.Split('-');
        if (parts.Length != 3) return false;
        if (!parts[0].Equals(Prefix, StringComparison.OrdinalIgnoreCase)) return false;
        if (parts[1].Length != 8) return false;
        if (parts[2].Length != 4) return false;
        if (!parts[1].All(c => "0123456789ABCDEF".Contains(c))) return false;

        return parts[2].Equals(ComputeChecksum(parts[1]), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Web sitesinin ürettiği anahtar formatı.</summary>
    public static string GenerateKey()
    {
        var body = RandomNumberGenerator.GetBytes(4);
        var hex = Convert.ToHexString(body);
        return $"{Prefix}-{hex}-{ComputeChecksum(hex)}";
    }

    public static string ComputeChecksum(string body) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Secret + body)))[..4];
}
