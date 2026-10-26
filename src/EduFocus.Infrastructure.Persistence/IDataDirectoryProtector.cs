namespace EduFocus.Infrastructure.Persistence;

public interface IDataDirectoryProtector
{
    /// <summary>Restricts the data tree to local Administrators and SYSTEM.</summary>
    void Protect(string directoryPath);
}
