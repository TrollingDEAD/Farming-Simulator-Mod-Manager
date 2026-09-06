using FsModManager.Core.ModScanning;
using Xunit;

namespace FsModManager.Core.Tests.ModScanning;

public class ModFilenameValidatorTests
{
    [Theory]
    [InlineData("FS25_CoolTractor.zip")]
    [InlineData("FS25_SomeMod.zip")]
    [InlineData("mod_123.zip")]
    [InlineData("FS25_mod_v2.zip")]
    [InlineData("A.zip")]
    public void Validate_WithValidFilenames_ReturnsValid(string filename)
    {
        var result = ModFilenameValidator.Validate(filename);

        Assert.True(result.IsValid);
        Assert.Empty(result.InvalidCharacters);
        Assert.Null(result.Reason);
        Assert.Equal(filename, result.SuggestedFilename);
    }

    [Fact]
    public void Validate_WithSpaces_IdentifiesSpaceAndInvalid()
    {
        var filename = "FS25 Some Mod.zip";
        var result = ModFilenameValidator.Validate(filename);

        Assert.False(result.IsValid);
        Assert.Contains(' ', result.InvalidCharacters);
        Assert.Contains("space", result.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("FS25_Some_Mod.zip", result.SuggestedFilename);
    }

    [Fact]
    public void Validate_WithParentheses_IdentifiesParenthesesAndSpace()
    {
        var filename = "FS25_SomeMod (1).zip";
        var result = ModFilenameValidator.Validate(filename);

        Assert.False(result.IsValid);
        Assert.Contains(' ', result.InvalidCharacters);
        Assert.Contains('(', result.InvalidCharacters);
        Assert.Contains(')', result.InvalidCharacters);
        Assert.Contains("parenthesis", result.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("FS25_SomeMod_1.zip", result.SuggestedFilename);
    }

    [Fact]
    public void Validate_WithDashesAndDots_IdentifiesSpecialCharacters()
    {
        var filename = "FS25-Cool.Tractor-v1.zip";
        var result = ModFilenameValidator.Validate(filename);

        Assert.False(result.IsValid);
        Assert.Contains('-', result.InvalidCharacters);
        Assert.Contains('.', result.InvalidCharacters);
        Assert.Equal("FS25_Cool_Tractor_v1.zip", result.SuggestedFilename);
    }

    [Fact]
    public void SanitizeFilename_WithComplicatedName_ProducesValidName()
    {
        var filename = " [FS25] My (Favorite) Mod -- v2.0 !.zip";
        var sanitized = ModFilenameValidator.SanitizeFilename(filename);

        Assert.Equal("FS25_My_Favorite_Mod_v2_0.zip", sanitized);
        var validation = ModFilenameValidator.Validate(sanitized);
        Assert.True(validation.IsValid);
    }

    [Fact]
    public void Validate_WithEmptyOrNull_ReturnsInvalid()
    {
        var result = ModFilenameValidator.Validate(null);
        Assert.False(result.IsValid);
        Assert.NotNull(result.Reason);
    }
}
