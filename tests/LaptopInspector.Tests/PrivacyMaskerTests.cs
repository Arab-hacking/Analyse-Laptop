using LaptopInspector.Core.Privacy;
using Xunit;

namespace LaptopInspector.Tests;

public sealed class PrivacyMaskerTests
{
    [Fact]
    public void MaskIdentifier_IsDeterministicWithinSalt()
    {
        string first = PrivacyMasker.MaskIdentifier("SERIAL-123", "salt");
        string second = PrivacyMasker.MaskIdentifier("SERIAL-123", "salt");
        string third = PrivacyMasker.MaskIdentifier("SERIAL-456", "salt");

        Assert.Equal(first, second);
        Assert.NotEqual(first, third);
        Assert.DoesNotContain("SERIAL", first, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RedactCommandLine_RemovesCommonSecrets()
    {
        string commandLine = "tool.exe --key=abc token=secret Authorization: Bearer eyJhbGciOi";
        string redacted = SensitiveValueRedactor.RedactCommandLine(commandLine);

        Assert.DoesNotContain("abc", redacted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", redacted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("eyJhbGciOi", redacted, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<redacted>", redacted, StringComparison.OrdinalIgnoreCase);
    }
}
