using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace AudioClipExtractor.Separation;

public sealed class SeparatedStems
{
    public required float[] Primary { get; init; }
    public required float[] Secondary { get; init; }
    public required string PrimaryName { get; init; }
    public required string SecondaryName { get; init; }
}

/// <summary>
/// MDX-Net 分离核心：整段音频分块 → STFT → ONNX 推理 → iSTFT → 重叠相加。
/// 参数与 audio-separator 的 MDXSeparator 保持一致（segment=dim_t=256、overlap=0.25、
/// 混合按峰值缩放到 0.9、主要轨按 compensate 系数从原混音里扣除得到第二轨）。
/// </summary>
public static class MdxSeparator
{
    public const int SampleRate = 44100;
    private const double Overlap = 0.25;
    private const float NormalizePeak = 0.9f;

    public static SeparatedStems Separate(string modelPath, MdxModelInfo model,
        float[] stereo, IProgress<double>? progress, CancellationToken ct)
    {
        int n = stereo.Length / 2;
        var stft = new MdxStft();
        int trim = MdxStft.Pad;
        int chunk = MdxStft.ChunkSamples;
        int gen = chunk - 2 * trim;
        int step = (int)((1 - Overlap) * chunk);

        // 混合峰值归一化（与 audio-separator 的 normalization_threshold=0.9 一致）
        float peak = 0;
        for (int i = 0; i < stereo.Length; i++) peak = MathF.Max(peak, MathF.Abs(stereo[i]));
        float gain = peak > NormalizePeak ? NormalizePeak / peak : 1f;

        int mixLen = trim + n + gen + trim - n % gen;
        var result = new float[mixLen * 2];
        var divider = new float[mixLen];

        var spec = new float[4 * MdxStft.DimF * MdxStft.Frames];
        var outSpec = new float[spec.Length];
        var tensor = new DenseTensor<float>(spec, new[] { 1, 4, MdxStft.DimF, MdxStft.Frames });
        var chunkIn = new float[chunk * 2];
        var chunkOut = new float[chunk * 2];

        var opts = new SessionOptions
        {
            IntraOpNumThreads = Math.Max(1, Environment.ProcessorCount),
            InterOpNumThreads = 1,
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL
        };
        using var session = new InferenceSession(modelPath, opts);
        var inputName = session.InputMetadata.Keys.First();

        int total = (mixLen + step - 1) / step;
        int done = 0;

        for (int start = 0; start < mixLen; start += step)
        {
            ct.ThrowIfCancellationRequested();
            int end = Math.Min(start + chunk, mixLen);
            int actual = end - start;

            // 组装该块的输入（超出原音频的部分补零，并按 gain 归一化）
            Array.Clear(chunkIn);
            for (int t = 0; t < actual; t++)
            {
                int m = start + t - trim;
                if (m < 0 || m >= n) continue;
                chunkIn[t * 2] = stereo[m * 2] * gain;
                chunkIn[t * 2 + 1] = stereo[m * 2 + 1] * gain;
            }

            stft.Forward(chunkIn, spec);
            // 前 3 个频点置零（与 UVR 一致）
            for (int c = 0; c < 4; c++)
                for (int k = 0; k < 3; k++)
                    for (int f = 0; f < MdxStft.Frames; f++)
                        spec[(c * MdxStft.DimF + k) * MdxStft.Frames + f] = 0;

            using (var run = session.Run(new[] { NamedOnnxValue.CreateFromTensor(inputName, tensor) }))
            {
                var outTensor = run.First().AsTensor<float>();
                var dense = outTensor as DenseTensor<float>;
                if (dense != null) dense.Buffer.Span.CopyTo(outSpec.AsSpan());
                else outTensor.ToArray().CopyTo(outSpec, 0);
            }

            stft.Inverse(outSpec, chunkOut);

            // 对称 hann 窗对块间重叠做加权平均
            for (int t = 0; t < actual; t++)
            {
                float w = actual > 1
                    ? 0.5f - 0.5f * (float)Math.Cos(2 * Math.PI * t / (actual - 1))
                    : 1f;
                divider[start + t] += w;
                result[(start + t) * 2] += chunkOut[t * 2] * w;
                result[(start + t) * 2 + 1] += chunkOut[t * 2 + 1] * w;
            }

            done++;
            progress?.Report(Math.Min(1, (double)done / total));
        }

        var primary = new float[n * 2];
        var secondary = new float[n * 2];
        for (int t = 0; t < n; t++)
        {
            float d = divider[trim + t];
            float k = d > 1e-6f && peak > 0 ? (1f / d) / gain : 0f;
            // 主要轨按归一化前的真实电平还原
            float p0 = result[(trim + t) * 2] * k;
            float p1 = result[(trim + t) * 2 + 1] * k;
            primary[t * 2] = p0;
            primary[t * 2 + 1] = p1;
            // 第二轨 = 原混音 - 主要轨 × compensate
            secondary[t * 2] = stereo[t * 2] - p0 * (float)model.Compensate;
            secondary[t * 2 + 1] = stereo[t * 2 + 1] - p1 * (float)model.Compensate;
        }

        return new SeparatedStems
        {
            Primary = primary,
            Secondary = secondary,
            PrimaryName = model.PrimaryStem,
            SecondaryName = model.SecondaryStem
        };
    }
}
