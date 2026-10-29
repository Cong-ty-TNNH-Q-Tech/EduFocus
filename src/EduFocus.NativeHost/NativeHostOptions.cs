namespace EduFocus.NativeHost;

public enum NativeHostMode
{
    Relay,
    Register,
    Unregister
}

/// <summary>
/// Chrome starts the host with the extension origin as the first argument, so
/// register/unregister modes are only recognised by their explicit flags.
/// </summary>
public sealed record NativeHostOptions(
    NativeHostMode Mode,
    bool MachineScope,
    string ManifestDirectory)
{
    public static NativeHostOptions Parse(
        IReadOnlyList<string>? args,
        string defaultManifestDirectory)
    {
        if (args is null || args.Count == 0)
            return new NativeHostOptions(NativeHostMode.Relay, false, defaultManifestDirectory);

        var mode = args.Any(IsRegister)
            ? NativeHostMode.Register
            : args.Any(IsUnregister)
                ? NativeHostMode.Unregister
                : NativeHostMode.Relay;

        if (mode == NativeHostMode.Relay)
            return new NativeHostOptions(mode, false, defaultManifestDirectory);

        var machineScope = false;
        for (var index = 0; index < args.Count; index++)
        {
            if (string.Equals(args[index], "--scope", StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 >= args.Count)
                    throw new ArgumentException("--scope requires 'machine' or 'user'.");

                var scope = args[index + 1];
                if (string.Equals(scope, "machine", StringComparison.OrdinalIgnoreCase))
                    machineScope = true;
                else if (!string.Equals(scope, "user", StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("--scope must be 'machine' or 'user'.");
            }
            else if (string.Equals(args[index], "--manifest-dir", StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 >= args.Count || string.IsNullOrWhiteSpace(args[index + 1]))
                    throw new ArgumentException("--manifest-dir requires a directory path.");

                defaultManifestDirectory = args[index + 1];
            }
        }

        return new NativeHostOptions(mode, machineScope, defaultManifestDirectory);
    }

    private static bool IsRegister(string argument) =>
        string.Equals(argument, "--register", StringComparison.OrdinalIgnoreCase);

    private static bool IsUnregister(string argument) =>
        string.Equals(argument, "--unregister", StringComparison.OrdinalIgnoreCase);
}
