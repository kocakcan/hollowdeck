using Godot;

namespace Hollowdeck.Audio;

// The game's 10 procedurally synthesized SFX, one per gameplay/UI category
// (not per-card/per-enemy - see the Phase 5 plan's scope cap). Each Build*
// method is pure and cheap enough to call once at AudioManager startup and
// cache the resulting AudioStreamWav.
public static class AudioCues
{
    private const int Rate = AudioSynth.DefaultMixRate;

    public static AudioStreamWav BuildUiClick()
    {
        double phase = 0;
        var tone = AudioSynth.Glide(Waveform.Square, 900, 600, 0.06f, Rate, ref phase, 0.5f);
        var env = AudioSynth.Adsr(tone.Length, Rate, 0.002f, 0.02f, 0.3f, 0.03f);
        return AudioSynth.BuildWav(AudioSynth.ApplyEnvelope(tone, env), Rate);
    }

    public static AudioStreamWav BuildCardPlay()
    {
        double glidePhase = 0, noisePhase = 0;
        var glide = AudioSynth.Glide(Waveform.Sine, 400, 900, 0.15f, Rate, ref glidePhase, 0.6f);
        var noise = AudioSynth.Oscillator(Waveform.Noise, 0, 0.15f, Rate, ref noisePhase, 0.5f);
        noise = AudioSynth.OnePoleHighPass(noise, 2000, Rate);
        var env = AudioSynth.Adsr(glide.Length, Rate, 0.005f, 0.05f, 0.4f, 0.08f);
        var mixed = AudioSynth.Mix(glide, noise);
        return AudioSynth.BuildWav(AudioSynth.ApplyEnvelope(mixed, env), Rate);
    }

    public static AudioStreamWav BuildCardDraw()
    {
        double phase = 0;
        var tone = AudioSynth.Oscillator(Waveform.Triangle, 700, 0.08f, Rate, ref phase, 0.5f);
        var env = AudioSynth.Adsr(tone.Length, Rate, 0.001f, 0.03f, 0.1f, 0.04f);
        return AudioSynth.BuildWav(AudioSynth.ApplyEnvelope(tone, env), Rate);
    }

    public static AudioStreamWav BuildDamageHit()
    {
        double thumpPhase = 0, noisePhase = 0;
        var thump = AudioSynth.Oscillator(Waveform.Sine, 90, 0.12f, Rate, ref thumpPhase, 0.8f);
        var noise = AudioSynth.Oscillator(Waveform.Noise, 0, 0.12f, Rate, ref noisePhase, 0.6f);
        noise = AudioSynth.OnePoleLowPass(noise, 400, Rate);
        var env = AudioSynth.Adsr(thump.Length, Rate, 0.001f, 0.04f, 0.2f, 0.05f);
        var mixed = AudioSynth.Mix(thump, noise);
        return AudioSynth.BuildWav(AudioSynth.ApplyEnvelope(mixed, env), Rate);
    }

    public static AudioStreamWav BuildBlockGain()
    {
        double phase1 = 0, phase2 = 0;
        var note1 = AudioSynth.Oscillator(Waveform.Square, 659f, 0.05f, Rate, ref phase1, 0.4f);
        var note2 = AudioSynth.Oscillator(Waveform.Square, 880f, 0.05f, Rate, ref phase2, 0.4f);
        var env1 = AudioSynth.Adsr(note1.Length, Rate, 0.002f, 0.02f, 0.3f, 0.02f);
        var env2 = AudioSynth.Adsr(note2.Length, Rate, 0.002f, 0.02f, 0.3f, 0.02f);
        var sequence = AudioSynth.Concat(
            AudioSynth.ApplyEnvelope(note1, env1),
            AudioSynth.ApplyEnvelope(note2, env2));
        return AudioSynth.BuildWav(sequence, Rate);
    }

    public static AudioStreamWav BuildStatusApply()
    {
        double sinePhase = 0, squarePhase = 0;
        var sine = AudioSynth.Glide(Waveform.Sine, 500, 300, 0.18f, Rate, ref sinePhase, 0.6f);
        var detune = AudioSynth.Glide(Waveform.Square, 510, 306, 0.18f, Rate, ref squarePhase, 0.2f);
        var env = AudioSynth.Adsr(sine.Length, Rate, 0.01f, 0.05f, 0.4f, 0.08f);
        var mixed = AudioSynth.Mix(sine, detune);
        return AudioSynth.BuildWav(AudioSynth.ApplyEnvelope(mixed, env), Rate);
    }

    public static AudioStreamWav BuildHeal()
    {
        float[] freqs = { 523.25f, 659.25f, 783.99f }; // C5 E5 G5
        var notes = new float[freqs.Length][];
        for (int i = 0; i < freqs.Length; i++)
        {
            double phase = 0;
            var note = AudioSynth.Oscillator(Waveform.Sine, freqs[i], 0.09f, Rate, ref phase, 0.5f);
            var env = AudioSynth.Adsr(note.Length, Rate, 0.02f, 0.03f, 0.5f, 0.04f);
            notes[i] = AudioSynth.ApplyEnvelope(note, env);
        }
        return AudioSynth.BuildWav(AudioSynth.Concat(notes), Rate);
    }

    public static AudioStreamWav BuildEnergyGain()
    {
        double sweepPhase = 0, sparklePhase = 0;
        var sweep = AudioSynth.Glide(Waveform.Sine, 400, 1200, 0.15f, Rate, ref sweepPhase, 0.6f);
        var sparkle = AudioSynth.Oscillator(Waveform.Triangle, 1600, 0.15f, Rate, ref sparklePhase, 0.15f);
        var env = AudioSynth.Adsr(sweep.Length, Rate, 0.01f, 0.04f, 0.4f, 0.06f);
        var mixed = AudioSynth.Mix(sweep, sparkle);
        return AudioSynth.BuildWav(AudioSynth.ApplyEnvelope(mixed, env), Rate);
    }

    public static AudioStreamWav BuildEnemyDeath()
    {
        double glidePhase = 0, noisePhase = 0;
        var glide = AudioSynth.Glide(Waveform.Sine, 300, 60, 0.3f, Rate, ref glidePhase, 0.7f);
        var noise = AudioSynth.Oscillator(Waveform.Noise, 0, 0.1f, Rate, ref noisePhase, 0.5f);
        noise = AudioSynth.OnePoleLowPass(noise, 800, Rate);
        var env = AudioSynth.Adsr(glide.Length, Rate, 0.005f, 0.08f, 0.3f, 0.18f);
        var mixed = AudioSynth.Mix(glide, noise);
        return AudioSynth.BuildWav(AudioSynth.ApplyEnvelope(mixed, env), Rate);
    }

    public static AudioStreamWav BuildRewardPickup()
    {
        float[] freqs = { 523.25f, 783.99f }; // C5 G5
        var notes = new float[freqs.Length][];
        for (int i = 0; i < freqs.Length; i++)
        {
            double phase = 0, overtonePhase = 0;
            var note = AudioSynth.Oscillator(Waveform.Sine, freqs[i], 0.1f, Rate, ref phase, 0.55f);
            var overtone = AudioSynth.Oscillator(Waveform.Triangle, freqs[i] * 2f, 0.1f, Rate, ref overtonePhase, 0.15f);
            var env = AudioSynth.Adsr(note.Length, Rate, 0.01f, 0.03f, 0.4f, 0.05f);
            notes[i] = AudioSynth.ApplyEnvelope(AudioSynth.Mix(note, overtone), env);
        }
        return AudioSynth.BuildWav(AudioSynth.Concat(notes), Rate);
    }
}
