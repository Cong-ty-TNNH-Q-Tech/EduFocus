using EduFocus.Ports.Outbound.Services;

namespace EduFocus.Infrastructure.FocusAudio;

/// <summary>
/// Synthesises the looping ambient tracks locally so the focus player needs no
/// downloaded media and no third-party audio library (issue #23). The output is
/// 16-bit mono PCM WAV, already crossfaded at the loop point.
/// </summary>
public static class ProceduralAmbientAudio
{
    public const int SampleRate = 22_050;
    public const int LoopSeconds = 20;
    public const int BitsPerSample = 16;
    public const short Channels = 1;

    private const int FadeSeconds = 1;
    private const float MasterPeak = 0.55f;

    public static byte[] CreateWave(FocusAudioPreset preset)
    {
        var loopLength = SampleRate * LoopSeconds;
        var fadeLength = SampleRate * FadeSeconds;
        var samples = new float[loopLength + fadeLength];
        var random = new Random(SeedOf(preset));

        switch (preset)
        {
            case FocusAudioPreset.WhiteNoise:
                FillWhiteNoise(samples, random);
                break;
            case FocusAudioPreset.Rain:
                FillRain(samples, random);
                break;
            case FocusAudioPreset.Cafe:
                FillCafe(samples, random);
                break;
            case FocusAudioPreset.Fireplace:
                FillFireplace(samples, random);
                break;
            case FocusAudioPreset.Lofi:
                FillLofi(samples, random);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(preset), preset, "Unknown focus audio preset.");
        }

