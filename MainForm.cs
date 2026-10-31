using AudioClipExtractor.Separation;
using NAudio.Wave;

namespace AudioClipExtractor;

public class MainForm : Form
{
    // ===== 配置常量 =====
    private const int OutSampleRate = 44100;
    private const int OutBits = 16;
    private const int OutChannels = 1;
    private const int SeekThrottleMs = 120;
    private const int DesignW = 900;
    private const int DesignH = 542;   // 552 - 10：状态文字上下边距由 21 收紧到 16

    // ===== 控件 =====
    private readonly ModernButton btnBrowse = new();
    private readonly ModernButton btnTheme = new();
    private readonly ModernButton btnFiles = new();    // 文件管理（模型/缓存目录）
    private readonly ModernMenu fileMenu = new();     // 文件管理二级菜单
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
    private readonly ModernScrollPanel trackList = new();
    private readonly ModernButton btnClear = new();
    private readonly ModernButton btnSeparate = new();
    private readonly ModernButton btnClearStems = new();   // 清除已分离出来的音轨
    private readonly ModernButton btnPreview = new();
    private readonly ModernButton btnExport = new();
    private readonly ModernMenu sepMenu = new();   // 「分离音轨」按钮弹出的引擎选择菜单

    private readonly Label lblStatus = new();
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 50 };

    // ===== 状态 =====
    private WaveOutEvent? output;
    private WaveStream? reader;
    private string? videoPath;
    private readonly List<AudioTrackInfo> tracks = new();
    private readonly List<TrackRow> trackRows = new();
    private TimeSpan mediaDuration;
    private int selectedTrack;
    private int totalMs;
    private int? markStartMs;
    private int? markEndMs;
    private bool _activeMarkerIsEnd;   // 最近触碰的标记（←/→ 微调目标）：true=终点，false=起点
    private DateTime lastSeek = DateTime.MinValue;
    private bool scrubbing;
    private bool previewing;
    private bool separating;
    private CancellationTokenSource? sepCts;

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
        // 按当前屏幕 DPI 设定初始客户区尺寸（设计值 900x552 @96DPI）
        float s = DeviceDpi / 96f;
        ClientSize = new Size((int)Math.Round(DesignW * s), (int)Math.Round(DesignH * s));
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
        MinimumSize = new Size((int)Math.Round(DesignW * s) + borderW, (int)Math.Round(DesignH * s) + borderH);
    }

    /// <summary>
    /// 手动布局：所有控件的位置和尺寸由设计值（96 DPI 下的 900x552）× 当前 DPI 系数算出，
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
        btnFiles.SetBounds(btnTheme.Left - Sc(8) - Sc(118), Sc(18), Sc(118), Sc(34));
        lblFile.SetBounds(Sc(160), Sc(27), Math.Max(Sc(60), btnFiles.Left - Sc(16) - Sc(160)), Sc(20));

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
        int cardH = Math.Max(Sc(228), H - cardTop - statusMargin - statusH - statusMargin);
        cardExport.SetBounds(Sc(16), cardTop, cardW, cardH);
        lblMark.SetBounds(Sc(20), Sc(18), cardW - Sc(40), Sc(22));
        lblHint.SetBounds(Sc(20), Sc(44), cardW - Sc(40), Sc(20));
        // 音轨列表：按钮行贴卡片底部，列表高度填充中间剩余空间
        int btnY = cardH - Sc(8) - Sc(36);
        trackList.SetBounds(Sc(20), Sc(70), cardW - Sc(40), btnY - Sc(10) - Sc(70));
        trackList.RowHeight = Sc(46);
        trackList.Gap = Sc(6);
        btnClear.SetBounds(Sc(20), btnY, Sc(118), Sc(36));
        btnSeparate.SetBounds(Sc(150), btnY, Sc(132), Sc(36));      // 设计 x=150，紧邻“清除标记”
        btnClearStems.SetBounds(Sc(294), btnY, Sc(118), Sc(36));   // 紧跟“分离音轨”
        btnPreview.SetBounds(cardW - Sc(328), btnY, Sc(148), Sc(36)); // 设计 x=540，距卡片右缘 328
        btnExport.SetBounds(cardW - Sc(168), btnY, Sc(148), Sc(36));  // 设计 x=700，距卡片右缘 168

        // 状态文字：距上方卡片 16px、距底边 16px 对称留白
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
        ClientSize = new Size(DesignW, DesignH);
        Font = Theme.FontUI;
        KeyPreview = true;
        DoubleBuffered = true;

        // 窗口左上角 / 任务栏图标：使用 exe 内嵌的 app.ico（csproj 的 ApplicationIcon）
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

        // --- 顶部工具条 ---
        btnBrowse.Text = "选择文件";
        btnBrowse.Style = ModernButtonStyle.Primary;
        btnBrowse.IconGlyph = "\uE8B7";
        btnBrowse.Click += (_, _) => BrowseVideo();

        lblFile.AutoEllipsis = true;
        lblFile.AutoSize = false;
        lblFile.Text = "未选择文件（可拖拽文件到窗口）";
        lblFile.Font = Theme.FontUI;
        lblFile.BackColor = Color.Transparent;

        btnTheme.Style = ModernButtonStyle.Ghost;
        btnTheme.Text = "主题";
        btnTheme.Click += (_, _) => Theme.Toggle();

        btnFiles.Style = ModernButtonStyle.Ghost;
        btnFiles.Text = "文件管理";
        btnFiles.IconGlyph = "\uE838";   // 沿用原来的“模型文件”图标
        btnFiles.Click += (_, _) => fileMenu.ShowAt(btnFiles);

        // 文件管理二级菜单：模型目录 / 缓存目录 / 清理缓存
        fileMenu.Add("模型文件", "\uE838", OpenModelsFolder);
        fileMenu.Add("缓存文件", "\uE8B7", OpenCacheFolder);
        fileMenu.Add("清理缓存", "\uE74D", ClearCacheFiles);

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
        // 音轨列表：多音轨时逐条展示（行内可单独预览），超出高度用自绘滚动条
        trackList.RowHeight = 46;
        trackList.Gap = 6;
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

        btnSeparate.Text = "分离音轨";
        btnSeparate.Style = ModernButtonStyle.Secondary;
        btnSeparate.IconGlyph = "\uE8EF";
        btnSeparate.Radius = 999;
        btnSeparate.Click += BtnSeparate_Click;

        btnClearStems.Text = "清理音轨";
        btnClearStems.Style = ModernButtonStyle.Ghost;
        btnClearStems.IconGlyph = "\uE74D";
        btnClearStems.Radius = 999;
        btnClearStems.Click += (_, _) => ClearStemTracks();
        btnClearStems.Enabled = false;

        // 分离引擎选择：Demucs 4 轨 / MDX-Net 2 轨
        sepMenu.Add("Demucs · 4 轨（不同乐器）", "\uE8EF", SepDemucs);
        sepMenu.Add("MDX-Net · 2 轨（伴奏 / 人声）", "\uE8D6", SepMdx);

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
        cardExport.Controls.Add(trackList);
        cardExport.Controls.Add(btnClear);
        cardExport.Controls.Add(btnSeparate);
        cardExport.Controls.Add(btnClearStems);
        cardExport.Controls.Add(btnPreview);
        cardExport.Controls.Add(btnExport);

        // --- 状态栏 ---
        lblStatus.Text = "就绪";
        lblStatus.Font = Theme.FontUI;
        lblStatus.BackColor = Color.Transparent;
        lblStatus.AutoSize = false;

        Controls.Add(btnBrowse);
        Controls.Add(lblFile);
        Controls.Add(btnFiles);
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
            Title = "选择视频或音频文件",
            Filter = "媒体文件|*.mp4;*.mov;*.mkv;*.avi;*.wmv;*.flv;*.webm;*.m4v;*.ts;*.mp3;*.wav;*.m4a;*.aac;*.flac;*.ogg;*.opus;*.wma" +
                     "|视频文件|*.mp4;*.mov;*.mkv;*.avi;*.wmv;*.flv;*.webm;*.m4v;*.ts" +
                     "|音频文件|*.mp3;*.wav;*.m4a;*.aac;*.flac;*.ogg;*.opus;*.wma" +
                     "|所有文件|*.*",
            CheckFileExists = true
        };
        if (ofd.ShowDialog(this) != DialogResult.OK) return;
        LoadMedia(ofd.FileName);
    }

    private void LoadMedia(string path)
    {
        sepCts?.Cancel(); // 正在分离时切换文件，先取消旧任务
        try
        {
            CloseMedia();
            ClearTrackRows();
            tracks.Clear();
            tracks.AddRange(MediaTracks.Enumerate(path, out mediaDuration));

            videoPath = path;
            lblFile.Text = Path.GetFileName(path);
            totalMs = Math.Max(1, (int)mediaDuration.TotalMilliseconds);
            selectedTrack = 0;

            BuildTrackRows();
            OpenReader(0);

            track.Maximum = totalMs;
            track.Value = 0;

            // 默认整段：起点 0，终点总时长
            markStartMs = 0;
            markEndMs = totalMs;

            UpdateTrackSummary();
            UpdateTimeLabel();
            UpdateMarkLabel();
            UpdateMarkButton();

            previewing = false;
            UpdatePreviewButton();
            SetPlaybackEnabled(true);
            SetStatus(tracks.Count > 1
                ? $"已加载: {Path.GetFileName(path)}（{tracks.Count} 条音轨）"
                : "已加载: " + Path.GetFileName(path), Theme.TextMuted);
        }
        catch (Exception ex)
        {
            CloseMedia();
            ClearTrackRows();
            tracks.Clear();
            videoPath = null;
            lblFile.Text = "未选择文件（可拖拽文件到窗口）";
            totalMs = 0;
            SetPlaybackEnabled(false);
            MessageBox.Show(this,
                "无法加载该文件的音轨：\r\n" + ex.Message +
                "\r\n\r\n如果文件本身没有音轨，或系统缺少对应解码器，请换个文件试试。",
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

    /// <summary>按当前选中音轨重建解码器与播放设备，并定位到 positionMs。</summary>
    private void OpenReader(int positionMs)
    {
        try { output?.Stop(); } catch { }
        try { output?.Dispose(); } catch { }
        try { reader?.Dispose(); } catch { }
        output = null;
        reader = null;

        if (videoPath == null || selectedTrack < 0 || selectedTrack >= tracks.Count) return;

        var info = tracks[selectedTrack];
        // 分离出来的音轨是独立 WAV 文件，直接读文件；容器音轨才走 Media Foundation
        reader = info.StemPath != null
            ? new WaveFileReader(info.StemPath)
            : TrackWaveStream.Open(videoPath, info, mediaDuration);
        output = new WaveOutEvent { DesiredLatency = 120, NumberOfBuffers = 3 };
        output.Init(reader);
        if (positionMs > 0) reader.CurrentTime = TimeSpan.FromMilliseconds(Math.Min(positionMs, totalMs));
        UpdatePlayButton();
    }

    private void SetPlaybackEnabled(bool on)
    {
        btnPlay.Enabled = on;
        btnMark.Enabled = on;
        btnClear.Enabled = on;
        btnSeparate.Enabled = on;
        if (!on) btnClearStems.Enabled = false;
        if (!on) track.Maximum = 1;
        UpdateExportEnabled();
    }

    // ===== 音轨列表 =====
    private void BuildTrackRows()
    {
        ClearTrackRows();
        for (int i = 0; i < tracks.Count; i++)
        {
            int index = i;
            var row = new TrackRow { Index = index, Title = tracks[i].Title, Detail = tracks[i].Detail };
            row.SelectRequested += (_, _) => SelectTrack(index);
            row.PreviewRequested += (_, _) => PreviewTrack(index);
            trackList.AddRow(row);
            trackRows.Add(row);
        }
        UpdateTrackRows();
    }

    private void ClearTrackRows()
    {
        trackList.ClearRows();
        trackRows.Clear();
    }

    private void UpdateTrackRows()
    {
        for (int i = 0; i < trackRows.Count; i++)
            trackRows[i].IsSelected = i == selectedTrack;
    }

    private void UpdateTrackSummary()
    {
        if (selectedTrack < 0 || selectedTrack >= tracks.Count) return;
        var info = tracks[selectedTrack];
        int containerCount = tracks.Count(t => !t.IsStem);
        string which = info.IsStem ? info.Title : $"音轨 {info.Ordinal}/{containerCount}";
        lblDuration.Text =
            $"总时长 {Fmt(totalMs)}    ·    {which}    ·    {info.SampleRate}Hz / {info.Channels}ch";
    }

    /// <summary>切换播放/导出所使用音轨；保留当前播放位置。</summary>
    private void SelectTrack(int index)
    {
        if (videoPath == null || index < 0 || index >= tracks.Count || index == selectedTrack) return;
        StopPreview();
        int keepMs = reader != null ? (int)Math.Min(reader.CurrentTime.TotalMilliseconds, totalMs) : 0;
        selectedTrack = index;
        OpenReader(keepMs);
        UpdateTrackRows();
        UpdateTrackSummary();
        UpdateExportEnabled();
        SetStatus($"已切换到 {tracks[index].Title}（{tracks[index].Detail}）", Theme.TextMuted);
    }

    /// <summary>列表行内的“预览”：先切到该音轨，再按当前标记区间预览。</summary>
    private void PreviewTrack(int index)
    {
        if (selectedTrack != index) SelectTrack(index);
        BtnPreview_Click(null, EventArgs.Empty);
    }

    private void UpdateExportEnabled()
    {
        bool ok = videoPath != null
                  && markStartMs.HasValue
                  && markEndMs.HasValue
                  && markEndMs.Value > markStartMs.Value;
        btnExport.Enabled = ok;
        btnPreview.Enabled = ok;
        btnClearStems.Enabled = videoPath != null && !separating && tracks.Any(t => t.IsStem);
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

        var info = tracks[selectedTrack];
        int containerCount = tracks.Count(t => !t.IsStem);
        string suffix = info.IsStem
            ? $"_{PathSafe(info.StemName ?? "分离")}_片段.wav"
            : containerCount > 1 ? $"_音轨{info.Ordinal}_片段.wav" : "_片段.wav";

        using var sfd = new SaveFileDialog
        {
            Title = "保存提取的音频",
            Filter = "WAV 音频 (*.wav)|*.wav",
            DefaultExt = "wav",
            AddExtension = true,
            FileName = Path.GetFileNameWithoutExtension(videoPath) + suffix,
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
        var srcInfo = info;
        var srcDuration = mediaDuration;

        try
        {
            await Task.Run(() => ExportSegment(srcPath, srcInfo, srcDuration, s, en, dstPath, progress));

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
            btnExport.IconGlyph = "\uE74E";
            UpdateExportEnabled();
        }
    }

    // 用独立的解码器读取指定音轨，重采样为 44.1kHz/16bit/单声道后按字节数精确截取写入 WAV
    private static void ExportSegment(string sourcePath, AudioTrackInfo info, TimeSpan duration,
        int startMs, int endMs, string destPath, IProgress<int>? progress)
    {
        var targetFormat = new WaveFormat(OutSampleRate, OutBits, OutChannels);
        long totalBytes = (long)targetFormat.AverageBytesPerSecond * (endMs - startMs) / 1000;

        using var src = info.StemPath != null
            ? (WaveStream)new WaveFileReader(info.StemPath)
            : TrackWaveStream.Open(sourcePath, info, duration);
        var seekTo = TimeSpan.FromMilliseconds(startMs);
        src.CurrentTime = seekTo < src.TotalTime ? seekTo : src.TotalTime;
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

    // ===== AI 音源分离（Demucs 4 轨 / MDX-Net 2 轨，纯 CPU） =====
    private void BtnSeparate_Click(object? sender, EventArgs e)
    {
        if (separating)
        {
            btnSeparate.Enabled = false;
            SetStatus("正在取消分离…", Theme.TextMuted);
            sepCts?.Cancel();
            return;
        }

        if (videoPath == null) { MessageBox.Show(this, "请先选择视频或音频文件。"); return; }

        // 选中分离轨时自动回退到它所属的原始音轨，方便换引擎重新分离并覆盖上一次结果
        var info = SeparationSource();
        if (info == null) return;

        // 弹出引擎选择菜单
        sepMenu.ShowAt(btnSeparate);
    }

    /// <summary>分离所用的源音轨：选中分离轨时，回退到它所属的原始音轨。</summary>
    private AudioTrackInfo? SeparationSource()
    {
        if (selectedTrack < 0 || selectedTrack >= tracks.Count) return null;
        var info = tracks[selectedTrack];
        return info.IsStem
            ? tracks.FirstOrDefault(t => !t.IsStem && t.StreamIndex == info.StreamIndex)
            : info;
    }

    private async void SepDemucs() => await StartSeparateAsync(true);

    private async void SepMdx() => await StartSeparateAsync(false);

    private async Task StartSeparateAsync(bool demucs)
    {
        if (separating || videoPath == null) return;
        var info = SeparationSource();
        if (info == null) return;
        await SeparateAsync(info, demucs);
    }

    private async Task SeparateAsync(AudioTrackInfo info, bool demucs)
    {
        // 1) 准备模型与轨道名（stemNames 用作缓存文件名，displayNames 是界面显示名）
        string modelPath;
        string modelKey;
        string[] stemNames;
        string[] displayNames;
        if (demucs)
        {
            var found = ModelStore.FindDemucs();
            if (found == null)
            {
                MessageBox.Show(this,
                    "未找到 Demucs 模型文件（htdemucs.onnx）。\r\n\r\n" +
                    "请先在本机运行一次：\r\n" +
                    "    python tools\\export_demucs.py\r\n\r\n" +
                    "脚本会把模型写入：\r\n" +
                    ModelStore.DemucsPath + "\r\n\r\n" +
                    "生成后重新点击「分离音轨」即可。",
                    "分离音轨", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            modelPath = found;
            modelKey = ModelStore.DemucsFileName;
            stemNames = DemucsSeparator.SourceNames;
            // Demucs 输出的是通用乐器轨，界面统一叫 音轨1…音轨N
            displayNames = Enumerable.Range(1, stemNames.Length).Select(i => $"音轨{i}").ToArray();
        }
        else
        {
            var model = MdxModelInfo.InstHq3;
            if (!ModelStore.IsInstalled(model))
            {
                var answer = MessageBox.Show(this,
                    $"首次使用需要下载音源分离模型（约 {model.Size / 1048576.0:0} MB）。\r\n\r\n" +
                    $"模型：{model.DisplayName}\r\n" +
                    "下载源为 Hugging Face 镜像，文件保存在本地，之后无需重复下载。\r\n\r\n" +
                    "现在开始下载并分离吗？",
                    "分离音轨", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
                if (answer != DialogResult.OK) return;
            }
            modelPath = ModelStore.PathFor(model);
            modelKey = model.FileName;
            stemNames = new[] { model.PrimaryStem, model.SecondaryStem };
            displayNames = stemNames.Select(StemDisplayName).ToArray();
        }

        StopPreview();

        string srcPath = videoPath!;
        var srcDuration = mediaDuration;
        var cacheDir = StemCache.DirFor(srcPath, info, modelKey);
        var paths = stemNames.Select(n => StemCache.StemPath(cacheDir, n)).ToArray();
        bool cached = paths.All(File.Exists);   // 之前分离过，直接复用结果

        string engine = demucs ? "Demucs" : "MDX-Net";
        bool completed = false;

        separating = true;
        sepCts = new CancellationTokenSource();
        var ct = sepCts.Token;
        btnSeparate.Text = "取消分离";
        btnSeparate.Style = ModernButtonStyle.Danger;
        btnSeparate.IconGlyph = "\uE711";
        SetStatus($"准备分离「{info.Title}」（{engine}）…", Theme.TextMuted);
        UpdateExportEnabled();   // 分离期间禁用「清理音轨」

        try
        {
            if (!cached)
            {
                var decode = new Progress<double>(p =>
                    SetStatus($"正在分离音轨（{engine}）… 解码 {(int)(p * 100)}%", Theme.TextMuted));
                var infer = new Progress<double>(p =>
                    SetStatus($"正在分离音轨（{engine}）… 推理 {(int)Math.Round(20 + p * 80)}%", Theme.TextMuted));

                if (!demucs)
                {
                    var model = MdxModelInfo.InstHq3;
                    var download = new Progress<(long Received, long Total)>(p =>
                    {
                        double f = p.Total <= 0 ? 0 : (double)p.Received / p.Total;
                        SetStatus($"正在下载分离模型… {(int)Math.Round(f * 100)}%  " +
                                  $"({p.Received / 1048576.0:0.0} / {p.Total / 1048576.0:0.0} MB)", Theme.TextMuted);
                    });
                    modelPath = await ModelStore.EnsureAsync(model, download, ct);

                    await Task.Run(() =>
                    {
                        var stereo = AudioUtil.DecodeStereo(srcPath, info, srcDuration, decode, ct);
                        var stems = MdxSeparator.Separate(modelPath, model, stereo, infer, ct);
                        AudioUtil.WritePcm16Stereo(paths[0], stems.Primary);
                        AudioUtil.WritePcm16Stereo(paths[1], stems.Secondary);
                    }, ct);
                }
                else
                {
                    await Task.Run(() =>
                    {
                        var stereo = AudioUtil.DecodeStereo(srcPath, info, srcDuration, decode, ct);
                        var stems = DemucsSeparator.Separate(modelPath, stereo, infer, ct);
                        for (int i = 0; i < stems.Length; i++)
                        {
                            ct.ThrowIfCancellationRequested();
                            AudioUtil.WritePcm16Stereo(paths[i], stems[i]);
                        }
                    }, ct);
                }
            }

            if (videoPath != srcPath)
            {
                SetStatus("视频已切换，分离结果未加入列表", Theme.TextMuted);
                return;
            }

            AddStemTracks(info, paths, displayNames);
            completed = true;
            SetStatus($"分离完成：已生成 {paths.Length} 条音轨（" +
                      string.Join("、", displayNames) + "），可单独预览或导出",
                Theme.Success);
        }
        catch (OperationCanceledException)
        {
            if (videoPath == srcPath) SetStatus("已取消分离", Theme.TextMuted);
        }
        catch (Exception ex)
        {
            SetStatus("分离失败", Theme.Danger);
            MessageBox.Show(this, "分离音轨失败：\r\n" + ex.Message, "出错",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            if (!completed)
            {
                foreach (var p in paths) { try { if (File.Exists(p)) File.Delete(p); } catch { } }
            }
            separating = false;
            sepCts?.Dispose();
            sepCts = null;
            btnSeparate.Enabled = true;
            btnSeparate.Text = "分离音轨";
            btnSeparate.Style = ModernButtonStyle.Secondary;
            btnSeparate.IconGlyph = "\uE8EF";
            UpdateExportEnabled();   // 分离结束后按是否有分离轨恢复「清理音轨」
        }
    }

    /// <summary>把分离结果登记成音轨：先移除该原始音轨已有的分离轨（换引擎时替换），再逐条追加。</summary>
    private void AddStemTracks(AudioTrackInfo source, string[] paths, string[] names)
    {
        RemoveStemTracks(source.StreamIndex);

        for (int i = 0; i < paths.Length; i++)
        {
            tracks.Add(new AudioTrackInfo
            {
                StreamIndex = source.StreamIndex,
                Ordinal = tracks.Count + 1,
                Channels = 2,
                SampleRate = MdxSeparator.SampleRate,
                StemPath = paths[i],
                StemName = names[i]
            });
        }

        BuildTrackRows();
        trackList.ScrollToEnd();   // 新行在列表末尾，自动滚出来
        UpdateTrackSummary();
        UpdateExportEnabled();
    }

    /// <summary>清理当前视频所有分离出来的音轨（缓存文件保留，下次分离可直接复用）。</summary>
    private void ClearStemTracks()
    {
        if (separating || !tracks.Any(t => t.IsStem)) return;

        var stemStreams = tracks.Where(t => t.IsStem).Select(t => t.StreamIndex).Distinct().ToList();
        foreach (var id in stemStreams) RemoveStemTracks(id);

        BuildTrackRows();
        UpdateTrackSummary();
        UpdateExportEnabled();
        SetStatus("已清理分离音轨", Theme.TextMuted);
    }

    /// <summary>移除某条原始音轨已生成的分离轨；若正在播放的正是它们，切回原始音轨。</summary>
    private void RemoveStemTracks(int streamIndex)
    {
        var removed = tracks.Where(t => t.IsStem && t.StreamIndex == streamIndex).ToList();
        if (removed.Count == 0) return;

        bool selectionRemoved = selectedTrack >= 0 && selectedTrack < tracks.Count
                                && removed.Contains(tracks[selectedTrack]);
        if (selectionRemoved)
        {
            // 先释放当前播放的 WAV 解码器，再定位回原始音轨
            StopPreview();
            CloseMedia();
            selectedTrack = Math.Max(0, tracks.FindIndex(t => !t.IsStem && t.StreamIndex == streamIndex));
        }

        foreach (var t in removed) tracks.Remove(t);

        if (selectionRemoved)
        {
            OpenReader(0);
            UpdateTimeLabel();
        }
        else
        {
            selectedTrack = Math.Min(selectedTrack, Math.Max(0, tracks.Count - 1));
        }
    }

    private static string StemDisplayName(string raw) => raw switch
    {
        "Instrumental" => "伴奏（分离）",
        "Vocals" => "人声（分离）",
        _ => raw
    };

    private static string PathSafe(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name;
    }

    // 滚轮位于音轨列表上时滚动列表（无论焦点落在哪个控件）
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if (trackList.CanScroll)
        {
            var p = trackList.PointToClient(Cursor.Position);
            if (trackList.ClientRectangle.Contains(p))
            {
                trackList.ScrollBy(-e.Delta / 120 * (trackList.RowHeight + trackList.Gap));
                return;
            }
        }
        base.OnMouseWheel(e);
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

    /// <summary>打开模型文件所在目录，并在状态栏告知用户模型保存位置。</summary>
    private void OpenModelsFolder()
    {
        var dir = ModelStore.ModelDir;
        try
        {
            Directory.CreateDirectory(dir);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{dir}\"")
            {
                UseShellExecute = true
            });
        }
        catch { }

        SetStatus($"模型目录：{dir}（Demucs 与 MDX-Net 模型都保存在这里）", Theme.TextMuted);
    }

    /// <summary>打开分离缓存目录。</summary>
    private void OpenCacheFolder()
    {
        var dir = StemCache.Root;
        try
        {
            Directory.CreateDirectory(dir);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{dir}\"")
            {
                UseShellExecute = true
            });
        }
        catch { }

        SetStatus($"缓存目录：{dir}（按视频与模型缓存分离结果）", Theme.TextMuted);
    }

    /// <summary>清除全部分离缓存；界面上已加载的分离轨会一并移除。</summary>
    private void ClearCacheFiles()
    {
        if (separating)
        {
            MessageBox.Show(this, "分离正在进行，完成后再清理缓存。", "清除缓存",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        long size = CacheSize();
        if (!Directory.Exists(StemCache.Root) || size <= 0)
        {
            SetStatus("缓存目录为空，无需清理", Theme.TextMuted);
            return;
        }

        var answer = MessageBox.Show(this,
            $"将删除全部分离缓存（约 {FormatSize(size)}），下次分离需要重新计算。\r\n\r\n确定清除吗？",
            "清除缓存", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (answer != DialogResult.Yes) return;

        StopPreview();
        if (tracks.Any(t => t.IsStem)) ClearStemTracks();

        int failed = 0;
        try
        {
            foreach (var file in Directory.GetFiles(StemCache.Root))
            {
                try { File.Delete(file); } catch { failed++; }
            }
            foreach (var sub in Directory.GetDirectories(StemCache.Root))
            {
                try { Directory.Delete(sub, true); } catch { failed++; }
            }
        }
        catch { failed++; }

        if (failed > 0)
            SetStatus($"缓存部分清除失败（{failed} 项被占用）", Theme.Danger);
        else
            SetStatus("缓存已清除", Theme.Success);
    }

    private static long CacheSize()
    {
        try
        {
            long size = 0;
            foreach (var fi in new DirectoryInfo(StemCache.Root).EnumerateFiles("*", SearchOption.AllDirectories))
                size += fi.Length;
            return size;
        }
        catch { return 0; }
    }

    private static string FormatSize(long bytes) =>
        bytes >= 1L << 30
            ? $"{bytes / 1024.0 / 1024.0 / 1024.0:0.0} GB"
            : $"{bytes / 1024.0 / 1024.0:0.0} MB";

    private void SetStatus(string text, Color color)
    {
        lblStatus.Text = text;
        lblStatus.ForeColor = color;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        timer.Stop();
        sepCts?.Cancel();
        CloseMedia();
        base.OnFormClosing(e);
    }
}
