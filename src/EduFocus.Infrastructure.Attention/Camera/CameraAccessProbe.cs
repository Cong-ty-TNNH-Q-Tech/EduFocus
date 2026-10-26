namespace EduFocus.Infrastructure.Attention.Camera;

public sealed record CameraAccessResult(bool Accessible);

/// <summary>Checks whether a camera can be opened without capturing or retaining any frame data.</summary>
public static class CameraAccessProbe
{
    public static CameraAccessResult Check(int cameraIndex)
    {
        if (cameraIndex is < 0 or > 15) return new(false);
        try
        {
            using var camera = new OpenCvCameraSource(cameraIndex, "msmf", 320, 240);
            return new(camera.Open().Success);
        }
        catch { return new(false); }
    }
}
