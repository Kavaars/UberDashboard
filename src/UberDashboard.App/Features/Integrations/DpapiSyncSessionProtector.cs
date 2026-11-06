using System.IO;
using System.Security.Cryptography;
using System.Text;
using UberDashboard.Application.Synchronization;
using UberDashboard.Core.Synchronization;

namespace UberDashboard.App.Features.Integrations;

internal sealed class DpapiSyncSessionProtector : ISyncSessionProtector
{
    public string Protect(SyncBinding binding, Guid transferId, string value)
    {
        if (value.Length > 16384) throw new InvalidDataException("Invalid upload session.");
        return "dpapi1:" + Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), Entropy(binding, transferId), DataProtectionScope.CurrentUser));
    }
    public string Unprotect(SyncBinding binding, Guid transferId, string envelope)
    {
        if (!envelope.StartsWith("dpapi1:", StringComparison.Ordinal) || envelope.Length > 32768) throw new CryptographicException("Invalid protected session.");
        return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(envelope[7..]), Entropy(binding, transferId), DataProtectionScope.CurrentUser));
    }
    private static byte[] Entropy(SyncBinding binding, Guid id) => SHA256.HashData(Encoding.UTF8.GetBytes(
        $"UberDashboard/upload/v1/{binding.Provider}/{binding.ClientId}/{binding.AccountId}/{binding.ResourcePath}/{id:D}"));
}
