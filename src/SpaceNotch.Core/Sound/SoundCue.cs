namespace SpaceNotch.Core.Sound;

/// <summary>Les trois sons discrets de la notch (D3).</summary>
public enum SoundCueKind
{
    /// <summary>La notch s'ouvre sous la main.</summary>
    Open,

    /// <summary>Un fichier est déposé sur l'étagère.</summary>
    Drop,

    /// <summary>Un minuteur ou une session de focus arrive à son terme.</summary>
    TimerDone
}

/// <summary>
/// Sons discrets (D3) : trois sons très courts et doux, synthétisés — aucun
/// fichier audio embarqué. Chaque son est une somme de sinus sous une
/// enveloppe qui part et revient à zéro, pour ne jamais claquer. Le volume
/// est bas (crête à <see cref="Peak"/>) et suit celui de Windows, puisque le
/// son passe par la sortie de l'application. Désactivés par défaut ; jamais
/// en « Ne pas déranger » (l'appelant le vérifie).
/// </summary>
public static class SoundCue
{
    public const int SampleRate = 44100;

    /// <summary>Crête maximale, en fraction de la pleine échelle (≈ −16 dB).</summary>
    public const double Peak = 0.16;

    /// <summary>Échantillons du son, entre −1 et 1.</summary>
    public static float[] Samples(SoundCueKind kind) => kind switch
    {
        // Deux notes montantes, une quinte : une ouverture.
        SoundCueKind.Open => Mix(Note(659.25, 0.00, 0.09), Note(987.77, 0.05, 0.12)),

        // Un « plop » grave et bref : quelque chose se pose.
        SoundCueKind.Drop => Mix(Glide(420, 240, 0.00, 0.11)),

        // Trois carillons doux : c'est fini.
        SoundCueKind.TimerDone => Mix(Note(880, 0.00, 0.22), Note(1108.73, 0.16, 0.22), Note(1318.51, 0.32, 0.34)),

        _ => []
    };

    /// <summary>Le son en WAV PCM 16 bits mono, prêt pour <c>PlaySound(SND_MEMORY)</c>.</summary>
    public static byte[] Wav(SoundCueKind kind)
    {
        float[] samples = Samples(kind);
        int dataBytes = samples.Length * 2;
        using var stream = new MemoryStream(44 + dataBytes);
        using var writer = new BinaryWriter(stream);

        writer.Write("RIFF"u8);
        writer.Write(36 + dataBytes);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(SampleRate);
        writer.Write(SampleRate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(dataBytes);

        foreach (float sample in samples)
        {
            writer.Write((short)Math.Round(Math.Clamp(sample, -1f, 1f) * short.MaxValue));
        }

        writer.Flush();
        return stream.ToArray();
    }

    private readonly record struct Voice(double StartHz, double EndHz, double Start, double Length);

    private static Voice Note(double hz, double start, double length) => new(hz, hz, start, length);

    private static Voice Glide(double fromHz, double toHz, double start, double length) => new(fromHz, toHz, start, length);

    private static float[] Mix(params Voice[] voices)
    {
        double end = voices.Max(v => v.Start + v.Length);
        var samples = new float[(int)Math.Ceiling(end * SampleRate) + 1];

        foreach (Voice voice in voices)
        {
            int first = (int)(voice.Start * SampleRate);
            int count = (int)(voice.Length * SampleRate);
            double phase = 0;

            for (int i = 0; i < count && first + i < samples.Length; i++)
            {
                double t = (double)i / count;
                double hz = voice.StartHz + ((voice.EndHz - voice.StartHz) * t);
                phase += 2 * Math.PI * hz / SampleRate;

                // Attaque de 6 ms, puis décroissance douce jusqu'à zéro.
                double attack = Math.Min(1, i / (0.006 * SampleRate));
                double envelope = attack * Math.Pow(1 - t, 2.2);
                double tone = Math.Sin(phase) + (0.18 * Math.Sin(2 * phase));
                samples[first + i] += (float)(tone * envelope);
            }
        }

        float loudest = samples.Length == 0 ? 0 : samples.Max(Math.Abs);

        if (loudest > 0)
        {
            float gain = (float)(Peak / loudest);

            for (int i = 0; i < samples.Length; i++)
            {
                samples[i] *= gain;
            }
        }

        return samples;
    }
}
