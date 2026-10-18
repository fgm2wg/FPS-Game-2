using System;
using System.IO;
using Godot;

namespace FPSGame.VFX.Impacts;

/// <summary>
/// Pre-computes and caches high-fidelity visual and acoustic explosion assets
/// (procedural soft smoke puff, spark streaks, charred crater decal, and seismic explosion WAV).
/// Guarantees that explosion particles are smooth, organic, and realistic (never blocky).
/// </summary>
public static class ExplosionAssetGenerator
{
    private const string AudioDir = "res://assets/audio/sfx/weapons";
    private const string ParticleDir = "res://assets/textures/particles";
    private const string DecalDir = "res://assets/textures/decals";

    public const string ExplosionAudioPath = "res://assets/audio/sfx/weapons/explosion.mp3";
    public const string SmokeTexturePath = "res://assets/textures/particles/explosion_smoke.png";
    public const string SparkTexturePath = "res://assets/textures/particles/explosion_spark.png";
    public const string ScorchTexturePath = "res://assets/textures/decals/explosion_scorch.png";

    public static void EnsureAssetsExist()
    {
        string globalAudioDir = ProjectSettings.GlobalizePath(AudioDir);
        string globalParticleDir = ProjectSettings.GlobalizePath(ParticleDir);
        string globalDecalDir = ProjectSettings.GlobalizePath(DecalDir);

        if (!Directory.Exists(globalAudioDir)) Directory.CreateDirectory(globalAudioDir);
        if (!Directory.Exists(globalParticleDir)) Directory.CreateDirectory(globalParticleDir);
        if (!Directory.Exists(globalDecalDir)) Directory.CreateDirectory(globalDecalDir);

        string globalAudioPath = ProjectSettings.GlobalizePath(ExplosionAudioPath);
        if ((!File.Exists(globalAudioPath) || new FileInfo(globalAudioPath).Length < 1000) && globalAudioPath.EndsWith(".wav"))
        {
            GenerateExplosionWav(globalAudioPath);
        }

        string globalSmokePath = ProjectSettings.GlobalizePath(SmokeTexturePath);
        if (!File.Exists(globalSmokePath))
        {
            GenerateSmokeTexture(globalSmokePath);
        }

        string globalSparkPath = ProjectSettings.GlobalizePath(SparkTexturePath);
        if (!File.Exists(globalSparkPath))
        {
            GenerateSparkTexture(globalSparkPath);
        }

        string globalScorchPath = ProjectSettings.GlobalizePath(ScorchTexturePath);
        if (!File.Exists(globalScorchPath))
        {
            GenerateScorchTexture(globalScorchPath);
        }
    }

    private static Texture2D? _cachedSmoke;
    private static Texture2D? _cachedSpark;
    private static Texture2D? _cachedScorch;

    public static Texture2D GetSmokeTexture()
    {
        if (_cachedSmoke != null) return _cachedSmoke;
        EnsureAssetsExist();

        if (ResourceLoader.Exists(SmokeTexturePath))
        {
            try
            {
                var res = ResourceLoader.Load<Texture2D>(SmokeTexturePath);
                if (res != null) { _cachedSmoke = res; return res; }
            }
            catch { }
        }

        string global = ProjectSettings.GlobalizePath(SmokeTexturePath);
        var img = Image.LoadFromFile(global);
        _cachedSmoke = ImageTexture.CreateFromImage(img);
        return _cachedSmoke;
    }

    public static Texture2D GetSparkTexture()
    {
        if (_cachedSpark != null) return _cachedSpark;
        EnsureAssetsExist();

        if (ResourceLoader.Exists(SparkTexturePath))
        {
            try
            {
                var res = ResourceLoader.Load<Texture2D>(SparkTexturePath);
                if (res != null) { _cachedSpark = res; return res; }
            }
            catch { }
        }

        string global = ProjectSettings.GlobalizePath(SparkTexturePath);
        var img = Image.LoadFromFile(global);
        _cachedSpark = ImageTexture.CreateFromImage(img);
        return _cachedSpark;
    }

