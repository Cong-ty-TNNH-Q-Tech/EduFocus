using EduFocus.Ports.Outbound.Services;

namespace EduFocus.UseCases.Tests.TestSupport;

internal sealed class FakeRecoveryCodeGenerator : IRecoveryCodeGenerator
{
    private readonly Queue<string> _codes;

    public FakeRecoveryCodeGenerator(params string[] codes)
    {
        _codes = new Queue<string>(codes);
    }

    public int GeneratedCount { get; private set; }

    public string Generate()
    {
        GeneratedCount++;
        return _codes.Dequeue();
    }
}
