using Ledger.SyncServer.Authentication;
using Microsoft.Extensions.Options;

namespace Ledger.SyncServer.UnitTests;

public class ApiKeyValidatorTests
{
    [Fact]
    public void Returns_true_for_a_key_matching_a_configured_hash()
    {
        var validator = CreateValidator(ApiKeyHasher.Hash("correct-key"));

        Assert.True(validator.IsValid("correct-key"));
    }

    [Fact]
    public void Returns_false_for_a_key_not_matching_any_configured_hash()
    {
        var validator = CreateValidator(ApiKeyHasher.Hash("correct-key"));

        Assert.False(validator.IsValid("wrong-key"));
    }

    [Fact]
    public void Returns_false_when_no_keys_are_configured()
    {
        var validator = CreateValidator();

        Assert.False(validator.IsValid("anything"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Returns_false_for_null_or_empty_input(string? apiKey)
    {
        var validator = CreateValidator(ApiKeyHasher.Hash("correct-key"));

        Assert.False(validator.IsValid(apiKey));
    }

    [Fact]
    public void Matches_against_any_of_several_configured_hashes_not_just_the_first()
    {
        var validator = CreateValidator(
            ApiKeyHasher.Hash("device-a-key"),
            ApiKeyHasher.Hash("device-b-key"));

        Assert.True(validator.IsValid("device-b-key"));
    }

    private static ApiKeyValidator CreateValidator(params string[] hashes) =>
        new(Options.Create(new ApiKeySettings { Hashes = hashes }));
}
