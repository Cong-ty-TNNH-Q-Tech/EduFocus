namespace EduFocus.Domain.Reports;

public enum TopActivityKind
{
    App,
    Domain,
    Unknown
}

/// <summary>
/// One row of the top app/domain list (database schema §13.3). Only the stored
/// app display name and the sanitized domain are eligible; window titles and
/// URLs never enter a report.
/// </summary>
public sealed record TopActivityItem(string Name, TopActivityKind Kind, int Seconds);
