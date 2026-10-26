using System.Diagnostics;
using System.Runtime.InteropServices;
using EduFocus.Domain.Attention;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using OpenCvSharp.Dnn;

namespace EduFocus.Infrastructure.Attention.Detection;

internal sealed class YuNetOnnxDetector : IFaceDetector
{
    private static readonly int[] Strides = [8, 16, 32];

    private readonly InferenceSession _session;
    private readonly string _inputName;
    private readonly int _modelWidth;
    private readonly int _modelHeight;
    private readonly float _scoreThreshold;
    private readonly float _nmsThreshold;

    public YuNetOnnxDetector(string modelPath, int threads, float scoreThreshold, float nmsThreshold)
    {
        var sw = Stopwatch.StartNew();
        var options = new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            IntraOpNumThreads = Math.Max(1, threads),
            InterOpNumThreads = 1
        };

        _session = new InferenceSession(modelPath, options);
        _inputName = _session.InputMetadata.Keys.First();
        var dimensions = _session.InputMetadata[_inputName].Dimensions;
        _modelHeight = dimensions[2];
        _modelWidth = dimensions[3];
        _scoreThreshold = scoreThreshold;
        _nmsThreshold = nmsThreshold;
        sw.Stop();
        LoadMs = sw.ElapsedMilliseconds;
        Model = $"YuNet 2023mar ONNX ({new FileInfo(modelPath).Length / 1024.0:F1} KiB)";
    }

    public string Name => "yunet-onnx";
    public string Model { get; }
    public long LoadMs { get; }

    public FaceObservation Detect(Mat bgrFrame)
    {
        var frameWidth = bgrFrame.Width;
        var frameHeight = bgrFrame.Height;
        var paddedWidth = ((frameWidth - 1) / 32 + 1) * 32;
        var paddedHeight = ((frameHeight - 1) / 32 + 1) * 32;

        using var padded = new Mat();
        Cv2.CopyMakeBorder(bgrFrame, padded, 0, paddedHeight - frameHeight, 0, paddedWidth - frameWidth,
            BorderTypes.Constant, Scalar.All(0));

        using var modelInput = new Mat();
        Cv2.Resize(padded, modelInput, new Size(_modelWidth, _modelHeight), 0, 0, InterpolationFlags.Linear);

        using var blob = CvDnn.BlobFromImage(modelInput, 1.0, new Size(_modelWidth, _modelHeight),
            new Scalar(0, 0, 0), swapRB: false, crop: false);

        var tensor = new DenseTensor<float>(new[] { 1, 3, _modelHeight, _modelWidth });
        var data = new float[3 * _modelHeight * _modelWidth];
        Marshal.Copy(blob.Data, data, 0, data.Length);
        data.AsSpan().CopyTo(tensor.Buffer.Span);

        using var results = _session.Run(new[] { NamedOnnxValue.CreateFromTensor(_inputName, tensor) });
        var outputs = results.ToDictionary(r => r.Name, r => r.AsTensor<float>());

        var candidates = new List<Candidate>();
        var scaleX = paddedWidth / (float)_modelWidth;
        var scaleY = paddedHeight / (float)_modelHeight;

        foreach (var stride in Strides)
        {
            var cols = _modelWidth / stride;
            var rows = _modelHeight / stride;
            var count = cols * rows;
            var cls = outputs[$"cls_{stride}"];
            var obj = outputs[$"obj_{stride}"];
            var bbox = outputs[$"bbox_{stride}"];
            var kps = outputs[$"kps_{stride}"];

            for (var index = 0; index < count; index++)
            {
                var clsScore = Math.Clamp(cls[0, index, 0], 0f, 1f);
                var objScore = Math.Clamp(obj[0, index, 0], 0f, 1f);
                var score = MathF.Sqrt(clsScore * objScore);
                if (score < _scoreThreshold)
                    continue;

                var col = index % cols;
                var row = index / cols;
                var cx = ((col + bbox[0, index, 0]) * stride) * scaleX;
                var cy = ((row + bbox[0, index, 1]) * stride) * scaleY;
                var width = MathF.Exp(bbox[0, index, 2]) * stride * scaleX;
                var height = MathF.Exp(bbox[0, index, 3]) * stride * scaleY;

                var landmarks = new Landmark[5];
                for (var n = 0; n < 5; n++)
                {
                    landmarks[n] = new Landmark(
                        (kps[0, index, 2 * n] + col) * stride * scaleX,
                        (kps[0, index, 2 * n + 1] + row) * stride * scaleY);
                }

                candidates.Add(new Candidate(
                    new Rect2f(cx - width / 2, cy - height / 2, width, height),
                    score,
                    landmarks));
            }
        }

        var kept = NonMaximumSuppression(candidates, _nmsThreshold);
        if (kept.Count == 0)
            return FaceObservation.NoFace;

        var best = kept
            .OrderByDescending(c => c.Score * c.Box.Width * c.Box.Height)
            .First();

        var box = best.Box;
        var rightEye = best.Landmarks[0];
        var leftEye = best.Landmarks[1];
        var nose = best.Landmarks[2];
        var rightMouth = best.Landmarks[3];
        var leftMouth = best.Landmarks[4];

        var eyeMidX = (rightEye.X + leftEye.X) / 2.0;
        var eyeMidY = (rightEye.Y + leftEye.Y) / 2.0;
        var interocular = Math.Sqrt(
            Math.Pow(rightEye.X - leftEye.X, 2) + Math.Pow(rightEye.Y - leftEye.Y, 2));
        var mouthMidY = (rightMouth.Y + leftMouth.Y) / 2.0;
        var verticalSpan = Math.Abs(mouthMidY - eyeMidY);

        var yawRatio = interocular > 1e-3
            ? Math.Abs(nose.X - eyeMidX) / interocular
            : 0;
        var pitchRatio = verticalSpan > 1e-3
            ? Math.Abs(nose.Y - (eyeMidY + 0.55 * verticalSpan)) / verticalSpan
            : 0;

        return new FaceObservation(
            CameraAvailable: true,
            FacePresent: true,
            FaceCount: kept.Count,
            Confidence: best.Score,
            X: box.X,
            Y: box.Y,
            Width: box.Width,
            Height: box.Height,
            RightEye: rightEye,
            LeftEye: leftEye,
            Nose: nose,
            RightMouth: rightMouth,
            LeftMouth: leftMouth,
            EyeCount: 2,
            HeadYawRatio: yawRatio,
            HeadPitchRatio: pitchRatio,
            LandmarksAvailable: true);
    }

    private static List<Candidate> NonMaximumSuppression(List<Candidate> candidates, float threshold)
    {
        var kept = new List<Candidate>();
        foreach (var candidate in candidates.OrderByDescending(c => c.Score))
        {
            if (kept.All(k => IntersectionOverUnion(k.Box, candidate.Box) < threshold))
                kept.Add(candidate);
        }

        return kept;
    }

    private static float IntersectionOverUnion(Rect2f a, Rect2f b)
    {
        var x1 = Math.Max(a.X, b.X);
        var y1 = Math.Max(a.Y, b.Y);
        var x2 = Math.Min(a.X + a.Width, b.X + b.Width);
        var y2 = Math.Min(a.Y + a.Height, b.Y + b.Height);
        var intersection = Math.Max(0, x2 - x1) * Math.Max(0, y2 - y1);
        var union = a.Width * a.Height + b.Width * b.Height - intersection;
        return union <= 0 ? 0 : intersection / union;
    }

    public void Dispose() => _session.Dispose();

    private sealed record Candidate(Rect2f Box, float Score, Landmark[] Landmarks);
}
