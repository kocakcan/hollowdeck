using System;
using System.Buffers.Binary;
using Godot;

namespace Hollowdeck.Audio;

public enum Waveform { Sine, Square, Triangle, Noise }

// Pure float[]-in-[-1,1] synthesis toolkit - no Godot node, just PCM math.
// AudioCues/AudioMusic compose these primitives into the game's actual
// sound effects and music loops; this file only knows about waveforms,
// envelopes, and how to package the result into an AudioStreamWav.
public static class AudioSynth
{
    public const int DefaultMixRate = 44100;

    // phase is threaded through by ref so chained segments (e.g. a pitch
    // glide built from several short Oscillator calls back to back) stay
    // continuous instead of clicking at the segment boundary.
    public static float[] Oscillator(Waveform wave, float freqHz, float durationSec,
        int mixRate, ref double phase, float amplitude = 1f, Random? rng = null)
    {
        int n = Math.Max(0, (int)(durationSec * mixRate));
        var buf = new float[n];
        double phaseInc = freqHz / mixRate;
        for (int i = 0; i < n; i++)
        {
            float v = wave switch
            {
                Waveform.Sine => (float)Math.Sin(phase * Math.Tau),
                Waveform.Square => phase < 0.5 ? 1f : -1f,
                Waveform.Triangle => (float)(4.0 * Math.Abs(phase - Math.Floor(phase + 0.5)) - 1.0),
                Waveform.Noise => (float)((rng ??= new Random()).NextDouble() * 2.0 - 1.0),
                _ => 0f,
            };
            buf[i] = v * amplitude;
            phase = (phase + phaseInc) % 1.0;
        }
        return buf;
    }

