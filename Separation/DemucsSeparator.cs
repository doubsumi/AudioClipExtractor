using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace AudioClipExtractor.Separation;

/// <summary>
/// Demucs v4（htdemucs）分离核心：7.8s 分块 → ONNX 推理 → 三角窗重叠相加。
/// ONNX 侧已经把 STFT/iSTFT 与复数运算改写为实数实现，输入 1×2×343980 波形，
/// 输出 1×8×343980（4 轨 × 立体声），分块与权重方式与 demucs apply_model 一致。
/// </summary>
public static class DemucsSeparator
{
    public const int SampleRate = 44100;
    public const int SegmentLength = 343980;      // 7.8s @ 44.1kHz
    private const double Overlap = 0.25;

    public static readonly string[] SourceNames = { "drums", "bass", "other", "vocals" };
    public const int SourceCount = 4;

    public static float[][] Separate(string modelPath, float[] stereo,
        IProgress<double>? progress, CancellationToken ct)
    {
        int n = stereo.Length / 2;
        int step = (int)((1 - Overlap) * SegmentLength);
        var weight = BuildWeight();
        var acc = new float[SourceCount * 2 * n];
        var sumW = new float[n];

        var opts = new SessionOptions
        {
            IntraOpNumThreads = Math.Max(1, Environment.ProcessorCount),
            InterOpNumThreads = 1,
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL
        };
        using var session = new InferenceSession(modelPath, opts);
        string inputName = session.InputMetadata.Keys.First();
        string outputName = session.OutputMetadata.Keys.First();

        var inBuf = new float[2 * SegmentLength];
        var input = new DenseTensor<float>(inBuf, new[] { 1, 2, SegmentLength });

        int total = (n + step - 1) / step;
        int done = 0;
        for (int offset = 0; offset < n; offset += step)
        {
            ct.ThrowIfCancellationRequested();
            int len = Math.Min(SegmentLength, n - offset);
            int delta = SegmentLength - len;
            int start = offset - delta / 2;                 // 与 demucs TensorChunk.padded 一致：居中补零
            int cs = Math.Max(0, start);
            int ce = Math.Min(n, start + SegmentLength);
            int padLeft = cs - start;

            Array.Clear(inBuf, 0, inBuf.Length);
            for (int t = cs; t < ce; t++)
            {
                int d = padLeft + t - cs;
                // ONNX 张量 [1, 2, L] 是行优先的分声道布局（先整段左声道，再整段右声道）
                inBuf[d] = stereo[t * 2];
                inBuf[SegmentLength + d] = stereo[t * 2 + 1];
            }

            using (var run = session.Run(new[] { NamedOnnxValue.CreateFromTensor(inputName, input) }))
            {
                var tensor = run.First().AsTensor<float>();
                float[]? copied = null;
                ReadOnlySpan<float> src;
                if (tensor is DenseTensor<float> dense)
                    src = dense.Buffer.Span;                 // 直接读输出缓冲，避免每块复制
                else
                {
                    copied = tensor.ToArray();
                    src = copied;
                }

                int trim = (SegmentLength - len) / 2;        // center_trim(out, len)
                for (int s = 0; s < SourceCount; s++)
                {
                    for (int ch = 0; ch < 2; ch++)
                    {
                        int o = (s * 2 + ch) * n;
                        int r = (s * 2 + ch) * SegmentLength + trim;
                        for (int t = 0; t < len; t++)
                            acc[o + offset + t] += src[r + t] * weight[t];
                    }
                }
            }

            for (int t = 0; t < len; t++) sumW[offset + t] += weight[t];

            done++;
            progress?.Report(Math.Min(1, (double)done / total));
        }

        var stems = new float[SourceCount][];
        for (int s = 0; s < SourceCount; s++)
        {
            var dst = new float[n * 2];
            for (int t = 0; t < n; t++)
            {
                float w = sumW[t];
                float k = w > 1e-6f ? 1f / w : 0f;
                dst[t * 2] = acc[(s * 2) * n + t] * k;
                dst[t * 2 + 1] = acc[(s * 2 + 1) * n + t] * k;
            }
            stems[s] = dst;
        }
        return stems;
    }

    /// <summary>中间最高、两端为 1 的三角权重（归一化到 1），与 demucs apply_model 相同。</summary>
    private static float[] BuildWeight()
    {
        int half = SegmentLength / 2;
        var w = new float[SegmentLength];
        for (int i = 0; i < half; i++) w[i] = i + 1;
        for (int i = half; i < SegmentLength; i++) w[i] = SegmentLength - i;
        float max = w[half - 1];
        for (int i = 0; i < w.Length; i++) w[i] /= max;
        return w;
    }
}
