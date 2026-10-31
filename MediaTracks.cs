using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using NAudio.MediaFoundation;
using NAudio.Wave;

namespace AudioClipExtractor;

/// <summary>容器中的一条音频流（多音轨视频的一条音轨），或一条分离出来的虚拟音轨。</summary>
public sealed class AudioTrackInfo
{
    public required int StreamIndex { get; init; }
    public required int Ordinal { get; init; }   // 第几条音频轨（从 1 开始），仅用于展示
    public string Language { get; init; } = "";
    public int Channels { get; init; }
    public int SampleRate { get; init; }

    /// <summary>分离结果的 WAV 文件；非空表示这是一条虚拟音轨，不来自视频容器。</summary>
    public string? StemPath { get; init; }
    public string? StemName { get; init; }
    public bool IsStem => StemPath != null;

    public string Title => IsStem
        ? StemName ?? "分离音轨"
        : Ordinal == 1 ? "默认音轨" : $"音轨 {Ordinal}";   // 首条容器音轨固定叫“默认音轨”，避免与分离出的 音轨1 重名

    public string Detail
    {
        get
        {
            if (IsStem)
                return $"AI 分离 · {(Channels >= 2 ? "立体声" : "单声道")} · {SampleRate / 1000.0:0.#} kHz";

            var parts = new List<string>();
            var lang = LanguageName(Language);
            if (lang.Length > 0) parts.Add(lang);
            parts.Add(Channels >= 2 ? $"{Channels} 声道" : "单声道");
            if (SampleRate > 0) parts.Add($"{SampleRate / 1000.0:0.#} kHz");
            return string.Join(" · ", parts);
        }
    }

    private static string LanguageName(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return "";
        var c = code.Trim().ToLowerInvariant();
        if (c.StartsWith("zh") || c.StartsWith("chi") || c.StartsWith("zho") || c.StartsWith("cmn")) return "中文";
        if (c.StartsWith("en") || c.StartsWith("eng")) return "English";
        if (c.StartsWith("ja") || c.StartsWith("jpn")) return "日本語";
        if (c.StartsWith("ko") || c.StartsWith("kor")) return "한국어";
        if (c.StartsWith("fr") || c.StartsWith("fra") || c.StartsWith("fre")) return "Français";
        if (c.StartsWith("de") || c.StartsWith("deu") || c.StartsWith("ger")) return "Deutsch";
        if (c.StartsWith("es") || c.StartsWith("spa")) return "Español";
        if (c.StartsWith("ru") || c.StartsWith("rus")) return "Русский";
        if (c.StartsWith("yue")) return "粤语";
        return code;
    }
}

/// <summary>
/// 用 Media Foundation 的 Source Reader 直接读取指定音轨（NAudio 的 MediaFoundationReader 只会读第一条）。
/// </summary>
public static class MediaTracks
{
    // MF_MT_LANGUAGE（NAudio 未公开该属性键）
    private static readonly Guid MF_MT_LANGUAGE = new("55B9B5A1-1B0B-4B0E-8D4D-F8A2F48E364B");

    /// <summary>枚举文件中所有音频轨；没有音轨时抛出异常。</summary>
    public static List<AudioTrackInfo> Enumerate(string path, out TimeSpan duration)
    {
        MediaFoundationApi.Startup();
        MediaFoundationInterop.MFCreateSourceReaderFromURL(path, null, out var reader);
        try
        {
            duration = GetDuration(reader);
            var list = new List<AudioTrackInfo>();
            for (int idx = 0; idx < 64; idx++)
            {
                IMFMediaType? native = null;
                try
                {
                    reader.GetNativeMediaType(idx, 0, out native);
                }
                catch
                {
                    break; // 没有更多流
                }

                try
                {
                    native.GetMajorType(out Guid major);
                    if (major != MediaTypes.MFMediaType_Audio) continue;

                    int channels = 2, rate = 44100;
                    try { native.GetUINT32(MediaFoundationAttributes.MF_MT_AUDIO_NUM_CHANNELS, out channels); } catch { }
                    try { native.GetUINT32(MediaFoundationAttributes.MF_MT_AUDIO_SAMPLES_PER_SECOND, out rate); } catch { }

                    string lang = "";
                    try
                    {
                        var sb = new StringBuilder(256);
                        native.GetString(MF_MT_LANGUAGE, sb, 256, out _);
                        lang = sb.ToString();
                    }
                    catch { }

                    list.Add(new AudioTrackInfo
                    {
                        StreamIndex = idx,
                        Ordinal = list.Count + 1,
                        Channels = Math.Max(1, channels),
                        SampleRate = Math.Max(0, rate),
                        Language = lang
                    });
                }
                finally
                {
                    if (native != null) Marshal.ReleaseComObject(native);
                }
            }

            if (list.Count == 0)
                throw new InvalidOperationException("该文件中没有可读取的音频轨道。");
            return list;
        }
        finally
        {
            Marshal.ReleaseComObject(reader);
        }
    }

