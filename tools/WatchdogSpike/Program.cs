namespace WatchdogSpike;

public static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            var options = CliOptions.Parse(args);
            if (options.Help)
            {
                CliOptions.PrintHelp();
                return 0;
            }

            return options.Command switch
            {
                SpikeCommand.Scenario => SpikeScenario.Run(options),
                SpikeCommand.Watchdog => WatchdogRunner.Run(options),
                SpikeCommand.Target => TargetApp.Run(options),
                SpikeCommand.TaskCheck or SpikeCommand.TaskCheckLoop => TaskChecker.Run(options),
                _ => 1
            };
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine($"Invalid arguments: {ex.Message}");
            CliOptions.PrintHelp();
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Watchdog spike failed: {ex.GetType().Name}: {ex.Message}");
            Console.Error.WriteLine(ex.StackTrace);
            return 1;
        }
    }
}