    public static Texture2D GetScorchTexture()
    {
        if (_cachedScorch != null) return _cachedScorch;
        EnsureAssetsExist();

        if (ResourceLoader.Exists(ScorchTexturePath))
        {
            try
            {
                var res = ResourceLoader.Load<Texture2D>(ScorchTexturePath);
                if (res != null) { _cachedScorch = res; return res; }
            }
            catch { }
        }

        string global = ProjectSettings.GlobalizePath(ScorchTexturePath);
        var img = Image.LoadFromFile(global);
        _cachedScorch = ImageTexture.CreateFromImage(img);
        return _cachedScorch;
    }

    private static AudioStream? _cachedAudio;

    public static AudioStream? GetExplosionAudio()
    {
        if (_cachedAudio != null) return _cachedAudio;
        EnsureAssetsExist();
        _cachedAudio = ResourceLoader.Load<AudioStream>(ExplosionAudioPath);
        return _cachedAudio;
    }

    /// <summary>
    /// Synthesizes a 44.1kHz 16-bit mono cinematic explosion WAV.
    /// Features: supersonic shockwave transient crack, deep saturated sub-bass thump (42-80 Hz),
    /// turbulent fire rumble with two-pole low-pass filtering, and an acoustic landscape reverberation tail.
    /// </summary>
    public static void GenerateExplosionWav(string destinationPath)
    {
        const int sampleRate = 44100;
        const float duration = 2.4f;
        int totalSamples = (int)(sampleRate * duration);
        const int bytesPerSample = 2;
        const int channels = 1;
        int dataChunkSize = totalSamples * bytesPerSample * channels;

        using var fs = new FileStream(destinationPath, System.IO.FileMode.Create, System.IO.FileAccess.Write);
        using var bw = new BinaryWriter(fs);

        // RIFF header
        bw.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
        bw.Write(dataChunkSize + 36);
        bw.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));

        // fmt chunk
        bw.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
        bw.Write(16); // Chunk size
        bw.Write((short)1); // PCM format
        bw.Write((short)channels);
        bw.Write(sampleRate);
        bw.Write(sampleRate * channels * bytesPerSample);
        bw.Write((short)(channels * bytesPerSample));
        bw.Write((short)16); // Bits per sample

        // data chunk
        bw.Write(System.Text.Encoding.ASCII.GetBytes("data"));
        bw.Write(dataChunkSize);

        var rand = new Random(1337);

        // 2-pole lowpass filter state for turbulent noise
        double lp1 = 0.0, lp2 = 0.0;

        // Multi-tap comb delay lines for acoustic environmental reverberation
        int dLen1 = (int)(sampleRate * 0.068);
        int dLen2 = (int)(sampleRate * 0.109);
        int dLen3 = (int)(sampleRate * 0.147);
        double[] buf1 = new double[dLen1];
        double[] buf2 = new double[dLen2];
        double[] buf3 = new double[dLen3];
        int idx1 = 0, idx2 = 0, idx3 = 0;

        for (int i = 0; i < totalSamples; i++)
        {
            double t = (double)i / sampleRate;

            // 1. Supersonic blast wave transient crack (0 to 35ms)
            double crack = 0.0;
            if (t < 0.045)
            {
                double crackEnv = Math.Exp(-t * 95.0);
                double crackFreq = 650.0 * Math.Exp(-t * 85.0) + 90.0;
                double crackNoise = (rand.NextDouble() * 2.0 - 1.0) * 0.75;
                crack = (Math.Sin(2.0 * Math.PI * crackFreq * t) + crackNoise) * crackEnv * 1.35;
            }

            // 2. Heavy seismic sub-bass thump (42-80 Hz) with non-linear saturation
            double bassFreq = 40.0 + 42.0 * Math.Exp(-t * 4.5);
            double bassAttack = Math.Sin(Math.Min(Math.PI * 0.5, t * 280.0));
            double bassDecay = Math.Exp(-t * 2.2);
            double bassRaw = Math.Sin(2.0 * Math.PI * bassFreq * t) * bassAttack * bassDecay * 1.5;
            double bass = Math.Tanh(bassRaw * 1.6); // Warm saturation harmonics

            // 3. Turbulent fire and blast noise with dynamic 2-pole lowpass filtering
            double white = rand.NextDouble() * 2.0 - 1.0;
            double cutoff = 110.0 + 750.0 * Math.Exp(-t * 3.8); // Filter opens and rolls off
            double rc = 1.0 / (2.0 * Math.PI * cutoff);
            double dt = 1.0 / sampleRate;
            double alpha = dt / (rc + dt);
            lp1 += alpha * (white - lp1);
            lp2 += alpha * (lp1 - lp2);

            double noiseAttack = Math.Sin(Math.Min(Math.PI * 0.5, t * 180.0));
            double noiseDecay = Math.Exp(-t * 1.85);
            double noise = lp2 * noiseAttack * noiseDecay * 2.4;

            // 4. Shrapnel / debris scatter micro-bursts
            double debris = 0.0;
            if (t > 0.04 && t < 1.4)
            {
                if (rand.NextDouble() < (0.018 * Math.Exp(-t * 1.4)))
                {
                    debris = (rand.NextDouble() * 2.0 - 1.0) * 0.5 * Math.Exp(-t);
                }
            }

            double dry = crack + bass + noise + debris;

            // 5. Reverberation feedback loops
            double echo1 = buf1[idx1];
            double echo2 = buf2[idx2];
            double echo3 = buf3[idx3];

            buf1[idx1] = dry * 0.50 + echo1 * 0.42;
            buf2[idx2] = dry * 0.40 + echo2 * 0.38;
            buf3[idx3] = dry * 0.35 + echo3 * 0.35;

            idx1 = (idx1 + 1) % dLen1;
            idx2 = (idx2 + 1) % dLen2;
            idx3 = (idx3 + 1) % dLen3;

            double wet = (echo1 + echo2 + echo3) * 0.35;
            double mix = dry * 0.85 + wet;

            // Master soft limiter
            double limited = Math.Tanh(mix * 0.88);
            short pcm = (short)Math.Clamp(Math.Round(limited * 32000.0), -32767.0, 32767.0);
            bw.Write(pcm);
        }

        bw.Flush();
        GD.Print($"[ExplosionAssetGenerator] Synthesized explosion WAV at {destinationPath}");
    }

    /// <summary>
    /// Generates a 256x256 smooth, organic billowing smoke/fire puff texture.
    /// Combines radial exponential falloff with multi-octave sinusoidal turbulence so particles
    /// look like rich volumetric smoke plumes rather than flat or blocky quads.
    /// </summary>
    public static void GenerateSmokeTexture(string destinationPath)
    {
        const int size = 256;
        var image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        float center = size * 0.5f;
        float maxRadius = center - 8.0f; // Pure transparent 8px margin to eliminate billboard edge clamping

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x - center;
                float dy = y - center;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);

                if (dist >= maxRadius)
                {
                    image.SetPixel(x, y, new Color(1, 1, 1, 0));
                    continue;
                }

                float normDist = dist / maxRadius;
                float angle = Mathf.Atan2(dy, dx);

                // Multi-octave harmonic turbulence (simulates smoke billow lobes)
                float lobe1 = Mathf.Sin(angle * 3.0f + 0.4f) * 0.16f;
                float lobe2 = Mathf.Cos(angle * 5.0f - 0.7f) * 0.11f;
                float lobe3 = Mathf.Sin(angle * 8.0f + 1.2f) * 0.06f;
                float radiusMod = 1.0f + lobe1 + lobe2 + lobe3;

                float effectiveDist = dist / (maxRadius * radiusMod);
                if (effectiveDist >= 1.0f)
                {
                    image.SetPixel(x, y, new Color(1, 1, 1, 0));
                    continue;
                }

                // Smooth Gaussian-Hermite core falloff
                float t = 1.0f - effectiveDist;
                float smooth = t * t * (3.0f - 2.0f * t);
                float alpha = Mathf.Clamp(smooth * smooth * 1.2f, 0.0f, 1.0f);

                // Internal density variations
                float internalNoise = 0.90f + 0.10f * Mathf.Sin(dx * 0.12f) * Mathf.Cos(dy * 0.12f);
                float luminance = internalNoise;

                image.SetPixel(x, y, new Color(luminance, luminance, luminance, alpha));
            }
        }

        image.SavePng(destinationPath);
        GD.Print($"[ExplosionAssetGenerator] Generated smoke texture at {destinationPath}");
    }

    /// <summary>
    /// Generates a 128x128 incandescent glowing spark streak texture with smooth exponential glow.
    /// </summary>
    public static void GenerateSparkTexture(string destinationPath)
    {
        const int size = 128;
        var image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        float cx = size * 0.5f;
        float cy = size * 0.5f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x - cx) / (size * 0.5f);
                float dy = (y - cy) / (size * 0.5f);

                // Stretched diamond/oval spark profile
                float dOval = Mathf.Sqrt(dx * dx * 2.5f + dy * dy * 12.0f);
                float glow = Mathf.Max(0.0f, 1.0f - dOval);
                float alpha = Mathf.Pow(glow, 2.5f);

                // Ultra-bright core
                float core = Mathf.Max(0.0f, 1.0f - Mathf.Sqrt(dx * dx * 8.0f + dy * dy * 35.0f));
                alpha = Mathf.Clamp(alpha + core * 0.8f, 0.0f, 1.0f);

                image.SetPixel(x, y, new Color(1.0f, 0.95f, 0.85f, alpha));
            }
        }

        image.SavePng(destinationPath);
        GD.Print($"[ExplosionAssetGenerator] Generated spark texture at {destinationPath}");
    }

    /// <summary>
    /// Generates a 256x256 charred blast crater decal texture with irregular blast rays and soft edge falloff.
    /// </summary>
    public static void GenerateScorchTexture(string destinationPath)
    {
        const int size = 256;
        var image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        float cx = size * 0.5f;
        float cy = size * 0.5f;
        float maxR = cx - 12.0f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x - cx;
                float dy = y - cy;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);

                if (dist >= maxR)
                {
                    image.SetPixel(x, y, new Color(0, 0, 0, 0));
                    continue;
                }

                float angle = Mathf.Atan2(dy, dx);
                // Irregular jagged blast splatter rays
                float ray1 = Mathf.Sin(angle * 7.0f) * 0.18f;
                float ray2 = Mathf.Cos(angle * 13.0f + 0.8f) * 0.12f;
                float ray3 = Mathf.Sin(angle * 21.0f - 1.5f) * 0.08f;
                float rMod = 1.0f + ray1 + ray2 + ray3;

                float effDist = dist / (maxR * rMod);
                if (effDist >= 1.0f)
                {
                    image.SetPixel(x, y, new Color(0, 0, 0, 0));
                    continue;
                }

                float falloff = 1.0f - effDist;
                float alpha = Mathf.Pow(falloff, 1.6f) * 0.92f;
                // Charcoal soot color with micro-speckle variation
                float speckle = 0.04f + 0.03f * Mathf.Sin(dx * 0.4f) * Mathf.Cos(dy * 0.4f);

                image.SetPixel(x, y, new Color(speckle, speckle, speckle, alpha));
            }
        }

        image.SavePng(destinationPath);
        GD.Print($"[ExplosionAssetGenerator] Generated scorch texture at {destinationPath}");
    }
}