    internal static TimeSpan GetDuration(IMFSourceReader reader)
    {
        IntPtr pv = Marshal.AllocCoTaskMem(16);
        try
        {
            Marshal.Copy(new byte[16], 0, pv, 16);
            int hr = reader.GetPresentationAttribute(
                MediaFoundationInterop.MF_SOURCE_READER_MEDIASOURCE,
                MediaFoundationAttributes.MF_PD_DURATION, pv);
            if (hr < 0) return TimeSpan.Zero;
            return TimeSpan.FromTicks(Marshal.ReadInt64(pv, 8)); // 64bit 值单位 100ns，即 TimeSpan tick
        }
        finally
        {
            Marshal.FreeCoTaskMem(pv);
        }
    }

    internal static int ReadUInt(IMFMediaType? type, Guid key, int fallback)
    {
        if (type == null) return fallback;
        try
        {
            type.GetUINT32(key, out int v);
            return v > 0 ? v : fallback;
        }
        catch
        {
            return fallback;
        }
    }
}

/// <summary>
/// 单条音轨的 PCM 流，可直接交给 WaveOutEvent / MediaFoundationResampler。
/// 注意：Media Foundation 的 SourceReader 与创建它的 COM 单元绑定，在 WinForms 的 STA
/// UI 线程创建后，WaveOutEvent 用自己的 MTA 播放线程去读取会报 E_NOINTERFACE，表现为
/// 点播放立刻停住。所以把 SourceReader 固定在一个专用的 MTA 工作线程上，所有打开、
/// 读取与定位都在该线程执行，外部调用通过任务队列同步转发。
/// </summary>
public sealed class TrackWaveStream : WaveStream
{
    private readonly BlockingCollection<Action> _jobs = new();
    private readonly Thread _worker;
    private readonly WaveFormat _format;
    private readonly long _lengthBytes;

    // 以下状态只在工作线程上访问
    private IMFSourceReader? _reader;
    private readonly int _streamIndex;
    private byte[] _pending = new byte[65536];
    private int _pendingPos, _pendingLen;
    private volatile bool _eos;
    private long _position;
    private bool _disposed;

    private TrackWaveStream(string path, AudioTrackInfo info, TimeSpan duration)
    {
        _streamIndex = info.StreamIndex;
        MediaFoundationApi.Startup();
        _worker = new Thread(RunJobs) { IsBackground = true, Name = "TrackWaveReader" };
        _worker.SetApartmentState(ApartmentState.MTA);
        _worker.Start();

        var (format, length) = Run(() => OpenOnWorker(path, info, duration));
        _format = format;
        _lengthBytes = length;
    }

    public static TrackWaveStream Open(string path, AudioTrackInfo info, TimeSpan duration)
        => new(path, info, duration);

    public override WaveFormat WaveFormat => _format;
    public override long Length => _lengthBytes;

    public override long Position
    {
        get => Interlocked.Read(ref _position);
        set => SeekBytes(value);
    }

    public void SetPositionMs(double ms)
        => SeekBytes((long)(Math.Max(0, ms) * _format.AverageBytesPerSecond / 1000));

    // ===== 工作线程调度 =====
    private void RunJobs()
    {
        // 必须用 GetConsumingEnumerable：直接 foreach 拿到的是快照枚举，不会阻塞等待新任务
        foreach (var job in _jobs.GetConsumingEnumerable())
        {
            try { job(); } catch { }
        }
    }

    private T Run<T>(Func<T> body)
    {
        var tcs = new TaskCompletionSource<T>();
        _jobs.Add(() =>
        {
            try { tcs.SetResult(body()); }
            catch (Exception ex) { tcs.SetException(ex); }
        });
        return tcs.Task.GetAwaiter().GetResult();
    }

    private void Run(Action body) => Run<object?>(() => { body(); return null; });

    private (WaveFormat Format, long Length) OpenOnWorker(string path, AudioTrackInfo info, TimeSpan duration)
    {
        MediaFoundationInterop.MFCreateSourceReaderFromURL(path, null, out var reader);
        try
        {
            reader.SetStreamSelection(MediaFoundationInterop.MF_SOURCE_READER_ALL_STREAMS, false);
            reader.SetStreamSelection(info.StreamIndex, true);

            var mt = MediaFoundationApi.CreateMediaType();
            mt.SetGUID(MediaFoundationAttributes.MF_MT_MAJOR_TYPE, MediaTypes.MFMediaType_Audio);
            mt.SetGUID(MediaFoundationAttributes.MF_MT_SUBTYPE, AudioSubtypes.MFAudioFormat_PCM);
            mt.SetUINT32(MediaFoundationAttributes.MF_MT_AUDIO_NUM_CHANNELS, info.Channels);
            mt.SetUINT32(MediaFoundationAttributes.MF_MT_AUDIO_SAMPLES_PER_SECOND, info.SampleRate);
            mt.SetUINT32(MediaFoundationAttributes.MF_MT_AUDIO_BITS_PER_SAMPLE, 16);
            reader.SetCurrentMediaType(info.StreamIndex, IntPtr.Zero, mt);
            Marshal.ReleaseComObject(mt);

            IMFMediaType? current = null;
            try { reader.GetCurrentMediaType(info.StreamIndex, out current); } catch { }
            int channels = MediaTracks.ReadUInt(current, MediaFoundationAttributes.MF_MT_AUDIO_NUM_CHANNELS, info.Channels);
            int rate = MediaTracks.ReadUInt(current, MediaFoundationAttributes.MF_MT_AUDIO_SAMPLES_PER_SECOND, info.SampleRate);
            if (current != null) Marshal.ReleaseComObject(current);

            var format = new WaveFormat(Math.Max(8000, rate), 16, Math.Max(1, channels));
            long length = Math.Max(format.AverageBytesPerSecond / 2,
                (long)(duration.TotalSeconds * format.AverageBytesPerSecond));
            _reader = reader;
            return (format, length);
        }
        catch
        {
            Marshal.ReleaseComObject(reader);
            throw;
        }
    }

