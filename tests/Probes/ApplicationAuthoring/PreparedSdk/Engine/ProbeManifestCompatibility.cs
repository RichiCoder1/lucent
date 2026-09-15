namespace Lucent.ApplicationAuthoring.SdkHost;

// The shared experimental driver also carries the original SDK-host comparison helper.
// PreparedSdk does not use it, but links the complete driver to keep one implementation.
internal sealed record ManifestEntry(string Identity, string Sha256);
