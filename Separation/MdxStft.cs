using System.Numerics;

namespace AudioClipExtractor.Separation;

/// <summary>
/// MDX-Net 使用的 STFT/ISTFT：n_fft=6144、hop=1024、保留 3072 个频点、每块 256 帧。
/// 语义对齐 audio-separator 的 uvr_lib_v5/stft.py（torch.stft center=True, reflect padding,
/// hann 窗 periodic=True；逆变换按 COLA 用 w² 归一化并裁掉两侧 n_fft/2）。
/// </summary>
internal sealed class MdxStft
{
    public const int NFft = 6144;
    public const int Hop = 1024;
    public const int DimF = 3072;
    public const int Frames = 256;
    public const int Pad = NFft / 2;
    public const int ChunkSamples = Hop * (Frames - 1); // 261120
    private const int PaddedLen = ChunkSamples + 2 * Pad;

    private readonly float[] _win = new float[NFft];
    private readonly Complex[] _buf = new Complex[NFft];
    private readonly float[] _padded = new float[PaddedLen * 2];
    private readonly float[] _olaL = new float[PaddedLen];
    private readonly float[] _olaR = new float[PaddedLen];
    private readonly float[] _wsum = new float[PaddedLen];

    public MdxStft()
    {
        for (int n = 0; n < NFft; n++)
            _win[n] = 0.5f - 0.5f * (float)Math.Cos(2 * Math.PI * n / NFft);

        // 重叠相加的窗平方和（每块都相同，预先算一次）
        for (int f = 0; f < Frames; f++)
        {
            int start = f * Hop;
            for (int n = 0; n < NFft; n++)
            {
                float w = _win[n];
                _wsum[start + n] += w * w;
            }
        }
    }

    /// <summary>立体声（交错 L/R，长度 = ChunkSamples）→ 频谱 [4, DimF, Frames]（实/虚 交替通道）。</summary>
    public void Forward(float[] stereo, float[] spec)
    {
        // torch 的 reflect padding（两端各 NFft/2，不重复端点）
        int count = ChunkSamples;
        for (int i = 0; i < PaddedLen; i++)
        {
            int src = i - Pad;
            if (src < 0) src = -src;
            else if (src >= count) src = 2 * (count - 1) - src;
            _padded[i * 2] = stereo[src * 2];
            _padded[i * 2 + 1] = stereo[src * 2 + 1];
        }

        var buf = _buf;
        for (int f = 0; f < Frames; f++)
        {
            int baseIdx = f * Hop;
            for (int ch = 0; ch < 2; ch++)
            {
                for (int n = 0; n < NFft; n++)
                {
                    float s = _padded[(baseIdx + n) * 2 + ch] * _win[n];
                    buf[n] = new Complex(s, 0);
                }
                Fft.Transform(buf, false);

                int cReal = ch * 2;
                int cImag = cReal + 1;
                for (int k = 0; k < DimF; k++)
                {
                    spec[(cReal * DimF + k) * Frames + f] = (float)buf[k].Real;
                    spec[(cImag * DimF + k) * Frames + f] = (float)buf[k].Imaginary;
                }
            }
        }
    }

    /// <summary>频谱 [4, DimF, Frames] → 立体声（交错 L/R，长度 = ChunkSamples）。</summary>
    public void Inverse(float[] spec, float[] stereoOut)
    {
        Array.Clear(_olaL);
        Array.Clear(_olaR);

        var buf = _buf;
        for (int f = 0; f < Frames; f++)
        {
            int start = f * Hop;
            for (int ch = 0; ch < 2; ch++)
            {
                int cReal = ch * 2;
                int cImag = cReal + 1;
                for (int k = 0; k < DimF; k++)
                    buf[k] = new Complex(
                        spec[(cReal * DimF + k) * Frames + f],
                        spec[(cImag * DimF + k) * Frames + f]);

                buf[DimF] = Complex.Zero; // 被裁掉的 Nyquist 频点按 0 处理
                for (int k = DimF + 1; k < NFft; k++)
                    buf[k] = Complex.Conjugate(buf[NFft - k]);

                Fft.Transform(buf, true);

                var ola = ch == 0 ? _olaL : _olaR;
                for (int n = 0; n < NFft; n++)
                    ola[start + n] += (float)buf[n].Real * _win[n];
            }
        }

        for (int i = 0; i < PaddedLen; i++)
        {
            float ws = _wsum[i];
            float k = ws > 1e-6f ? 1f / ws : 0f;
            int o = i - Pad;
            if (o < 0 || o >= ChunkSamples) continue;
            stereoOut[o * 2] = _olaL[i] * k;
            stereoOut[o * 2 + 1] = _olaR[i] * k;
        }
    }
}
