using EduFocus.Domain.Entities;
using FluentAssertions;

namespace EduFocus.Domain.Tests.Domain;

public class ParentCredentialTests
{
    private static readonly DateTime Now = new(2026, 10, 29, 7, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Create_StoresOnlyHashesAndMetadata()
    {
        var credential = Create();

        credential.FailedAttempts.Should().Be(0);
        credential.LockoutUntilUtc.Should().BeNull();
        credential.PinUpdatedAtUtc.Should().Be(Now);
        credential.CreatedAtUtc.Should().Be(Now);
        credential.PinIterations.Should().Be(ParentCredential.MinimumIterations);
        credential.RecoveryCodeIterations.Should().Be(ParentCredential.MinimumIterations);
    }

    [Fact]
    public void Create_RejectsIterationsBelowPolicyMinimum()
    {
        var act = () => ParentCredential.Create(
            Hash(), Salt(), 599_999, Hash(), Salt(), ParentCredential.MinimumIterations, Now);

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("pinIterations");
    }

    [Fact]
    public void Create_RejectsWrongHashOrSaltLength()
    {
        var shortHash = Convert.ToBase64String(new byte[16]);
        var act = () => ParentCredential.Create(
            shortHash, Salt(), ParentCredential.MinimumIterations, Hash(), Salt(), ParentCredential.MinimumIterations, Now);

        act.Should().Throw<ArgumentException>().WithParameterName("pinHash");
    }

    [Fact]
    public void Create_RejectsInvalidBase64()
    {
        var act = () => ParentCredential.Create(
            "not base64!", Salt(), ParentCredential.MinimumIterations, Hash(), Salt(), ParentCredential.MinimumIterations, Now);

        act.Should().Throw<ArgumentException>().WithParameterName("pinHash");
    }

    [Fact]
    public void FiveFailedAttempts_TriggerFifteenMinuteLockout()
    {
        var credential = Create();

        for (var i = 0; i < 4; i++)
            credential.RegisterFailedAttempt(Now.AddSeconds(i));

        credential.FailedAttempts.Should().Be(4);
        credential.IsLockedOut(Now.AddSeconds(4)).Should().BeFalse();

        credential.RegisterFailedAttempt(Now.AddSeconds(4));

        credential.FailedAttempts.Should().Be(5);
        credential.LockoutUntilUtc.Should().Be(Now.AddSeconds(4).AddMinutes(15));
        credential.IsLockedOut(Now.AddMinutes(14)).Should().BeTrue();
        credential.IsLockedOut(Now.AddMinutes(19)).Should().BeFalse();
    }

    [Fact]
    public void FailedAttempt_WhileLocked_Throws()
    {
        var credential = LockedCredential();

        var act = () => credential.RegisterFailedAttempt(Now.AddMinutes(1));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void FailedAttempt_AfterLockoutExpires_StartsNewCount()
    {
        var credential = LockedCredential();

        credential.RegisterFailedAttempt(Now.AddMinutes(16));

        credential.FailedAttempts.Should().Be(1);
        credential.LockoutUntilUtc.Should().BeNull();
    }

    [Fact]
    public void SuccessfulAuthentication_ResetsFailures()
    {
        var credential = Create();
        credential.RegisterFailedAttempt(Now.AddSeconds(1));
        credential.RegisterFailedAttempt(Now.AddSeconds(2));

        credential.RegisterSuccessfulAuthentication(Now.AddSeconds(3));

        credential.FailedAttempts.Should().Be(0);
        credential.LockoutUntilUtc.Should().BeNull();
    }

    [Fact]
    public void ChangePin_ReplacesHashAndResetsFailures()
    {
        var credential = Create();
        credential.RegisterFailedAttempt(Now.AddSeconds(1));

        var newHash = Convert.ToBase64String(Enumerable.Repeat((byte)0x42, 32).ToArray());
        var newSalt = Convert.ToBase64String(Enumerable.Repeat((byte)0x24, 16).ToArray());
        credential.ChangePin(newHash, newSalt, 700_000, Now.AddMinutes(1));

        credential.PinHash.Should().Be(newHash);
        credential.PinSalt.Should().Be(newSalt);
        credential.PinIterations.Should().Be(700_000);
        credential.PinUpdatedAtUtc.Should().Be(Now.AddMinutes(1));
        credential.FailedAttempts.Should().Be(0);
    }

    [Fact]
    public void ChangeRecoveryCode_ReplacesHashOnly()
    {
        var credential = Create();
        var newHash = Convert.ToBase64String(Enumerable.Repeat((byte)0x11, 32).ToArray());
        var newSalt = Convert.ToBase64String(Enumerable.Repeat((byte)0x22, 16).ToArray());

        credential.ChangeRecoveryCode(newHash, newSalt, 650_000, Now.AddMinutes(1));

        credential.RecoveryCodeHash.Should().Be(newHash);
        credential.RecoveryCodeSalt.Should().Be(newSalt);
        credential.RecoveryCodeIterations.Should().Be(650_000);
        credential.PinHash.Should().NotBe(newHash);
    }

    [Fact]
    public void DomainModel_ExposesNoPlaintextSecretProperties()
    {
        var propertyNames = typeof(ParentCredential)
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();

        propertyNames.Should().NotContain(["Pin", "RecoveryCode", "PlainPin", "PlainRecoveryCode"]);
    }

    [Fact]
    public void Create_RequiresUtcTimestamp()
    {
        var local = new DateTime(2026, 10, 29, 7, 0, 0, DateTimeKind.Local);

        var act = () => ParentCredential.Create(
            Hash(), Salt(), ParentCredential.MinimumIterations, Hash(), Salt(), ParentCredential.MinimumIterations, local);

        act.Should().Throw<ArgumentException>().WithParameterName("nowUtc");
    }

    private static ParentCredential Create() => ParentCredential.Create(
        Hash(), Salt(), ParentCredential.MinimumIterations,
        Hash(), Salt(), ParentCredential.MinimumIterations,
        Now);

    private static ParentCredential LockedCredential()
    {
        var credential = Create();
        for (var i = 0; i < ParentCredential.MaxFailedAttempts; i++)
            credential.RegisterFailedAttempt(Now.AddSeconds(i));

        return credential;
    }

    private static string Hash() => Convert.ToBase64String(new byte[32]);

    private static string Salt() => Convert.ToBase64String(new byte[16]);
}
