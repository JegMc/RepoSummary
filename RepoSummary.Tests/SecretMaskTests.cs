using RepoSummary.Services;

namespace RepoSummary.Tests;

public class SecretMaskTests
{
    [Fact]
    public void Mask_never_reveals_the_middle_of_a_secret()
    {
        var secret = "sk-proj-1234567890ABCDEFghij";
        var masked = EncryptedSecretStore.Mask(secret);

        Assert.NotNull(masked);
        Assert.DoesNotContain("567890ABCDEF", masked);   // the middle is gone
        Assert.Equal("sk-p…ghij", masked);
    }

    [Fact]
    public void Mask_hides_short_secrets_entirely()
    {
        Assert.Equal("••••", EncryptedSecretStore.Mask("abc123"));
        Assert.Equal("••••", EncryptedSecretStore.Mask("12345678"));
    }

    [Fact]
    public void Mask_returns_null_for_empty()
    {
        Assert.Null(EncryptedSecretStore.Mask(null));
        Assert.Null(EncryptedSecretStore.Mask(""));
    }
}