    // Sweeps linearly from startHz to endHz over the buffer's duration -
    // used for glide-style cues (card_play's whoosh, damage_hit's pitch
    // drop) without needing to hand-chain many short Oscillator calls.
    public static float[] Glide(Waveform wave, float startHz, float endHz, float durationSec,
        int mixRate, ref double phase, float amplitude = 1f)
    {
        int n = Math.Max(0, (int)(durationSec * mixRate));
        var buf = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = n <= 1 ? 0f : i / (float)(n - 1);
            float freq = startHz + (endHz - startHz) * t;
            float v = wave switch
            {
                Waveform.Sine => (float)Math.Sin(phase * Math.Tau),
                Waveform.Square => phase < 0.5 ? 1f : -1f,
                Waveform.Triangle => (float)(4.0 * Math.Abs(phase - Math.Floor(phase + 0.5)) - 1.0),
                _ => 0f,
            };
            buf[i] = v * amplitude;
            phase = (phase + freq / mixRate) % 1.0;
        }
        return buf;
    }

    // Linear ADSR - enough fidelity for short programmer-authored cues;
    // returns an envelope buffer meant to be multiplied onto a signal via
    // ApplyEnvelope.
    public static float[] Adsr(int totalSamples, int mixRate,
        float attackSec, float decaySec, float sustainLevel, float releaseSec)
    {
        var env = new float[totalSamples];
        int a = (int)(attackSec * mixRate);
        int d = (int)(decaySec * mixRate);
        int r = (int)(releaseSec * mixRate);
        int sustainStart = a + d;
        int releaseStart = Math.Max(sustainStart, totalSamples - r);
        for (int i = 0; i < totalSamples; i++)
        {
            env[i] = i < a ? i / (float)Math.Max(a, 1)
                : i < sustainStart ? Lerp(1f, sustainLevel, (i - a) / (float)Math.Max(d, 1))
                : i < releaseStart ? sustainLevel
                : Lerp(sustainLevel, 0f, (i - releaseStart) / (float)Math.Max(totalSamples - releaseStart, 1));
        }
        return env;
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * Math.Clamp(t, 0f, 1f);

    public static float[] ApplyEnvelope(float[] samples, float[] envelope)
    {
        int n = Math.Min(samples.Length, envelope.Length);
        var result = new float[n];
        for (int i = 0; i < n; i++) result[i] = samples[i] * envelope[i];
        return result;
    }

    // Sums layers (padding shorter ones with silence) then peak-normalizes
    // if the mix exceeds [-1,1] - a soft ceiling rather than hard-clamping,
    // so combining several oscillators never introduces audible clipping.
    public static float[] Mix(params float[][] layers)
    {
        int n = 0;
        foreach (var layer in layers) n = Math.Max(n, layer.Length);
        var result = new float[n];
        foreach (var layer in layers)
            for (int i = 0; i < layer.Length; i++)
                result[i] += layer[i];

        float peak = 0f;
        foreach (var v in result) peak = Math.Max(peak, Math.Abs(v));
        if (peak > 1f)
            for (int i = 0; i < n; i++) result[i] /= peak;
        return result;
    }

    // Single-pole IIR filters - enough to shape "high-passed noise burst" /
    // "low-passed thud" style cues without a real biquad/EQ implementation.
    public static float[] OnePoleHighPass(float[] input, float cutoffHz, int mixRate)
    {
        float rc = 1f / (2f * MathF.PI * cutoffHz);
        float dt = 1f / mixRate;
        float a = rc / (rc + dt);
        var output = new float[input.Length];
        float prevIn = 0f, prevOut = 0f;
        for (int i = 0; i < input.Length; i++)
        {
            output[i] = a * (prevOut + input[i] - prevIn);
            prevIn = input[i];
            prevOut = output[i];
        }
        return output;
    }

    public static float[] OnePoleLowPass(float[] input, float cutoffHz, int mixRate)
    {
        float dt = 1f / mixRate;
        float rc = 1f / (2f * MathF.PI * cutoffHz);
        float a = dt / (rc + dt);
        var output = new float[input.Length];
        float prevOut = 0f;
        for (int i = 0; i < input.Length; i++)
        {
            prevOut += a * (input[i] - prevOut);
            output[i] = prevOut;
        }
        return output;
    }

    // Click-free looping: render loopSamples+fadeSamples of material, then
    // equal-power-crossfade the tail (fadeSamples) into the head, trimming
    // back down to exactly loopSamples. Works even for non-harmonic content
    // (filtered noise, detuned layers) unlike relying on the buffer length
    // being an exact integer number of cycles of the fundamental.
    public static float[] CrossfadeLoop(float[] rendered, int loopSamples, int fadeSamples)
    {
        fadeSamples = Math.Min(fadeSamples, loopSamples / 2);
        var result = new float[loopSamples];
        Array.Copy(rendered, result, loopSamples);
        int tailStart = loopSamples;
        for (int i = 0; i < fadeSamples; i++)
        {
            float t = i / (float)fadeSamples;
            float fadeIn = MathF.Sin(t * MathF.PI * 0.5f);
            float fadeOut = MathF.Cos(t * MathF.PI * 0.5f);
            float tailSample = tailStart + i < rendered.Length ? rendered[tailStart + i] : 0f;
            result[i] = result[i] * fadeIn + tailSample * fadeOut;
        }
        return result;
    }

    // Concatenates segments end to end - used for note sequences (arpeggios,
    // two-note "clink"/"chime" cues) built from several short Oscillator+
    // envelope calls.
    public static float[] Concat(params float[][] segments)
    {
        int total = 0;
        foreach (var s in segments) total += s.Length;
        var result = new float[total];
        int offset = 0;
        foreach (var s in segments)
        {
            Array.Copy(s, 0, result, offset, s.Length);
            offset += s.Length;
        }
        return result;
    }

    public static AudioStreamWav BuildWav(float[] samples, int mixRate,
        AudioStreamWav.LoopModeEnum loop = AudioStreamWav.LoopModeEnum.Disabled,
        int loopBeginFrame = 0, int loopEndFrame = 0)
    {
        var data = new byte[samples.Length * 2];
        for (int i = 0; i < samples.Length; i++)
        {
            short s = (short)Math.Clamp(samples[i] * 32767f, short.MinValue, short.MaxValue);
            BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(i * 2, 2), s);
        }
        return new AudioStreamWav
        {
            Data = data,
            Format = AudioStreamWav.FormatEnum.Format16Bits,
            MixRate = mixRate,
            Stereo = false,
            LoopMode = loop,
            LoopBegin = loopBeginFrame,
            LoopEnd = loopEndFrame == 0 ? samples.Length : loopEndFrame,
        };
    }
}
