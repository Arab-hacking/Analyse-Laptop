using System.Security.Cryptography;
using System.Text;

namespace LaptopInspector.Core.Privacy;

public static class PrivacyMasker
{
    public static string MaskIdentifier(string? value, string salt, string prefix = "masked")
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(salt + "|" + value));
        string hash = Convert.ToHexString(bytes).ToLowerInvariant()[..16];
        return $"<{prefix}:{hash}>";
    }

    public static string MaybeMask(string? value, string salt, bool anonymize, string prefix = "masked")
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return anonymize ? MaskIdentifier(value, salt, prefix) : value;
    }

    /// <summary>
    /// Убирает из пути каталог профиля пользователя, чтобы в режиме --anonymize
    /// пути вида C:\Users\Имя\... не раскрывали имя учётной записи.
    /// </summary>
    public static string MaskPath(string? path, bool anonymize)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        if (!anonymize)
        {
            return path;
        }

        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(profile))
        {
            return path;
        }

        return path.Replace(profile, "<userprofile>", StringComparison.OrdinalIgnoreCase);
    }
}
