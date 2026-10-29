namespace EduFocus.Domain.Enums;

/// <summary>Owns the current ON state so an automatic schedule can end its own protection.</summary>
public enum ProtectionSource
{
    None,
    Manual,
    Schedule
}
