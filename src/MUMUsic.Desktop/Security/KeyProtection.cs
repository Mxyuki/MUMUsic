using System.Security.Cryptography;
using System.Text;

namespace MUMUsic.Desktop.Security;

/// <summary>The access key as stored in settings.json: DPAPI for the current Windows user, then base64. The same as
/// the 1.x app (CryptProtectData, no entropy), so a key saved by it still works.</summary>
internal static class KeyProtection
{
    public static string Protect(string text) =>
        Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(text), null, DataProtectionScope.CurrentUser));

    public static string? Unprotect(string? stored)
    {
        if (string.IsNullOrEmpty(stored)) return null;
        try
        {
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(stored), null,
                DataProtectionScope.CurrentUser));
        }
        catch (Exception error) when (error is FormatException or CryptographicException)
        {
            return null;
        }
    }
}
