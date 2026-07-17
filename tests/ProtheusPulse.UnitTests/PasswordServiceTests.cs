using ProtheusPulse.Application.Security;
using ProtheusPulse.Infrastructure.Security;

namespace ProtheusPulse.UnitTests;

public sealed class PasswordServiceTests
{
    [Fact]
    public void HashUsesSaltAndVerifiesOnlyTheOriginalPassword()
    {
        var service = new Pbkdf2PasswordService();
        const string password = "UmaSenha!Segura2026";

        var first = service.Hash(password);
        var second = service.Hash(password);

        Assert.NotEqual(first, second);
        Assert.True(service.Verify(password, first));
        Assert.False(service.Verify("senha-incorreta", first));
    }

    [Theory]
    [InlineData("curta")]
    [InlineData("semsimbolo123A")]
    [InlineData("SEMMINUSCULA!123")]
    public void WeakPasswordsAreRejected(string password)
    {
        Assert.NotEmpty(PasswordPolicy.Validate(password));
    }
}
