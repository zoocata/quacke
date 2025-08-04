using System;
using System.Security.Cryptography;
using System.Text;

namespace QuakeServerManager.Services
{
    /// <summary>
    /// Simple helper that uses Windows DPAPI (CurrentUser scope) to protect and unprotect strings.
    /// Falls back to returning the input string on any failure so existing plain-text files still load.
    /// </summary>
    internal static class CryptoHelper
    {
        public static string Protect(string plainText)
        {
            if (string.IsNullOrEmpty(plainText)) return plainText;
            try
            {
                var bytes = Encoding.UTF8.GetBytes(plainText);
                var protectedBytes = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
                return Convert.ToBase64String(protectedBytes);
            }
            catch
            {
                // In the unlikely event of a failure, return plaintext to avoid data-loss.
                return plainText;
            }
        }

        public static string Unprotect(string protectedText)
        {
            if (string.IsNullOrEmpty(protectedText)) return protectedText;
            try
            {
                var protectedBytes = Convert.FromBase64String(protectedText);
                var bytes = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(bytes);
            }
            catch
            {
                // If it was never protected (legacy plain-text file), just return the original.
                return protectedText;
            }
        }
    }
}