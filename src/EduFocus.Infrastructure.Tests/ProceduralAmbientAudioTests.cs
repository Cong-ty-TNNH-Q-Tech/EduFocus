using System.Buffers.Binary;
using System.Text;
using EduFocus.Infrastructure.FocusAudio;
using EduFocus.Ports.Outbound.Services;
using FluentAssertions;

namespace EduFocus.Infrastructure.Tests;

public sealed class ProceduralAmbientAudioTests
{
    public static TheoryData<FocusAudioPreset> Presets =>
    [
        FocusAudioPreset.WhiteNoise,
        FocusAudioPreset.Rain,
        FocusAudioPreset.Cafe,
        FocusAudioPreset.Fireplace,
        FocusAudioPreset.Lofi
    ];

    [Theory]
    [MemberData(nameof(Presets))]
    public void Wave_is_a_valid_mono_pcm_stream(FocusAudioPreset preset)
    {
        var wave = ProceduralAmbientAudio.CreateWave(preset);

        Encoding.ASCII.GetString(wave, 0, 4).Should().Be("RIFF");
        Encoding.ASCII.GetString(wave, 8, 4).Should().Be("WAVE");
        Encoding.ASCII.GetString(wave, 12, 4).Should().Be("fmt ");
        Encoding.ASCII.GetString(wave, 36, 4).Should().Be("data");

        BinaryPrimitives.ReadInt16LittleEndian(wave.AsSpan(20, 2)).Should().Be(1);
        BinaryPrimitives.ReadInt16LittleEndian(wave.AsSpan(22, 2)).Should().Be(1);
        BinaryPrimitives.ReadInt32LittleEndian(wave.AsSpan(24, 4))
            .Should().Be(ProceduralAmbientAudio.SampleRate);
        BinaryPrimitives.ReadInt16LittleEndian(wave.AsSpan(34, 2)).Should().Be(16);

        var dataLength = BinaryPrimitives.ReadInt32LittleEndian(wave.AsSpan(40, 4));
        dataLength.Should().Be(wave.Length - 44);
        dataLength.Should().Be(
            ProceduralAmbientAudio.SampleRate * ProceduralAmbientAudio.LoopSeconds * 2);
        BinaryPrimitives.ReadInt32LittleEndian(wave.AsSpan(4, 4)).Should().Be(36 + dataLength);
    }

    [Theory]
    [MemberData(nameof(Presets))]
    public void Samples_stay_inside_the_pcm_range_and_are_not_silent(FocusAudioPreset preset)
    {
        var wave = ProceduralAmbientAudio.CreateWave(preset);
        var peak = 0;

        for (var offset = 44; offset < wave.Length; offset += 2)
        {
            var sample = BinaryPrimitives.ReadInt16LittleEndian(wave.AsSpan(offset, 2));
            peak = Math.Max(peak, Math.Abs((int)sample));
        }

        peak.Should().BeGreaterThan(1_000);
        peak.Should().BeLessThanOrEqualTo(short.MaxValue);
    }

    [Fact]
    public void Generation_is_deterministic_per_preset()
    {
        var first = ProceduralAmbientAudio.CreateWave(FocusAudioPreset.Rain);
        var second = ProceduralAmbientAudio.CreateWave(FocusAudioPreset.Rain);

        first.Should().Equal(second);
    }

    [Fact]
    public void Presets_sound_different_from_each_other()
    {
        var whiteNoise = ProceduralAmbientAudio.CreateWave(FocusAudioPreset.WhiteNoise);
        var rain = ProceduralAmbientAudio.CreateWave(FocusAudioPreset.Rain);

        whiteNoise.Should().NotEqual(rain);
    }
}
