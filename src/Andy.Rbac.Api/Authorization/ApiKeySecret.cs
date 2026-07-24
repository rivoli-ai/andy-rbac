using System.Security.Cryptography;
using System.Text;

namespace Andy.Rbac.Api.Authorization;

/// <summary>
/// Generation, formatting and verification of API key material.
///
/// A key is presented to the user exactly once, as <c>{prefix}.{secret}</c>:
///   - <c>prefix</c> identifies the row (unique, indexed, safe to log) so
///     verification is a single indexed lookup rather than a table scan.
///   - <c>secret</c> is 32 bytes of CSPRNG output, base64url-encoded.
///
/// Only SHA-256 of the secret is persisted. A password KDF (bcrypt/PBKDF2)
/// buys nothing here — the secret is full-entropy random rather than a
/// user-chosen password, so there is no dictionary to stretch against, and the
/// per-request cost would land on every authenticated call. Comparison is
/// fixed-time regardless.
/// </summary>
public static class ApiKeySecret
{
    /// <summary>Marks the key as belonging to this service, and its environment.</summary>
    public const string LivePrefix = "rbac_live";

    private const int SecretBytes = 32;
    private const int PrefixRandomBytes = 9; // 12 base64url chars

    public sealed record GeneratedKey(string Prefix, string Secret, string Hash)
    {
        /// <summary>The full key. Shown once at creation and never recoverable.</summary>
        public string PlaintextKey => $"{Prefix}.{Secret}";
    }

    public static GeneratedKey Generate()
    {
        var prefix = $"{LivePrefix}_{ToBase64Url(RandomNumberGenerator.GetBytes(PrefixRandomBytes))}";
        var secret = ToBase64Url(RandomNumberGenerator.GetBytes(SecretBytes));
        return new GeneratedKey(prefix, secret, Hash(secret));
    }

    /// <summary>
    /// Splits a presented key into its lookup prefix and secret. Returns false
    /// for anything malformed, so the caller never runs a lookup on garbage.
    /// </summary>
    public static bool TryParse(string? presented, out string prefix, out string secret)
    {
        prefix = string.Empty;
        secret = string.Empty;

        if (string.IsNullOrWhiteSpace(presented))
            return false;

        // The prefix itself contains '_' but no '.', so split on the last '.'
        // to tolerate any future secret alphabet.
        var separator = presented.LastIndexOf('.');
        if (separator <= 0 || separator == presented.Length - 1)
            return false;

        prefix = presented[..separator];
        secret = presented[(separator + 1)..];
        return prefix.StartsWith(LivePrefix, StringComparison.Ordinal);
    }

    /// <summary>
    /// Base64url (RFC 4648 §5) without padding, so keys stay safe in headers,
    /// URLs and shell arguments without quoting.
    /// </summary>
    private static string ToBase64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static string Hash(string secret) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret))).ToLowerInvariant();

    /// <summary>
    /// Fixed-time comparison of a presented secret against a stored hash, so
    /// verification time can't be used to recover the hash byte by byte.
    /// </summary>
    public static bool Verify(string presentedSecret, string storedHash)
    {
        var computed = Encoding.UTF8.GetBytes(Hash(presentedSecret));
        var stored = Encoding.UTF8.GetBytes(storedHash ?? string.Empty);
        return CryptographicOperations.FixedTimeEquals(computed, stored);
    }
}
