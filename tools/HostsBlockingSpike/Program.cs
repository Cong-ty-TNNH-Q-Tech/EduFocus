using HostsBlockingSpike;

try
{
    var options = CliOptions.Parse(args);
    if (options.Help)
    {
        CliOptions.PrintHelp();
        return 0;
    }

    return await SpikeRunner.RunAsync(options);
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine($"Invalid arguments: {ex.Message}");
    CliOptions.PrintHelp();
    return 1;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Spike failed: {ex.GetType().Name}: {ex.Message}");
    Console.Error.WriteLine(ex.StackTrace);
    return 1;
}
