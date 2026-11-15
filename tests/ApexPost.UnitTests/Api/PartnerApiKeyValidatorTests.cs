using ApexPost.Api.Services;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace ApexPost.UnitTests.Api;

public sealed class PartnerApiKeyValidatorTests
{
    [Theory]
    [InlineData("partner-secret", true)]
    [InlineData("PARTNER-SECRET", false)]   // comparison is case-sensitive
    [InlineData("partner-secret ", false)]  // no trimming
    [InlineData("wrong", false)]
    [InlineData("", false)]
    [InlineData(" ", false)]
    [InlineData(null, false)]
    public void IsValid_SuppliedKey_MatchesOnlyTheExactConfiguredKey(string? supplied, bool expected)
    {
        // Arrange
        var configuration = new ConfigurationManager { ["Partner:ApiKey"] = "partner-secret" };
        var validator = new PartnerApiKeyValidator(configuration);

        // Act
        var valid = validator.IsValid(supplied);

        // Assert
        Assert.Equal(expected, valid);
    }

    [Fact]
    public void IsValid_NoKeyConfigured_FailsClosed()
    {
        // Arrange
        var validator = new PartnerApiKeyValidator(new ConfigurationManager());

        // Act
        var valid = validator.IsValid("any-supplied-key");

        // Assert
        Assert.False(valid);
        Assert.False(validator.IsConfigured);
    }
}