    private void SeekBytes(long bytes)
    {
        bytes = Math.Clamp(bytes, 0, _lengthBytes);
        Run(() =>
        {
            try
            {
                SeekInSourceReader(bytes);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"定位音频失败（0x{ex.HResult:X8}）。", ex);
            }
            Interlocked.Exchange(ref _position, bytes);
            _pendingPos = _pendingLen = 0;
            _eos = false;
        });
    }

    private void SeekInSourceReader(long bytes)
    {
        if (_reader == null) throw new InvalidOperationException("音频流已关闭。");
        IntPtr pv = Marshal.AllocCoTaskMem(16);
        try
        {
            Marshal.Copy(new byte[16], 0, pv, 16);
            Marshal.WriteInt16(pv, 0, 20); // VT_I8，PROPVARIANT 的 vt
            // 位置单位是 100ns 刻度（1 秒 = 10,000,000）
            long ticks = (long)(bytes * 10000000.0 / _format.AverageBytesPerSecond);
            Marshal.WriteInt64(pv, 8, ticks);
            _reader.SetCurrentPosition(Guid.Empty, pv);
        }
        finally
        {
            Marshal.FreeCoTaskMem(pv);
        }
    }

    // WaveOutEvent 在自己的 MTA 播放线程上调用 Read，所以整段读取同样转发到工作线程，
    // 保证 SourceReader 只被创建它的那个 MTA 线程访问。
    public override int Read(byte[] buffer, int offset, int count)
    {
        if (Volatile.Read(ref _disposed)) return 0;
        try
        {
            return Run(() => ReadCore(buffer, offset, count));
        }
        catch
        {
            return 0; // 流已释放或设备异常：直接结束播放
        }
    }

    private int ReadCore(byte[] buffer, int offset, int count)
    {
        int done = 0;
        while (done < count)
        {
            if (_pendingLen == 0 && !Refill()) break;
            if (_pendingLen == 0) break;
            int n = Math.Min(count - done, _pendingLen);
            Buffer.BlockCopy(_pending, _pendingPos, buffer, offset + done, n);
            _pendingPos += n;
            _pendingLen -= n;
            done += n;
            Interlocked.Exchange(ref _position, Math.Min(_lengthBytes, Interlocked.Read(ref _position) + n));
        }
        return done;
    }

    private bool Refill()
    {
        if (_eos) return false;
        if (_reader == null) { _eos = true; return false; }
        // 偶尔会读到空样本（容器边界），重试几次；真正结束时置 _eos
        for (int attempt = 0; attempt < 4; attempt++)
        {
            IMFSample? sample = null;
            try
            {
                _reader.ReadSample(_streamIndex, 0, out _, out MF_SOURCE_READER_FLAG flags, out _, out sample);
                if ((flags & MF_SOURCE_READER_FLAG.MF_SOURCE_READERF_ENDOFSTREAM) != 0
                    || (flags & MF_SOURCE_READER_FLAG.MF_SOURCE_READERF_ERROR) != 0)
                {
                    _eos = true;
                    return false;
                }
                if (sample == null) return false;
                if (CopySample(sample)) return true;
            }
            finally
            {
                if (sample != null) Marshal.ReleaseComObject(sample);
            }
        }
        return false;
    }

    private bool CopySample(IMFSample sample)
    {
        IMFMediaBuffer? buffer = null;
        try
        {
            sample.ConvertToContiguousBuffer(out buffer);
            buffer.Lock(out IntPtr p, out _, out int len);
            if (len <= 0)
            {
                buffer.Unlock();
                return false;
            }
            if (len > _pending.Length) Array.Resize(ref _pending, len);
            Marshal.Copy(p, _pending, 0, len);
            buffer.Unlock();
            _pendingPos = 0;
            _pendingLen = len;
            return true;
        }
        finally
        {
            if (buffer != null) Marshal.ReleaseComObject(buffer);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            // SourceReader 必须在创建它的工作线程上释放，否则会留下跨单元引用
            try
            {
                Run(() =>
                {
                    if (_reader != null)
                    {
                        Marshal.ReleaseComObject(_reader);
                        _reader = null;
                    }
                });
            }
            catch { }

            try { _jobs.CompleteAdding(); } catch { }
            _worker.Join(2000);
            _jobs.Dispose();
        }
        base.Dispose(disposing);
    }
}
