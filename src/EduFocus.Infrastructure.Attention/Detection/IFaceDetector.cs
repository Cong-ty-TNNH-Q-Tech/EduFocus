using EduFocus.Domain.Attention;
using OpenCvSharp;

namespace EduFocus.Infrastructure.Attention.Detection;

public interface IFaceDetector : IDisposable
{
    string Name { get; }
    string Model { get; }
    long LoadMs { get; }
    FaceObservation Detect(Mat bgrFrame);
}
