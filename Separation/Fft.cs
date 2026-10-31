using System.Numerics;

namespace AudioClipExtractor.Separation;

/// <summary>
/// 长度可分解为 3 × 2^k 的复数 FFT（MDX-Net 的 n_fft = 6144 = 3 × 2048）。
/// 实输入直接在复数数组上做（虚部置 0），实现简单、结果与 torch.fft 一致。
/// </summary>
internal static class Fft
{
    private static readonly Dictionary<int, Complex[]> TwiddleCache = new();

    private static Complex[] Twiddles(int n)
    {
        lock (TwiddleCache)
        {
            if (TwiddleCache.TryGetValue(n, out var t)) return t;
            var table = new Complex[n];
            for (int k = 0; k < n; k++)
            {
                double a = -2 * Math.PI * k / n;
                table[k] = new Complex(Math.Cos(a), Math.Sin(a));
            }
            TwiddleCache[n] = table;
            return table;
        }
    }

    /// <summary>原地复数 FFT；inverse=true 时做逆变换（含 1/N 归一化）。</summary>
    public static void Transform(Complex[] x, bool inverse)
    {
        int n = x.Length;
        if (n <= 2)
        {
            if (n == 2)
            {
                var a = x[0];
                var b = x[1];
                x[0] = a + b;
                x[1] = a - b;
            }
            return;
        }

        int m = n / 3;
        if (m * 3 != n) throw new ArgumentException("FFT 长度必须是 3 × 2^k", nameof(x));

        var y0 = new Complex[m];
        var y1 = new Complex[m];
        var y2 = new Complex[m];
        for (int i = 0; i < m; i++)
        {
            y0[i] = x[3 * i];
            y1[i] = x[3 * i + 1];
            y2[i] = x[3 * i + 2];
        }

        Radix2(y0, inverse);
        Radix2(y1, inverse);
        Radix2(y2, inverse);

        var tw = Twiddles(n);
        for (int k = 0; k < n; k++)
        {
            var t1 = inverse ? Complex.Conjugate(tw[k]) : tw[k];
            int k2 = (2 * k) % n;
            var t2 = inverse ? Complex.Conjugate(tw[k2]) : tw[k2];
            x[k] = y0[k % m] + t1 * y1[k % m] + t2 * y2[k % m];
        }

        if (inverse)
            for (int k = 0; k < n; k++) x[k] /= n;
    }

    private static void Radix2(Complex[] a, bool inverse)
    {
        int n = a.Length;
        // 位反转置换
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; j >= bit; bit >>= 1) j -= bit;
            j += bit;
            if (i < j) (a[i], a[j]) = (a[j], a[i]);
        }

        double sign = inverse ? 1 : -1;
        for (int len = 2; len <= n; len <<= 1)
        {
            double ang = sign * 2 * Math.PI / len;
            var wlen = new Complex(Math.Cos(ang), Math.Sin(ang));
            int half = len / 2;
            for (int i = 0; i < n; i += len)
            {
                var w = Complex.One;
                for (int k = 0; k < half; k++)
                {
                    var u = a[i + k];
                    var v = a[i + k + half] * w;
                    a[i + k] = u + v;
                    a[i + k + half] = u - v;
                    w *= wlen;
                }
            }
        }
    }
}
