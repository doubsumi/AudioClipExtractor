using System.Security.Cryptography;
using System.Text;

namespace AudioClipExtractor.Separation;

/// <summary>分离结果的本地缓存，按“视频文件 + 音轨 + 模型”生成稳定目录。</summary>
public static class StemCache
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AudioClipExtractor", "stems");

    public static string DirFor(string videoPath, AudioTrackInfo info, MdxModelInfo model)
        => DirFor(videoPath, info, model.FileName);

    // 分离流程改动时递增，自动作废旧缓存
    // v3：修复 ONNX 模型 iSTFT 后导出的新权重，旧缓存（错误分离结果）全部作废
    private const string CacheVersion = "3";

    /// <summary>每个模型一个独立目录，避免 MDX（2 轨）与 Demucs（4 轨）互相覆盖。</summary>
    public static string DirFor(string videoPath, AudioTrackInfo info, string modelKey)
    {
        var fi = new FileInfo(videoPath);
        string key = $"{CacheVersion}|{fi.FullName}|{fi.Length}|{fi.LastWriteTimeUtc.Ticks}|{info.StreamIndex}|{modelKey}";
        var bytes = SHA1.HashData(Encoding.UTF8.GetBytes(key));
        var dir = Path.Combine(Root, Convert.ToHexString(bytes)[..10]);
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>某条分离音轨的 WAV 路径（按轨道名，如 drums / vocals / primary）。</summary>
    public static string StemPath(string dir, string name) => Path.Combine(dir, name + ".wav");
}
