namespace EduFocus.UseCases.Attention;

/// <summary>
/// Sampling cadence for UC08. One second (1 FPS) sits inside the 1–2 FPS
/// target from the architecture; the pipeline validates the value at startup.
/// </summary>
public sealed record AttentionPipelineOptions(TimeSpan SampleInterval)
{
    public static AttentionPipelineOptions Default { get; } = new(TimeSpan.FromSeconds(1));
}
