using NAudio.Wave;

namespace AudioClipExtractor;

public class MainForm : Form
{
    // ===== 配置常量 =====
    private const int OutSampleRate = 44100;
    private const int OutBits = 16;
    private const int OutChannels = 1;
    private const int SeekThrottleMs = 120;

    // ===== 控件 =====
    private readonly ModernButton btnBrowse = new();
    private readonly ModernButton btnTheme = new();
    private readonly Label lblFile = new();

    private readonly ModernCard cardPlayer = new();
    private readonly Label lblDuration = new();
    private readonly ModernTrackBar track = new();
    private readonly Label lblTime = new();
    private readonly ModernButton btnPlay = new();
    private readonly ModernButton btnMark = new();

    private readonly ModernCard cardExport = new();
    private readonly Label lblMark = new();
    private readonly Label lblHint = new();
    private readonly ModernButton btnClear = new();
    private readonly ModernButton btnPreview = new();
    private readonly ModernButton btnExport = new();

    private readonly Label lblStatus = new();
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 50 };

    // ===== 状态 =====
    private WaveOutEvent? output;
    private MediaFoundationReader? reader;
    private string? videoPath;
    private int totalMs;
    private int? markStartMs;
    private int? markEndMs;
    private bool _activeMarkerIsEnd;   // 最近触碰的标记（←/→ 微调目标）：true=终点，false=起点
    private DateTime lastSeek = DateTime.MinValue;
    private bool scrubbing;
    private bool previewing;

    public MainForm()
    {
        InitializeUI();
        ApplyTheme();

        Theme.Changed += ApplyTheme;
        timer.Tick += Timer_Tick;
        timer.Start();

        AllowDrop = true;
        DragEnter += (_, e) =>
        {
            if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
                e.Effect = DragDropEffects.Copy;
        };
        DragDrop += (_, e) =>
        {
            if (e.Data?.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
                LoadMedia(files[0]);
        };

        SetPlaybackEnabled(false);
        UpdateExportEnabled();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // 按当前屏幕 DPI 设定初始客户区尺寸（设计值 900x480 @96DPI）
        float s = DeviceDpi / 96f;
        ClientSize = new Size((int)Math.Round(900 * s), (int)Math.Round(480 * s));
        UpdateMinimumSize();
        LayoutUi();
        Native.ApplyWindowStyle(Handle, Theme.IsDark);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        LayoutUi();
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e); // AutoScaleMode.None 下 base 不会再自动缩放控件
        // 窗口像素尺寸按 DPI 比例调整，保持逻辑大小不变
        if (!e.SuggestedRectangle.IsEmpty)
            SetBounds(0, 0, e.SuggestedRectangle.Width, e.SuggestedRectangle.Height, BoundsSpecified.Size);
        UpdateMinimumSize();
        LayoutUi();
    }

    // 最小尺寸 = 设计客户区(900x480) × 当前 DPI + 边框/标题栏厚度
    private void UpdateMinimumSize()
    {
        float s = DeviceDpi / 96f;
        int borderW = Math.Max(0, Width - ClientSize.Width);
        int borderH = Math.Max(0, Height - ClientSize.Height);
        MinimumSize = new Size((int)Math.Round(900 * s) + borderW, (int)Math.Round(480 * s) + borderH);
    }

    /// <summary>
    /// 手动布局：所有控件的位置和尺寸由设计值（96 DPI 下的 900x480）× 当前 DPI 系数算出，
    /// 宽度方向随客户区拉伸。每次 DPI 切换 / 窗口缩放都从头重算，杜绝累积漂移。
    /// </summary>
    private void LayoutUi()
    {
        int W = ClientSize.Width;
        int H = ClientSize.Height;
        if (W <= 0 || H <= 0) return;
        float s = DeviceDpi / 96f;
        int Sc(int v) => (int)Math.Round(v * s);

        SuspendLayout();

        btnBrowse.SetBounds(Sc(16), Sc(16), Sc(132), Sc(38));
        btnTheme.SetBounds(W - Sc(24) - Sc(100), Sc(18), Sc(100), Sc(34));
        lblFile.SetBounds(Sc(160), Sc(27), Math.Max(Sc(60), btnTheme.Left - Sc(16) - Sc(160)), Sc(20));

        int cardW = W - Sc(32);

        cardPlayer.SetBounds(Sc(16), Sc(72), cardW, Sc(180));
        lblDuration.SetBounds(Sc(20), Sc(14), Sc(500), Sc(22));
        track.SetBounds(Sc(20), Sc(50), cardW - Sc(40), Sc(80));   // 控件较高：上方留给拖动气泡
        lblTime.SetBounds(Sc(20), Sc(134), Sc(380), Sc(26));
        btnPlay.SetBounds(cardW - Sc(312), Sc(130), Sc(132), Sc(36));   // 设计 x=556，距卡片右缘 312
        btnMark.SetBounds(cardW - Sc(168), Sc(130), Sc(148), Sc(36));   // 设计 x=700，距卡片右缘 168

        // 状态文字：上下各 16px 对称留白；cardExport 高度随窗口拉伸自适应
        int statusH = Sc(18);
        int statusMargin = Sc(16);
        int cardTop = Sc(264);
        int cardH = Math.Max(Sc(160), H - cardTop - statusMargin - statusH - statusMargin);
        cardExport.SetBounds(Sc(16), cardTop, cardW, cardH);
        lblMark.SetBounds(Sc(20), Sc(20), cardW - Sc(40), Sc(22));
        lblHint.SetBounds(Sc(20), Sc(50), cardW - Sc(40), Sc(20));
        // 按钮行贴卡片底部，中间留白随卡片高度伸缩
        int btnY = cardH - Sc(8) - Sc(36);
        btnClear.SetBounds(Sc(20), btnY, Sc(118), Sc(36));
        btnPreview.SetBounds(cardW - Sc(328), btnY, Sc(148), Sc(36)); // 设计 x=540，距卡片右缘 328
        btnExport.SetBounds(cardW - Sc(168), btnY, Sc(148), Sc(36));  // 设计 x=700，距卡片右缘 168

        lblStatus.SetBounds(Sc(20), H - statusMargin - statusH, Math.Max(Sc(60), W - Sc(40)), statusH);

        ResumeLayout(true);
    }

    // ===== UI 构建 =====
    private void InitializeUI()
    {
        SuspendLayout();
        Text = "视频音频片段提取器";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        // 完全手动 DPI 布局：禁用自动缩放，避免锚点 + AutoScale 在 DPI 切换时累积漂移
        AutoScaleMode = AutoScaleMode.None;
        ClientSize = new Size(900, 480);
        Font = Theme.FontUI;
        KeyPreview = true;
        DoubleBuffered = true;

        // 窗口左上角 / 任务栏图标：使用 exe 内嵌的 app.ico（csproj 的 ApplicationIcon）
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

        // --- 顶部工具条 ---
        btnBrowse.Text = "选择视频";
        btnBrowse.Style = ModernButtonStyle.Primary;
        btnBrowse.IconGlyph = "\uE8B7";
        btnBrowse.Click += (_, _) => BrowseVideo();

        lblFile.AutoEllipsis = true;
        lblFile.AutoSize = false;
        lblFile.Text = "未选择视频（可拖拽文件到窗口）";
        lblFile.Font = Theme.FontUI;
        lblFile.BackColor = Color.Transparent;

        btnTheme.Style = ModernButtonStyle.Ghost;
        btnTheme.Text = "主题";
        btnTheme.Click += (_, _) => Theme.Toggle();

        // --- 播放器卡片 ---
        lblDuration.Text = "总时长：--";
        lblDuration.Font = Theme.FontUI;
        lblDuration.BackColor = Color.Transparent;
        track.Scrubbing += Track_Scrubbing;
        track.ScrubEnded += Track_ScrubEnded;
        track.MarkerDragging += Track_MarkerDragging;
        track.MarkerDragEnded += Track_MarkerDragging;

        lblTime.Text = "00:00.000  /  00:00.000";
        lblTime.Font = Theme.FontMono;
        lblTime.BackColor = Color.Transparent;
        lblTime.AutoSize = false;
        btnPlay.Text = "播放";
        btnPlay.Style = ModernButtonStyle.Primary;
        btnPlay.IconGlyph = "\uE768";
        btnPlay.Radius = 999; // 最大圆角（胶囊形）：RoundRect 自动截断到高度一半，任意 DPI 下都保持全圆
        btnPlay.Click += (_, _) => TogglePlay();

        // 播放按钮右侧的标记按钮（二态：开始标记 / 结束标记）
        btnMark.Text = "开始标记";
        btnMark.Style = ModernButtonStyle.Secondary;
        btnMark.IconGlyph = "\uE7C1";
        btnMark.Radius = 999;
        btnMark.Click += BtnMark_Click;

        cardPlayer.Controls.Add(lblDuration);
        cardPlayer.Controls.Add(track);
        cardPlayer.Controls.Add(lblTime);
        cardPlayer.Controls.Add(btnPlay);
        cardPlayer.Controls.Add(btnMark);

        // --- 导出卡片 ---
        lblMark.Text = "起点 --    终点 --    区间 --";
        lblMark.Font = Theme.FontUI;
        lblMark.BackColor = Color.Transparent;
        lblMark.AutoSize = false;

        lblHint.Text = "提示：未手动标记时，导出按钮默认导出整段音频";
        lblHint.Font = new Font("Microsoft YaHei UI", 8.25F, FontStyle.Regular);
        lblHint.BackColor = Color.Transparent;
        lblHint.AutoSize = false;

        btnClear.Text = "清除标记";
        btnClear.Style = ModernButtonStyle.Ghost;
        btnClear.IconGlyph = "\uE74D";
        btnClear.Radius = 999; // 最大圆角（胶囊形）
        btnClear.Click += (_, _) => ClearMarks();

        btnPreview.Text = "预览区间";
        btnPreview.Style = ModernButtonStyle.Secondary;
        btnPreview.IconGlyph = "";
        btnPreview.Radius = 999;
        btnPreview.Click += BtnPreview_Click;

        btnExport.Text = "导出音频";
        btnExport.Style = ModernButtonStyle.Primary;
        btnExport.IconGlyph = "\uE74E";
        btnExport.Radius = 999;
        btnExport.Click += btnExport_Click;

        cardExport.Controls.Add(lblMark);
        cardExport.Controls.Add(lblHint);
        cardExport.Controls.Add(btnClear);
        cardExport.Controls.Add(btnPreview);
        cardExport.Controls.Add(btnExport);

        // --- 状态栏 ---
        lblStatus.Text = "就绪";
        lblStatus.Font = Theme.FontUI;
        lblStatus.BackColor = Color.Transparent;
        lblStatus.AutoSize = false;

        Controls.Add(btnBrowse);
        Controls.Add(lblFile);
        Controls.Add(btnTheme);
        Controls.Add(cardPlayer);
        Controls.Add(cardExport);
        Controls.Add(lblStatus);

        ResumeLayout(false);
        LayoutUi();

        KeyDown += MainForm_KeyDown;
    }

    private void ApplyTheme()
    {
        // 注意：这里不再重设 Font，避免任何缩放联动；主题切换只换颜色
        BackColor = Theme.Window;

        lblFile.ForeColor = Theme.TextPrimary;
        lblFile.BackColor = Color.Transparent;

        lblDuration.ForeColor = Theme.TextSecondary;
        lblDuration.BackColor = Color.Transparent;

        lblTime.ForeColor = Theme.TextPrimary;
        lblTime.BackColor = Color.Transparent;

        lblMark.ForeColor = Theme.TextSecondary;
        lblMark.BackColor = Color.Transparent;

        lblHint.ForeColor = Theme.TextMuted;
        lblHint.BackColor = Color.Transparent;

        lblStatus.ForeColor = Theme.TextMuted;
        lblStatus.BackColor = Color.Transparent;

        btnTheme.IconGlyph = Theme.IsDark ? "\uE706" : "\uE708";

        if (IsHandleCreated)
            Native.ApplyWindowStyle(Handle, Theme.IsDark);

        Invalidate(true);
    }

    // ===== 选视频 =====
    private void BrowseVideo()
    {
        using var ofd = new OpenFileDialog
        {
            Title = "选择视频文件",
            Filter = "视频文件|*.mp4;*.mov;*.mkv;*.avi;*.wmv;*.flv;*.webm;*.m4v;*.ts|所有文件|*.*",
            CheckFileExists = true
        };
        if (ofd.ShowDialog(this) != DialogResult.OK) return;
        LoadMedia(ofd.FileName);
    }

    private void LoadMedia(string path)
    {
        try
        {
            CloseMedia();
            reader = new MediaFoundationReader(path);
            output = new WaveOutEvent { DesiredLatency = 120, NumberOfBuffers = 3 };
            output.Init(reader);

            videoPath = path;
            lblFile.Text = Path.GetFileName(path);
            totalMs = Math.Max(1, (int)reader.TotalTime.TotalMilliseconds);

            track.Maximum = totalMs;
            track.Value = 0;

            // 默认整段：起点 0，终点总时长
            markStartMs = 0;
            markEndMs = totalMs;

            lblDuration.Text = $"总时长  {Fmt(totalMs)}    ·    {reader.WaveFormat.SampleRate}Hz / {reader.WaveFormat.Channels}ch";
            UpdateTimeLabel();
            UpdateMarkLabel();
            UpdateMarkButton();

            previewing = false;
            UpdatePreviewButton();
            SetPlaybackEnabled(true);
            SetStatus("已加载: " + Path.GetFileName(path), Theme.TextMuted);
        }
        catch (Exception ex)
        {
            CloseMedia();
            videoPath = null;
            lblFile.Text = "未选择视频（可拖拽文件到窗口）";
            totalMs = 0;
            SetPlaybackEnabled(false);
            MessageBox.Show(this,
                "无法加载该视频的音轨：\r\n" + ex.Message +
                "\r\n\r\n如果视频本身没有音轨，或系统缺少对应解码器，请换个文件试试。",
                "加载失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void CloseMedia()
    {
        try { output?.Stop(); } catch { }
        try { output?.Dispose(); } catch { }
        try { reader?.Dispose(); } catch { }
        output = null;
        reader = null;
    }

    private void SetPlaybackEnabled(bool on)
    {
        btnPlay.Enabled = on;
        btnMark.Enabled = on;
        btnClear.Enabled = on;
        if (!on) track.Maximum = 1;
        UpdateExportEnabled();
    }

    private void UpdateExportEnabled()
    {
        bool ok = videoPath != null
                  && markStartMs.HasValue
                  && markEndMs.HasValue
                  && markEndMs.Value > markStartMs.Value;
        btnExport.Enabled = ok;
        btnPreview.Enabled = ok;
    }

    // ===== 播放 =====
    private void TogglePlay()
    {
        if (output == null || reader == null) return;
        previewing = false;
        UpdatePreviewButton();
        if (output.PlaybackState == PlaybackState.Playing)
        {
            output.Pause();
        }
        else
        {
            if (reader.CurrentTime >= reader.TotalTime - TimeSpan.FromMilliseconds(50))
                SeekTo(0, true);
            output.Play();
        }
        UpdatePlayButton();
    }

    private void UpdatePlayButton()
    {
        bool playing = output?.PlaybackState == PlaybackState.Playing;
        btnPlay.Text = playing ? "暂停" : "播放";
        btnPlay.IconGlyph = playing ? "\uE769" : "\uE768";
    }

    private void Track_Scrubbing(object? sender, EventArgs e)
    {
        if (reader == null) return;
        previewing = false;
        UpdatePreviewButton();
        scrubbing = true;
        UpdateTimeLabel();
        SeekTo(track.Value, false);
    }

    private void Track_ScrubEnded(object? sender, EventArgs e)
    {
        scrubbing = false;
        SeekTo(track.Value, true);
        UpdateTimeLabel();
    }

    // 拖动轨道上的标记把手：实时同步标记区间
    private void Track_MarkerDragging(object? sender, EventArgs e)
    {
        StopPreview();
        _activeMarkerIsEnd = track.LastDraggedIsEnd;
        markStartMs = track.SelectionStart;
        markEndMs = track.SelectionEnd;
        UpdateMarkLabel();
        UpdateMarkButton();
        UpdateExportEnabled();
    }

    private void SeekTo(int ms, bool force)
    {
        if (reader == null || output == null) return;
        if (!force && (DateTime.Now - lastSeek).TotalMilliseconds < SeekThrottleMs) return;
        lastSeek = DateTime.Now;
        try
        {
            bool playing = output.PlaybackState == PlaybackState.Playing;
            if (playing) output.Pause();
            reader.CurrentTime = TimeSpan.FromMilliseconds(Math.Clamp(ms, 0, totalMs));
            if (playing) output.Play();
        }
        catch { }
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        if (reader == null) return;
        if (!scrubbing)
        {
            int ms = (int)reader.CurrentTime.TotalMilliseconds;
            track.Value = Math.Clamp(ms, 0, totalMs);
            UpdateTimeLabel();
        }

        // 区间预览：到达标记终点或播放停止时自动结束
        if (previewing &&
            (output?.PlaybackState != PlaybackState.Playing
             || (markEndMs.HasValue && reader.CurrentTime.TotalMilliseconds >= markEndMs.Value)))
        {
            previewing = false;
            try { output?.Pause(); } catch { }
            UpdatePreviewButton();
        }

        UpdatePlayButton();
    }

    private void UpdateTimeLabel()
    {
        int cur = scrubbing ? track.Value
            : reader != null ? (int)reader.CurrentTime.TotalMilliseconds : 0;
        lblTime.Text = $"{Fmt(cur)}   /   {Fmt(totalMs)}";
    }

    // ===== 区间预览 =====
    private void BtnPreview_Click(object? sender, EventArgs e)
    {
        if (previewing) { StopPreview(); return; }
        if (reader == null || output == null || videoPath == null) return;
        if (!markStartMs.HasValue || !markEndMs.HasValue || markEndMs.Value <= markStartMs.Value) return;

        SeekTo(markStartMs.Value, true);
        output.Play();
        previewing = true;
        UpdatePreviewButton();
        UpdatePlayButton();
    }

    private void StopPreview()
    {
        if (!previewing) return;
        previewing = false;
        try { output?.Pause(); } catch { }
        UpdatePreviewButton();
        UpdatePlayButton();
    }

    private void UpdatePreviewButton()
    {
        btnPreview.Text = previewing ? "停止预览" : "预览区间";
        btnPreview.IconGlyph = previewing ? "" : "";
        btnPreview.Style = previewing ? ModernButtonStyle.Primary : ModernButtonStyle.Secondary;
    }

    // ===== 标记逻辑 =====
    private void BtnMark_Click(object? sender, EventArgs e)
    {
        if (videoPath == null) return;
        StopPreview();

        if (markEndMs == null)
        {
            // 当前处于“等待终点”状态
            int v = track.Value;
            int s = markStartMs ?? 0;
            if (v <= s)
            {
                MessageBox.Show(this, "终点必须大于起点。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            markEndMs = v;
            _activeMarkerIsEnd = true;
        }
        else
        {
            // 当前是完整区间，点一下重新设起点
            markStartMs = track.Value;
            markEndMs = null;
            _activeMarkerIsEnd = false;
        }

        UpdateMarkLabel();
        UpdateMarkButton();
        UpdateExportEnabled();
    }

    private void ClearMarks()
    {
        if (videoPath == null) return;
        StopPreview();
        markStartMs = 0;
        markEndMs = totalMs;
        UpdateMarkLabel();
        UpdateMarkButton();
        UpdateExportEnabled();
    }

    private void UpdateMarkButton()
    {
        bool settingEnd = markEndMs == null;
        btnMark.Text = settingEnd ? "结束标记" : "开始标记";
        btnMark.IconGlyph = settingEnd ? "\uE73E" : "\uE7C1";
        btnMark.Style = settingEnd ? ModernButtonStyle.Primary : ModernButtonStyle.Secondary;
    }

    private void UpdateMarkLabel()
    {
        track.SelectionStart = markStartMs;
        track.SelectionEnd = markEndMs;

        string s = markStartMs.HasValue ? Fmt(markStartMs.Value) : "--";
        string e = markEndMs.HasValue ? Fmt(markEndMs.Value) : "--";
        string d = (markStartMs.HasValue && markEndMs.HasValue && markEndMs.Value > markStartMs.Value)
            ? $"{((markEndMs.Value - markStartMs.Value) / 1000.0):F3} s"
            : "--";
        lblMark.Text = $"起点 {s}    终点 {e}    区间 {d}";

        if (markEndMs == null)
            lblHint.Text = "已设置起点，请点击「结束标记」确定终点（←/→ 可微调起点）";
        else if (markStartMs == 0 && markEndMs == totalMs)
            lblHint.Text = "提示：当前为整段范围，导出整段音频；可拖动轨道上的标记把手设定区间";
        else
            lblHint.Text = "提示：当前为手动标记范围，导出以该区间为准（可拖动标记把手，或按 ←/→ 微调，Shift/Ctrl 加速）";
    }

    private static string Fmt(int ms)
    {
        var t = TimeSpan.FromMilliseconds(Math.Max(0, ms));
        return t.TotalHours >= 1 ? t.ToString(@"hh\:mm\:ss\.fff") : t.ToString(@"mm\:ss\.fff");
    }

    // ===== 导出（使用 Windows 自带的 Media Foundation 解码，无需 ffmpeg） =====
    private async void btnExport_Click(object? sender, EventArgs e)
    {
        if (videoPath == null) { MessageBox.Show(this, "请先选择视频文件。"); return; }
        if (!markStartMs.HasValue || !markEndMs.HasValue)
        { MessageBox.Show(this, "请先标记起点和终点。"); return; }

        int s = markStartMs.Value;
        int en = markEndMs.Value;
        if (en <= s) { MessageBox.Show(this, "终点必须大于起点。"); return; }

        using var sfd = new SaveFileDialog
        {
            Title = "保存提取的音频",
            Filter = "WAV 音频 (*.wav)|*.wav",
            DefaultExt = "wav",
            AddExtension = true,
            FileName = Path.GetFileNameWithoutExtension(videoPath) + "_片段.wav",
            InitialDirectory = Path.GetDirectoryName(videoPath) ?? ""
        };
        if (sfd.ShowDialog(this) != DialogResult.OK) return;

        try { output?.Pause(); } catch { }

        btnExport.Enabled = false;
        btnExport.Text = "导出中…";
        btnExport.IconGlyph = "";
        SetStatus("正在导出…", Theme.TextMuted);
        UseWaitCursor = true;

        string srcPath = videoPath;
        string dstPath = sfd.FileName;
        var progress = new Progress<int>(p => SetStatus($"正在导出… {p}%", Theme.TextMuted));

        try
        {
            await Task.Run(() => ExportSegment(srcPath, s, en, dstPath, progress));

            var fi = new FileInfo(dstPath);
            SetStatus($"完成: {fi.Name}  ({fi.Length / 1024.0:F1} KB)", Theme.Success);
        }
        catch (Exception ex)
        {
            try { if (File.Exists(dstPath)) File.Delete(dstPath); } catch { }
            SetStatus("导出出错", Theme.Danger);
            MessageBox.Show(this, "导出失败：\r\n" + ex.Message, "出错", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            UseWaitCursor = false;
            btnExport.Text = "导出音频";
            btnExport.IconGlyph = "";
            UpdateExportEnabled();
        }
    }

    // 用独立的 MediaFoundationReader 解码源文件，重采样为 44.1kHz/16bit/单声道后按字节数精确截取写入 WAV
    private static void ExportSegment(string sourcePath, int startMs, int endMs,
        string destPath, IProgress<int>? progress)
    {
        var targetFormat = new WaveFormat(OutSampleRate, OutBits, OutChannels);
        long totalBytes = (long)targetFormat.AverageBytesPerSecond * (endMs - startMs) / 1000;

        using var src = new MediaFoundationReader(sourcePath);
        src.CurrentTime = TimeSpan.FromMilliseconds(startMs);
        using var resampler = new MediaFoundationResampler(src, targetFormat) { ResamplerQuality = 60 };
        using var writer = new WaveFileWriter(destPath, targetFormat);

        var buffer = new byte[81920];
        long written = 0;
        int lastPct = -1;
        while (written < totalBytes)
        {
            int want = (int)Math.Min(buffer.Length, totalBytes - written);
            int read = resampler.Read(buffer, 0, want);
            if (read <= 0) break;
            writer.Write(buffer, 0, read);
            written += read;

            int pct = (int)(written * 100 / totalBytes);
            if (pct != lastPct)
            {
                lastPct = pct;
                progress?.Report(pct);
            }
        }
        writer.Flush();
    }

    // ===== 快捷键 / 其它 =====
    // 方向键微调最近触碰的标记：←/→ = ±10ms，Shift = ±100ms，Ctrl = ±1000ms
    private void NudgeActiveMarker(int direction, int stepMs)
    {
        if (videoPath == null) return;
        StopPreview();
        int delta = direction * stepMs;
        if (_activeMarkerIsEnd && markEndMs.HasValue)
        {
            markEndMs = Math.Clamp(markEndMs.Value + delta, (markStartMs ?? 0) + 1, totalMs);
            SetStatus($"终点 {Fmt(markEndMs.Value)}", Theme.TextMuted);
        }
        else if (markStartMs.HasValue)
        {
            markStartMs = Math.Clamp(markStartMs.Value + delta, 0, (markEndMs ?? totalMs) - 1);
            SetStatus($"起点 {Fmt(markStartMs.Value)}", Theme.TextMuted);
        }
        UpdateMarkLabel();
        UpdateExportEnabled();
    }

    // 焦点落在窗体本身时（如拖放文件后还没点过任何控件），方向键也优先用于标记微调。
    // 控件获得焦点时则由控件的 IsInputKey 放行，经 KeyPreview 走 MainForm_KeyDown。
    protected override bool ProcessDialogKey(Keys keyData)
    {
        if ((keyData & Keys.KeyCode) is Keys.Left or Keys.Right && (keyData & Keys.Alt) == 0
            && videoPath != null && (markStartMs.HasValue || markEndMs.HasValue))
        {
            int step = (keyData & Keys.Shift) != 0 ? 100 : (keyData & Keys.Control) != 0 ? 1000 : 10;
            NudgeActiveMarker((keyData & Keys.KeyCode) == Keys.Right ? 1 : -1, step);
            return true;
        }
        return base.ProcessDialogKey(keyData);
    }

    private void MainForm_KeyDown(object? sender, KeyEventArgs e)
    {
        if ((e.KeyCode == Keys.Left || e.KeyCode == Keys.Right) && !e.Alt
            && videoPath != null && (markStartMs.HasValue || markEndMs.HasValue))
        {
            int step = e.Shift ? 100 : e.Control ? 1000 : 10;
            NudgeActiveMarker(e.KeyCode == Keys.Right ? 1 : -1, step);
            e.Handled = true;
            e.SuppressKeyPress = true;
            return;
        }
        if (e.KeyCode == Keys.Space && !e.Control && !e.Alt) { TogglePlay(); e.Handled = true; return; }
        if (e.KeyCode == Keys.I && !e.Control)
        {
            if (videoPath != null)
            {
                markStartMs = track.Value;
                markEndMs = null;
                _activeMarkerIsEnd = false;
                UpdateMarkLabel();
                UpdateMarkButton();
                UpdateExportEnabled();
            }
            e.Handled = true; return;
        }
        if (e.KeyCode == Keys.O && !e.Control)
        {
            if (videoPath != null && markStartMs.HasValue && track.Value > markStartMs.Value)
            {
                markEndMs = track.Value;
                _activeMarkerIsEnd = true;
                UpdateMarkLabel();
                UpdateMarkButton();
                UpdateExportEnabled();
            }
            e.Handled = true; return;
        }
        if (e.Control && e.KeyCode == Keys.E) { btnExport.PerformClick(); e.Handled = true; return; }
    }

    private void SetStatus(string text, Color color)
    {
        lblStatus.Text = text;
        lblStatus.ForeColor = color;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        timer.Stop();
        CloseMedia();
        base.OnFormClosing(e);
    }
}
