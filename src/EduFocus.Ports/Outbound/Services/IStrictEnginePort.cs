using System.Collections.Generic;
using System.Threading.Tasks;
using EduFocus.Domain.Entities;

namespace EduFocus.Ports.Outbound.Services;

public interface IStrictEnginePort
{
    Task ApplyProfileAsync(IReadOnlyCollection<SessionProfileSnapshot> profiles);
    Task ClearRestrictionsAsync();
    Task LockScreenAsync();
}
