namespace AudioClipExtractor.Separation;

/// <summary>一个可用的 MDX-Net 模型（ONNX 权重 + 预处理参数）。</summary>
public sealed class MdxModelInfo
{
    public required string FileName { get; init; }
    public required string DisplayName { get; init; }
    public required long Size { get; init; }
    public required string Sha256 { get; init; }
    public required string[] Urls { get; init; }

    public string PrimaryStem { get; init; } = "Instrumental";  // 模型直接输出的一条
    public string SecondaryStem { get; init; } = "Vocals";      // 由混合 - 主要 得到
    public int NFft { get; init; } = 6144;
    public int DimF { get; init; } = 3072;
    public int DimTLog2 { get; init; } = 8;
    public double Compensate { get; init; } = 1.02;

    public int DimT => 1 << DimTLog2;
    public int Hop => NFft / 6;

    public static readonly MdxModelInfo InstHq3 = new()
    {
        FileName = "UVR-MDX-NET-Inst_HQ_3.onnx",
        DisplayName = "MDX-Net 伴奏/人声（UVR-MDX-NET-Inst_HQ_3）",
        Size = 66759214,
        Sha256 = "317554b07fe1ea5279a77f2b1520a41ea4b93432560c4ffd08792c30fddf9adc",
        Urls = new[]
        {
            "https://hf-mirror.com/masszhou/mdxnet/resolve/main/UVR-MDX-NET-Inst_HQ_3.onnx",
            "https://huggingface.co/masszhou/mdxnet/resolve/main/UVR-MDX-NET-Inst_HQ_3.onnx",
            "https://hf-mirror.com/LeeAeron/uvr_mdx-net/resolve/main/UVR-MDX-NET-Inst_HQ_3.onnx"
        },
        PrimaryStem = "Instrumental",
        SecondaryStem = "Vocals",
        Compensate = 1.022
    };
}
