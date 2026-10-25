using EtlTesting.Core;
using EtlTesting.SqlServer;
using Xunit;

namespace EtlTesting.Tests;

public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute() { if (!OperatingSystem.IsWindows()) Skip = "Windows Credential Manager requires Windows."; }
}
public sealed class WindowsCredentialTests
{
    [WindowsFact] public void AProfileSecretRoundTripsThroughTheWindowsVaultAndIsExcludedFromSerialization()
    {
        var target = "EtlTestStudio/test/" + Guid.NewGuid().ToString("N");
        var credential = new SqlCredentials("etl-workspace-test", "fictional-secret-" + Guid.NewGuid().ToString("N"));
        try
        {
            WindowsCredentialStore.Write(target, credential);
            Assert.Equal(credential, WindowsCredentialStore.Read(target));
            Assert.DoesNotContain(credential.Password, StudioJson.Write(credential));
            Assert.DoesNotContain(credential.Password, credential.ToString());
        }
        finally { WindowsCredentialStore.Delete(target); }
        Assert.False(WindowsCredentialStore.TryRead(target, out _));
    }
}
