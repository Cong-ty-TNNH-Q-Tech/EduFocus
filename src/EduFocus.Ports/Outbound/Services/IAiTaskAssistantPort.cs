using System.Threading.Tasks;

namespace EduFocus.Ports.Outbound.Services;

public interface IAiTaskAssistantPort
{
    Task<string> AnalyzeAndSuggestTaskPriorityAsync(string dailyTasksDump);
    Task<string> BreakdownTaskAsync(string taskName);
}
