using EduFocus.Domain.Entities;
using EduFocus.UseCases.Auth;

namespace EduFocus.UseCases.Tests.TestSupport;

internal sealed class AuthHarness
{
    public const string InitialPin = "482915";
    public const string ReplacementPin = "135790";
    public const string SecondReplacementPin = "246813";
    public const string FirstRecoveryCode = "ABCDEFGHJKLM";
    public const string SecondRecoveryCode = "NPQRSTUVWXYZ";
    public const string ThirdRecoveryCode = "23456789ABCD";

    public static readonly DateTimeOffset Start = new(2026, 10, 29, 8, 0, 0, TimeSpan.Zero);
    public static readonly Guid CorrelationId = Guid.Parse("11111111-2222-3333-4444-555555555555");

    public AuthHarness()
    {
        RecoveryCodes = new FakeRecoveryCodeGenerator(
            FirstRecoveryCode,
            SecondRecoveryCode,
            ThirdRecoveryCode);
        Clock = new ManualTimeProvider(Start);
        Correlation = new TestCorrelationContext { Current = CorrelationId };
        UseCase = new ParentAuthUseCase(
            Credentials,
            Hasher,
            RecoveryCodes,
            Audits,
            Correlation,
            Clock);
    }

    public FakeParentCredentialRepository Credentials { get; } = new();

    public FakePasswordHasher Hasher { get; } = new();

    public FakeRecoveryCodeGenerator RecoveryCodes { get; }

    public RecordingAuditLogRepository Audits { get; } = new();

    public TestCorrelationContext Correlation { get; }

    public ManualTimeProvider Clock { get; }

    public ParentAuthUseCase UseCase { get; }

    public ParentCredential Credential =>
        Credentials.Stored ?? throw new InvalidOperationException("No credential has been stored.");

    public static async Task<AuthHarness> WithCredentialAsync()
    {
        var harness = new AuthHarness();
        await harness.UseCase.SetupAsync(InitialPin);
        return harness;
    }
}
