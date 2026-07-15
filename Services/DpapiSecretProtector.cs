using System.Security.Cryptography;
using System.Text;

namespace NetworkHealthMonitor.Services;

public sealed class DpapiSecretProtector : ISecretProtector
{
    private const string Prefix = "dpapi:";

    public string Protect(string plainText)
    {
        if (string.IsNullOrEmpty(plainText) || plainText.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return plainText;
        }

        var bytes = Encoding.UTF8.GetBytes(plainText);
        var protectedBytes = ProtectedData.Protect(bytes, optionalEntropy: null, DataProtectionScope.LocalMachine);
        return Prefix + Convert.ToBase64String(protectedBytes);
    }

    public string Unprotect(string protectedText)
    {
        if (string.IsNullOrEmpty(protectedText) || !protectedText.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return protectedText;
        }

        var protectedBytes = Convert.FromBase64String(protectedText[Prefix.Length..]);
        var bytes = ProtectedData.Unprotect(protectedBytes, optionalEntropy: null, DataProtectionScope.LocalMachine);
        return Encoding.UTF8.GetString(bytes);
    }
}
