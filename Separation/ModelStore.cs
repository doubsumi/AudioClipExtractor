using System.Net.Http;
using System.Security.Cryptography;

namespace AudioClipExtractor.Separation;

/// <summary>模型文件下载 / 校验 / 本地缓存。</summary>
public static class ModelStore
{
    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = true })
    {
        Timeout = Timeout.InfiniteTimeSpan
    };

    public static string ModelDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AudioClipExtractor", "models");

    public static string PathFor(MdxModelInfo model) => Path.Combine(ModelDir, model.FileName);

    /// <summary>Demucs（htdemucs）ONNX 的标准位置；由 tools/export_demucs.py 生成本机模型。</summary>
    public const string DemucsFileName = "htdemucs.onnx";

    public static string DemucsPath => Path.Combine(ModelDir, DemucsFileName);

    /// <summary>
    /// 查找已就绪的 Demucs ONNX：模型目录优先，其次程序所在目录及 models 子目录。
    /// 权重通常以外部数据文件（.onnx.data）随附，两者齐全才算可用。
    /// </summary>
    public static string? FindDemucs()
    {
        var roots = new[]
        {
            ModelDir,
            AppContext.BaseDirectory,
            Path.Combine(AppContext.BaseDirectory, "models")
        };
        foreach (var root in roots)
        {
            var path = Path.Combine(root, DemucsFileName);
            if (!File.Exists(path)) continue;
            if (File.Exists(path + ".data") || new FileInfo(path).Length > 10_000_000) return path;
        }
        return null;
    }

    public static bool IsInstalled(MdxModelInfo model)
    {
        var p = PathFor(model);
        return File.Exists(p) && new FileInfo(p).Length == model.Size;
    }

    /// <summary>确保模型已就绪；缺失时依次尝试各镜像地址下载并校验 SHA-256。</summary>
    public static async Task<string> EnsureAsync(MdxModelInfo model,
        IProgress<(long Received, long Total)>? progress, CancellationToken ct)
    {
        Directory.CreateDirectory(ModelDir);
        var target = PathFor(model);
        if (IsInstalled(model)) return target;

        Exception? last = null;
        foreach (var url in model.Urls)
        {
            var part = target + ".part";
            try
            {
                await DownloadAsync(url, part, model.Size, progress, ct);
                var hash = await Sha256Async(part, ct);
                if (!hash.Equals(model.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"模型校验失败（SHA-256 不匹配）：{Path.GetFileName(url)}");

                File.Move(part, target, true);
                return target;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                last = ex;
                try { if (File.Exists(part)) File.Delete(part); } catch { }
            }
        }
        throw new IOException("模型下载失败：" + (last?.Message ?? "没有可用的下载地址"), last);
    }

    private static async Task DownloadAsync(string url, string dest, long expectedSize,
        IProgress<(long, long)>? progress, CancellationToken ct)
    {
        using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        long total = resp.Content.Headers.ContentLength is long len && len > 0 ? len : expectedSize;

        await using var src = await resp.Content.ReadAsStreamAsync(ct);
        await using var dst = File.Create(dest);
        var buf = new byte[81920];
        long received = 0;
        int n;
        while ((n = await src.ReadAsync(buf, ct)) > 0)
        {
            await dst.WriteAsync(buf.AsMemory(0, n), ct);
            received += n;
            progress?.Report((received, Math.Max(total, received)));
        }
        if (received != expectedSize)
            throw new InvalidDataException($"下载不完整：{received} / {expectedSize} 字节");
    }

    private static async Task<string> Sha256Async(string path, CancellationToken ct)
    {
        await using var fs = File.OpenRead(path);
        var hash = await SHA256.Create().ComputeHashAsync(fs, ct);
        return Convert.ToHexString(hash);
    }
}
