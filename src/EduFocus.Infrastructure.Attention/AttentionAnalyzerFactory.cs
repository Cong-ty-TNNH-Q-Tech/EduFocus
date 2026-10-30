using EduFocus.Domain.Attention;
using EduFocus.Infrastructure.Attention.Detection;
using EduFocus.Ports.DTOs;
using EduFocus.Ports.Outbound.Services;

namespace EduFocus.Infrastructure.Attention;

public static class AttentionAnalyzerFactory
{
    public static IAttentionFrameAnalyzer Create(
        string backend,
        AttentionAnalyzerOptions options,
        string modelDirectory)
    {
        var detector = CreateDetector(backend, options, modelDirectory);
        return new OpenCvAttentionFrameAnalyzer(detector);
    }

    public static IAttentionFrameAnalyzer CreateLazy(
        string backend,
        AttentionAnalyzerOptions options,
        string modelDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backend);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelDirectory);

        return new LazyAttentionFrameAnalyzer(backend, options, modelDirectory);
    }

    public static IFaceDetector CreateDetector(
        string backend,
        AttentionAnalyzerOptions options,
        string modelDirectory)
    {
        return backend.Trim().ToLowerInvariant() switch
        {
            "yunet" => CreateYuNet(options, modelDirectory),
            "haar" => CreateHaar(modelDirectory),
            _ => throw new ArgumentException($"Unknown backend: {backend}. Supported: yunet, haar.", nameof(backend))
        };
    }

    private static YuNetOnnxDetector CreateYuNet(AttentionAnalyzerOptions options, string modelDirectory)
    {
        var modelPath = Path.Combine(modelDirectory, "face_detection_yunet_2023mar.onnx");
        if (!File.Exists(modelPath))
            throw new FileNotFoundException("YuNet model not found.", modelPath);

        return new YuNetOnnxDetector(
            modelPath,
            options.OnnxThreads,
            options.ScoreThreshold,
            options.NmsThreshold);
    }

    private static HaarCascadeDetector CreateHaar(string modelDirectory)
    {
        var facePath = Path.Combine(modelDirectory, "haarcascade_frontalface_default.xml");
        var eyePath = Path.Combine(modelDirectory, "haarcascade_eye.xml");

        if (!File.Exists(facePath))
            throw new FileNotFoundException("Haar face cascade not found.", facePath);
        if (!File.Exists(eyePath))
            throw new FileNotFoundException("Haar eye cascade not found.", eyePath);

        return new HaarCascadeDetector(facePath, eyePath);
    }

    private sealed class LazyAttentionFrameAnalyzer : IAttentionFrameAnalyzer
    {
        private readonly string _backend;
        private readonly AttentionAnalyzerOptions _options;
        private readonly string _modelDirectory;
        private readonly object _gate = new();
        private IAttentionFrameAnalyzer? _inner;
        private bool _disposed;

        public LazyAttentionFrameAnalyzer(
            string backend,
            AttentionAnalyzerOptions options,
            string modelDirectory)
        {
            _backend = backend;
            _options = options;
            _modelDirectory = modelDirectory;
        }

        public string BackendName => _inner?.BackendName ?? _backend;

        public string ModelInfo => _inner?.ModelInfo ?? "deferred";

        public long ModelLoadMs => _inner?.ModelLoadMs ?? 0;

        public AttentionFrameResult Analyze(CameraFrame frame)
        {
            ArgumentNullException.ThrowIfNull(frame);

            IAttentionFrameAnalyzer analyzer;
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);

                if (_inner is null)
                {
                    try
                    {
                        _inner = Create(_backend, _options, _modelDirectory);
                    }
                    catch (FileNotFoundException) when (
                        !string.Equals(_backend.Trim(), "haar", StringComparison.OrdinalIgnoreCase)
                        && File.Exists(Path.Combine(_modelDirectory, "haarcascade_frontalface_default.xml"))
                        && File.Exists(Path.Combine(_modelDirectory, "haarcascade_eye.xml")))
                    {
                        _inner = Create("haar", _options, _modelDirectory);
                    }
                    catch (Exception ex)
                    {
                        return new AttentionFrameResult(
                            FaceObservation.NoFace,
                            Luminance: 0,
                            DetectMs: 0,
                            Error: $"detector-error: {ex.GetType().Name}: {ex.Message}");
                    }
                }

                analyzer = _inner;
            }

            return analyzer.Analyze(frame);
        }

        public void Dispose()
        {
            IAttentionFrameAnalyzer? inner;
            lock (_gate)
            {
                if (_disposed)
                    return;

                _disposed = true;
                inner = _inner;
                _inner = null;
            }

            inner?.Dispose();
        }
    }
}