        CrossfadeLoop(samples, loopLength, fadeLength);
        Normalize(samples.AsSpan(0, loopLength), MasterPeak);
        return WriteWave(samples.AsSpan(0, loopLength));
    }

    private static void FillWhiteNoise(float[] samples, Random random)
    {
        var filtered = 0f;

        for (var index = 0; index < samples.Length; index++)
        {
            var white = (float)(random.NextDouble() * 2 - 1);
            filtered = filtered * 0.35f + white * 0.65f;
            samples[index] = filtered * 0.7f;
        }
    }

    private static void FillRain(float[] samples, Random random)
    {
        var hiss = 0f;
        var rumble = 0f;

        for (var index = 0; index < samples.Length; index++)
        {
            var white = (float)(random.NextDouble() * 2 - 1);
            hiss = hiss * 0.62f + white * 0.38f;
            rumble = rumble * 0.995f + white * 0.005f;
            var swell = 0.85f + 0.15f * MathF.Sin(index * 0.0007f);
            samples[index] = (hiss * 0.5f + rumble * 3.2f) * swell;
        }

        AddDroplets(samples, random, count: 26, minFrequency: 900f, maxFrequency: 3_600f, maxAmplitude: 0.3f);
    }

    private static void FillCafe(float[] samples, Random random)
    {
        var hiss = 0f;
        var rumble = 0f;
        var murmurA = 96f + (float)random.NextDouble() * 42f;
        var murmurB = 148f + (float)random.NextDouble() * 55f;

        for (var index = 0; index < samples.Length; index++)
        {
            var white = (float)(random.NextDouble() * 2 - 1);
            hiss = hiss * 0.82f + white * 0.18f;
            rumble = rumble * 0.997f + white * 0.003f;

            var time = (float)index / SampleRate;
            var voice =
                MathF.Sin(2 * MathF.PI * murmurA * time + 0.6f * MathF.Sin(time * 0.7f)) * 0.5f +
                MathF.Sin(2 * MathF.PI * murmurB * time + 0.4f * MathF.Sin(time * 1.1f)) * 0.35f;
            var chatter = (0.55f + 0.45f * MathF.Sin(time * 0.55f)) * (0.4f + 0.6f * MathF.Sin(time * 1.7f));

            samples[index] = hiss * 0.16f + rumble * 2.4f + voice * chatter * 0.045f;
        }

        AddDroplets(samples, random, count: 8, minFrequency: 1_600f, maxFrequency: 3_400f, maxAmplitude: 0.16f);
    }

    private static void FillFireplace(float[] samples, Random random)
    {
        var rumble = 0f;
        var flicker = 0f;

        for (var index = 0; index < samples.Length; index++)
        {
            var white = (float)(random.NextDouble() * 2 - 1);
            rumble = rumble * 0.993f + white * 0.007f;
            flicker = flicker * 0.9f + white * 0.1f;
            var time = (float)index / SampleRate;
            var breath = 0.72f + 0.28f * MathF.Sin(time * 0.45f) * MathF.Sin(time * 0.13f + 1.4f);
            samples[index] = (rumble * 3.4f + flicker * 0.18f) * breath;
        }

        var crackles = 70 + random.Next(20);

        for (var crackle = 0; crackle < crackles; crackle++)
        {
            var start = random.Next(samples.Length);
            var duration = (int)(SampleRate * (0.004 + random.NextDouble() * 0.03));
            var amplitude = 0.25f + (float)random.NextDouble() * 0.55f;
            var tone = 700f + (float)random.NextDouble() * 2_300f;

            for (var offset = 0; offset < duration && start + offset < samples.Length; offset++)
            {
                var progress = (float)offset / duration;
                var decay = MathF.Exp(-progress * 7f);
                var noise = (float)(random.NextDouble() * 2 - 1);
                var click = MathF.Sin(2 * MathF.PI * tone * offset / SampleRate);
                samples[start + offset] += (noise * 0.7f + click * 0.3f) * decay * amplitude * 0.35f;
            }
        }
    }

    private static void FillLofi(float[] samples, Random random)
    {
        ReadOnlySpan<float> chords =
        [
            130.81f, 164.81f, 196.00f, 246.94f,
            110.00f, 130.81f, 164.81f, 196.00f,
            146.83f, 174.61f, 220.00f, 261.63f,
            98.00f, 123.47f, 146.83f, 174.61f
        ];

        var chordSeconds = 5f;
        var chordLength = (int)(SampleRate * chordSeconds);
        var pad = new float[samples.Length];

        for (var index = 0; index < samples.Length; index++)
        {
            var position = (float)index / SampleRate;
            var chordIndex = (int)(position / chordSeconds) % 4;
            var local = position % chordSeconds;
            var envelope = MathF.Min(1f, local / 0.9f) * MathF.Min(1f, (chordSeconds - local) / 0.9f);
            var value = 0f;

            for (var note = 0; note < 4; note++)
            {
                var frequency = chords[chordIndex * 4 + note];
                var phase = 2 * MathF.PI * frequency * position;
                var detune = 2 * MathF.PI * frequency * 1.002f * position;
                value += MathF.Sin(phase) * 0.5f + MathF.Sin(detune) * 0.25f;
                value += MathF.Sin(phase * 2f) * 0.12f;
            }

            var tremolo = 0.82f + 0.18f * MathF.Sin(position * 1.3f);
            pad[index] = value * envelope * tremolo * 0.055f;
        }

        var rumble = 0f;

        for (var index = 0; index < samples.Length; index++)
        {
            var white = (float)(random.NextDouble() * 2 - 1);
            rumble = rumble * 0.997f + white * 0.003f;
            samples[index] = pad[index] + rumble * 1.4f;
        }

        AddDroplets(samples, random, count: 150, minFrequency: 1_800f, maxFrequency: 6_000f, maxAmplitude: 0.05f);

        var beatInterval = SampleRate * 2;

        for (var start = 0; start < samples.Length; start += beatInterval)
        {
            var duration = (int)(SampleRate * 0.28);

            for (var offset = 0; offset < duration && start + offset < samples.Length; offset++)
            {
                var progress = (float)offset / duration;
                var decay = MathF.Exp(-progress * 6f);
                var kick = MathF.Sin(2 * MathF.PI * 52f * offset / SampleRate);
                samples[start + offset] += kick * decay * 0.12f;
            }
        }
    }

    private static void AddDroplets(
        float[] samples,
        Random random,
        int count,
        float minFrequency,
        float maxFrequency,
        float maxAmplitude)
    {
        for (var drop = 0; drop < count; drop++)
        {
            var start = random.Next(samples.Length);
            var duration = (int)(SampleRate * (0.015 + random.NextDouble() * 0.05));
            var frequency = minFrequency + (float)random.NextDouble() * (maxFrequency - minFrequency);
            var amplitude = maxAmplitude * (0.35f + (float)random.NextDouble() * 0.65f);

            for (var offset = 0; offset < duration && start + offset < samples.Length; offset++)
            {
                var progress = (float)offset / duration;
                var decay = MathF.Exp(-progress * 8f);
                var tone = MathF.Sin(2 * MathF.PI * frequency * offset / SampleRate);
                samples[start + offset] += tone * decay * amplitude * 0.4f;
            }
        }
    }

    private static void CrossfadeLoop(float[] samples, int loopLength, int fadeLength)
    {
        for (var index = 0; index < fadeLength; index++)
        {
            var blend = (float)index / fadeLength;
            samples[index] = samples[index] * blend + samples[loopLength + index] * (1 - blend);
        }
    }

    private static void Normalize(Span<float> samples, float peak)
    {
        var max = 0f;

        foreach (var sample in samples)
            max = MathF.Max(max, MathF.Abs(sample));

        if (max <= 0.0001f)
            return;

        var scale = peak / max;

        for (var index = 0; index < samples.Length; index++)
            samples[index] *= scale;
    }

    private static byte[] WriteWave(ReadOnlySpan<float> samples)
    {
        var dataLength = samples.Length * (BitsPerSample / 8);
        var buffer = new byte[44 + dataLength];
        var span = buffer.AsSpan();

        "RIFF"u8.CopyTo(span);
        BitConverter.TryWriteBytes(span[4..], 36 + dataLength);
        "WAVE"u8.CopyTo(span[8..]);
        "fmt "u8.CopyTo(span[12..]);
        BitConverter.TryWriteBytes(span[16..], 16);
        BitConverter.TryWriteBytes(span[20..], (short)1);
        BitConverter.TryWriteBytes(span[22..], Channels);
        BitConverter.TryWriteBytes(span[24..], SampleRate);
        BitConverter.TryWriteBytes(span[28..], SampleRate * Channels * (BitsPerSample / 8));
        BitConverter.TryWriteBytes(span[32..], (short)(Channels * (BitsPerSample / 8)));
        BitConverter.TryWriteBytes(span[34..], (short)BitsPerSample);
        "data"u8.CopyTo(span[36..]);
        BitConverter.TryWriteBytes(span[40..], dataLength);

        var data = span[44..];

        for (var index = 0; index < samples.Length; index++)
        {
            var clamped = Math.Clamp(samples[index], -1f, 1f);
            var value = (short)MathF.Round(clamped * short.MaxValue);
            BitConverter.TryWriteBytes(data[(index * 2)..], value);
        }

        return buffer;
    }

    private static int SeedOf(FocusAudioPreset preset) => preset switch
    {
        FocusAudioPreset.WhiteNoise => 17,
        FocusAudioPreset.Rain => 31,
        FocusAudioPreset.Cafe => 47,
        FocusAudioPreset.Fireplace => 61,
        FocusAudioPreset.Lofi => 79,
        _ => 101
    };
}
