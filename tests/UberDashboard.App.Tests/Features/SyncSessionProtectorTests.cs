using System.Security.Cryptography;
using UberDashboard.App.Features.Integrations;
using UberDashboard.Core.Synchronization;

namespace UberDashboard.App.Tests.Features;

public sealed class SyncSessionProtectorTests
{
    [Fact]
    public void Upload_session_is_protected_for_the_Windows_user_and_bound_to_account_resource_and_operation()
    {
        var protector = new DpapiSyncSessionProtector(); var binding = new SyncBinding(Guid.NewGuid(), SyncProviderKind.GoogleCalendar, "client", "account", "calendars/a/events", "Test"); var id = Guid.NewGuid();
        var value = "https://www.googleapis.com/upload/drive/v3/files?upload_id=private-session"; var envelope = protector.Protect(binding, id, value);
        Assert.StartsWith("dpapi1:", envelope); Assert.DoesNotContain("private-session", envelope, StringComparison.Ordinal); Assert.Equal(value, protector.Unprotect(binding, id, envelope));
        Assert.Throws<CryptographicException>(() => protector.Unprotect(binding with { AccountId = "other" }, id, envelope));
        Assert.Throws<CryptographicException>(() => protector.Unprotect(binding with { ResourcePath = "calendars/b/events" }, id, envelope));
        Assert.Throws<CryptographicException>(() => protector.Unprotect(binding, Guid.NewGuid(), envelope));
        Assert.Throws<CryptographicException>(() => protector.Unprotect(binding, id, value));
    }
}
