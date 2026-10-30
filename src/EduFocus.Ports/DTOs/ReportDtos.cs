using EduFocus.Domain.Enums;
using EduFocus.Domain.ValueObjects;

namespace EduFocus.Ports.DTOs;

/// <summary>
/// Minimal activity row for day/week reports (UC10, issue #36). Window titles
/// and URLs are deliberately absent so detailed reports cannot leak sensitive
/// text; the domain value is the sanitized domain stored at capture time.
/// </summary>
public sealed record ReportActivitySample(
    StudySessionId SessionId,
    DateTime SampledAtUtc,
    int ActiveSeconds,
    ActivityCategory Category,
    string? AppName,
    DomainName? Domain);
