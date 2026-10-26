using System.Diagnostics;
using EduFocus.Domain.Attention;
using OpenCvSharp;

namespace EduFocus.Infrastructure.Attention.Detection;

internal sealed class HaarCascadeDetector : IFaceDetector
{
    private readonly CascadeClassifier _faceCascade;
    private readonly CascadeClassifier _eyeCascade;

    public HaarCascadeDetector(string faceModelPath, string eyeModelPath)
    {
        var sw = Stopwatch.StartNew();
        _faceCascade = new CascadeClassifier(faceModelPath);
        _eyeCascade = new CascadeClassifier(eyeModelPath);
        sw.Stop();
        LoadMs = sw.ElapsedMilliseconds;

        if (_faceCascade.Empty() || _eyeCascade.Empty())
            throw new InvalidOperationException("Could not load Haar cascade model files.");

        var faceSize = new FileInfo(faceModelPath).Length;
        var eyeSize = new FileInfo(eyeModelPath).Length;
        Model = $"Haar cascades ({(faceSize + eyeSize) / 1024.0:F1} KiB)";
    }

    public string Name => "haar-cascade";
    public string Model { get; }
    public long LoadMs { get; }

    public FaceObservation Detect(Mat bgrFrame)
    {
        using var gray = new Mat();
        Cv2.CvtColor(bgrFrame, gray, ColorConversionCodes.BGR2GRAY);
        Cv2.EqualizeHist(gray, gray);

        var faces = _faceCascade.DetectMultiScale(
            gray,
            scaleFactor: 1.1,
            minNeighbors: 6,
            flags: HaarDetectionTypes.ScaleImage,
            minSize: new Size(80, 80));

        if (faces.Length == 0)
            return FaceObservation.NoFace;

        var face = faces.OrderByDescending(f => f.Width * f.Height).First();
        var eyeCount = 0;

        var eyeHeight = Math.Max(1, (int)(face.Height * 0.6));
        var upperFace = new Rect(face.X, face.Y, face.Width, eyeHeight);
        using (var eyeRoi = new Mat(gray, upperFace))
        {
            var eyes = _eyeCascade.DetectMultiScale(
                eyeRoi,
                scaleFactor: 1.1,
                minNeighbors: 4,
                flags: HaarDetectionTypes.ScaleImage,
                minSize: new Size(20, 20));
            eyeCount = Math.Min(2, eyes.Length);
        }

        var frameCenterX = bgrFrame.Width / 2.0;
        var frameCenterY = bgrFrame.Height / 2.0;
        var faceCenterX = face.X + face.Width / 2.0;
        var faceCenterY = face.Y + face.Height / 2.0;

        var yawRatio = Math.Abs(faceCenterX - frameCenterX) / Math.Max(1.0, bgrFrame.Width / 2.0);
        var pitchRatio = Math.Abs(faceCenterY - frameCenterY) / Math.Max(1.0, bgrFrame.Height / 2.0);

        return new FaceObservation(
            CameraAvailable: true,
            FacePresent: true,
            FaceCount: faces.Length,
            Confidence: eyeCount > 0 ? 0.75f : 0.55f,
            X: face.X,
            Y: face.Y,
            Width: face.Width,
            Height: face.Height,
            RightEye: null,
            LeftEye: null,
            Nose: null,
            RightMouth: null,
            LeftMouth: null,
            EyeCount: eyeCount,
            HeadYawRatio: yawRatio,
            HeadPitchRatio: pitchRatio,
            LandmarksAvailable: false);
    }

    public void Dispose()
    {
        _faceCascade.Dispose();
        _eyeCascade.Dispose();
    }
}
