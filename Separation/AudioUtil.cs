using NAudio.Wave;

namespace AudioClipExtractor.Separation;

/// <summary>把容器音轨解码成 44.1kHz 立体声 float（分离引擎的统一输入格式），以及写回 PCM16 WAV。</summary>
public static class AudioUtil
{
    public static float[] DecodeStereo(string videoPath, AudioTrackInfo info, TimeSpan duration,
        IProgress<double>? progress, CancellationToken ct)
    {
        using var src = TrackWaveStream.Open(videoPath, info, duration);
        using var resampler = info.SampleRate == MdxSeparator.SampleRate
            ? null
            : new MediaFoundationResampler(src, new WaveFormat(MdxSeparator.SampleRate, 16, info.Channels));
        IWaveProvider stream = resampler ?? (IWaveProvider)src;

        int channels = stream.WaveFormat.Channels;
        var fmt = stream.WaveFormat;
        var buffer = new byte[fmt.AverageBytesPerSecond * 2];
        long expectedBytes = (long)(duration.TotalSeconds * fmt.AverageBytesPerSecond);
        var samples = new List<float>(Math.Max(1024, (int)(duration.TotalSeconds * MdxSeparator.SampleRate) * 2));
        long readTotal = 0;

        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            ct.ThrowIfCancellationRequested();
            int frames = read / (2 * channels);
            for (int i = 0; i < frames; i++)
            {
                short l = BitConverter.ToInt16(buffer, i * 2 * channels);
                samples.Add(l / 32768f);
                short r = channels > 1 ? BitConverter.ToInt16(buffer, i * 2 * channels + 2) : l;
                samples.Add(r / 32768f);
            }
            readTotal += read;
            progress?.Report(Math.Min(1, (double)readTotal / Math.Max(1, expectedBytes) * 0.2)); // 解码占总进度 20%
        }

        return samples.ToArray();
    }

    public static void WritePcm16Stereo(string path, float[] interleaved)
    {
        using var writer = new WaveFileWriter(path, new WaveFormat(MdxSeparator.SampleRate, 16, 2));
        var bytes = new byte[interleaved.Length * 2];
        for (int i = 0; i < interleaved.Length; i++)
        {
            float v = interleaved[i];
            if (v > 1f) v = 1f; else if (v < -1f) v = -1f;
            short s = (short)Math.Round(v * 32767f);
            bytes[i * 2] = (byte)(s & 0xFF);
            bytes[i * 2 + 1] = (byte)((s >> 8) & 0xFF);
        }
        writer.Write(bytes, 0, bytes.Length);
    }
}
