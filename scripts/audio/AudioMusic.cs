using System;
using Godot;

namespace Hollowdeck.Audio;

// Programmer-authored generative music: a sustained drone plus a sparse
// arpeggio, not hand-composed. Rendered at 22050Hz (plenty for this
// content, keeps synth time/memory down for ~20s loops). The two loop
// tracks use AudioSynth.CrossfadeLoop for a click-free seam; the two
// stingers are one-shot (LoopMode.Disabled).
public static class AudioMusic
{
    private const int Rate = 22050;

    public static AudioStreamWav BuildAmbientLoop()
    {
        const float loopSeconds = 24f;
        const float fadeSeconds = 0.3f;
        int loopSamples = (int)(loopSeconds * Rate);
        int fadeSamples = (int)(fadeSeconds * Rate);
        int renderSamples = loopSamples + fadeSamples;
        float renderSeconds = renderSamples / (float)Rate;

        double rootPhase = 0, fifthPhase = 0;
        var root = EnsureLength(AudioSynth.Oscillator(Waveform.Sine, 55f, renderSeconds, Rate, ref rootPhase, 0.22f), renderSamples);
        var fifth = EnsureLength(AudioSynth.Oscillator(Waveform.Sine, 82.5f, renderSeconds, Rate, ref fifthPhase, 0.16f), renderSamples);
        var drone = AudioSynth.Mix(root, fifth);

        float[] arpeggioFreqs = { 220f, 277.18f, 329.63f, 440f }; // A major arpeggio (A3 C#4 E4 A4)
        var arpeggio = new float[renderSamples];
        int noteInterval = (int)(1.5f * Rate);
        int noteIndex = 0;
        for (int offset = 0; offset < renderSamples; offset += noteInterval, noteIndex++)
        {
            double phase = 0;
            var note = AudioSynth.Oscillator(Waveform.Triangle, arpeggioFreqs[noteIndex % arpeggioFreqs.Length],
                0.6f, Rate, ref phase, 0.3f);
            var env = AudioSynth.Adsr(note.Length, Rate, 0.05f, 0.2f, 0.3f, 0.3f);
            AddAt(arpeggio, AudioSynth.ApplyEnvelope(note, env), offset);
        }

        var mixed = AudioSynth.Mix(drone, arpeggio);
        var looped = AudioSynth.CrossfadeLoop(mixed, loopSamples, fadeSamples);
        return AudioSynth.BuildWav(looped, Rate, AudioStreamWav.LoopModeEnum.Forward, 0, loopSamples);
    }

    public static AudioStreamWav BuildCombatLoop()
    {
        const float loopSeconds = 16f;
        const float fadeSeconds = 0.3f;
        int loopSamples = (int)(loopSeconds * Rate);
        int fadeSamples = (int)(fadeSeconds * Rate);
        int renderSamples = loopSamples + fadeSamples;
        float renderSeconds = renderSamples / (float)Rate;

        double rootPhase = 0, thirdPhase = 0, noisePhase = 0;
        var root = EnsureLength(AudioSynth.Oscillator(Waveform.Sine, 65.41f, renderSeconds, Rate, ref rootPhase, 0.22f), renderSamples);
        var minorThird = EnsureLength(AudioSynth.Oscillator(Waveform.Sine, 77.78f, renderSeconds, Rate, ref thirdPhase, 0.16f), renderSamples);
        var noise = EnsureLength(AudioSynth.Oscillator(Waveform.Noise, 0, renderSeconds, Rate, ref noisePhase, 0.35f), renderSamples);
        noise = AudioSynth.OnePoleLowPass(noise, 300, Rate);
        var drone = AudioSynth.Mix(root, minorThird, noise);

        // Slow amplitude LFO on the drone for a subtle pulsing feel.
        var lfo = LfoEnvelope(renderSamples, Rate, 0.15f, 0.75f, 0.25f);
        drone = AudioSynth.ApplyEnvelope(drone, lfo);

        float[] arpeggioFreqs = { 130.81f, 155.56f, 196.00f, 261.63f }; // C minor arpeggio (C3 Eb3 G3 C4)
        var arpeggio = new float[renderSamples];
        int noteInterval = (int)(0.5f * Rate);
        int noteIndex = 0;
        for (int offset = 0; offset < renderSamples; offset += noteInterval, noteIndex++)
        {
            double phase = 0;
            var note = AudioSynth.Oscillator(Waveform.Triangle, arpeggioFreqs[noteIndex % arpeggioFreqs.Length],
                0.22f, Rate, ref phase, 0.28f);
            var env = AudioSynth.Adsr(note.Length, Rate, 0.01f, 0.06f, 0.3f, 0.1f);
            AddAt(arpeggio, AudioSynth.ApplyEnvelope(note, env), offset);
        }

        var mixed = AudioSynth.Mix(drone, arpeggio);
        var looped = AudioSynth.CrossfadeLoop(mixed, loopSamples, fadeSamples);
        return AudioSynth.BuildWav(looped, Rate, AudioStreamWav.LoopModeEnum.Forward, 0, loopSamples);
    }

    public static AudioStreamWav BuildVictoryStinger()
    {
        float[] freqs = { 523.25f, 659.25f, 783.99f, 1046.50f }; // C5 E5 G5 C6
        var notes = new float[freqs.Length][];
        for (int i = 0; i < freqs.Length; i++)
        {
            double sinePhase = 0, trianglePhase = 0;
            var sine = AudioSynth.Oscillator(Waveform.Sine, freqs[i], 0.6f, Rate, ref sinePhase, 0.45f);
            var triangle = AudioSynth.Oscillator(Waveform.Triangle, freqs[i], 0.6f, Rate, ref trianglePhase, 0.2f);
            var env = AudioSynth.Adsr(sine.Length, Rate, 0.02f, 0.1f, 0.6f, 0.25f);
            notes[i] = AudioSynth.ApplyEnvelope(AudioSynth.Mix(sine, triangle), env);
        }
        var samples = AudioSynth.Concat(notes);
        return AudioSynth.BuildWav(samples, Rate);
    }

    public static AudioStreamWav BuildDefeatStinger()
    {
        double phase = 0;
        var glide = AudioSynth.Glide(Waveform.Sine, 220, 90, 3f, Rate, ref phase, 0.55f);
        var env = AudioSynth.Adsr(glide.Length, Rate, 0.1f, 0.5f, 0.4f, 1.5f);
        return AudioSynth.BuildWav(AudioSynth.ApplyEnvelope(glide, env), Rate);
    }

    private static float[] EnsureLength(float[] samples, int length)
    {
        if (samples.Length == length) return samples;
        var resized = new float[length];
        Array.Copy(samples, resized, Math.Min(samples.Length, length));
        return resized;
    }

    private static void AddAt(float[] target, float[] source, int offset)
    {
        for (int i = 0; i < source.Length; i++)
        {
            int idx = offset + i;
            if (idx >= target.Length) break;
            target[idx] += source[i];
        }
    }

    private static float[] LfoEnvelope(int samples, int mixRate, float lfoHz, float baseLevel, float depth)
    {
        var env = new float[samples];
        for (int i = 0; i < samples; i++)
        {
            float t = i / (float)mixRate;
            env[i] = baseLevel + depth * MathF.Sin(2f * MathF.PI * lfoHz * t);
        }
        return env;
    }
}
