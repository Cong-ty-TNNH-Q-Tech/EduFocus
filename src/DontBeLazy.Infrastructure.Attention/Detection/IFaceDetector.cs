using DontBeLazy.Domain.Attention;
using OpenCvSharp;

namespace DontBeLazy.Infrastructure.Attention.Detection;

public interface IFaceDetector : IDisposable
{
    string Name { get; }
    string Model { get; }
    long LoadMs { get; }
    FaceObservation Detect(Mat bgrFrame);
}
