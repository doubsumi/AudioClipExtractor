using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace AudioClipExtractor;

public enum ModernButtonStyle { Primary, Secondary, Ghost, Danger }

public class ModernButton : Button
{
    private ModernButtonStyle _style = ModernButtonStyle.Secondary;
    private bool _hover, _pressed;
    private int _radius = 8;

    public ModernButtonStyle Style { get => _style; set { _style = value; Invalidate(); } }
    public int Radius { get => _radius; set { _radius = value; Invalidate(); } }
    public string IconGlyph { get; set; } = "";
    public int IconSize { get; set; } = 13;

    public ModernButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                 ControlStyles.SupportsTransparentBackColor, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        BackColor = Color.Transparent;
        Font = Theme.FontUI;
        Size = new Size(110, 36);
        Cursor = Cursors.Hand;
        UseVisualStyleBackColor = false;
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _pressed = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }

    // 方向键交给表单做标记微调（KeyPreview → MainForm_KeyDown），不在按钮间做焦点导航
    protected override bool IsInputKey(Keys keyData)
    {
        if ((keyData & Keys.KeyCode) is Keys.Left or Keys.Right) return true;
        return base.IsInputKey(keyData);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaintBackground(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        Color bg, fg, border;
        if (!Enabled)
        {
            bg = Theme.IsDark ? Color.FromArgb(40, 40, 46) : Color.FromArgb(238, 240, 245);
            fg = Theme.TextMuted; border = Color.Transparent;
        }
        else switch (_style)
        {
            case ModernButtonStyle.Primary:
                bg = _pressed ? Theme.AccentPressed : _hover ? Theme.AccentHover : Theme.Accent;
                fg = Color.White; border = Color.Transparent; break;
            case ModernButtonStyle.Danger:
                bg = _pressed ? Color.FromArgb(180, 48, 48) : _hover ? Theme.Danger : Color.FromArgb(200, 60, 60);
                fg = Color.White; border = Color.Transparent; break;
            case ModernButtonStyle.Ghost:
                bg = _pressed ? (Theme.IsDark ? Color.FromArgb(36, 36, 44) : Color.FromArgb(232, 234, 240))
                    : _hover     ? (Theme.IsDark ? Color.FromArgb(28, 28, 34) : Color.FromArgb(242, 243, 247))
                    : Color.Transparent;
                fg = Theme.TextPrimary; border = Color.Transparent; break;
            default: // Secondary
                bg = _pressed ? Theme.CardAlt : _hover ? (Theme.IsDark ? Color.FromArgb(50, 50, 60) : Color.FromArgb(246, 248, 252)) : Theme.Card;
                fg = Theme.TextPrimary; border = Theme.Border; break;
        }

        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        using (var path = RoundRect(r, _radius))
        {
            if (bg.A > 0)
            {
                using var b = new SolidBrush(bg);
                g.FillPath(b, path);
            }
            if (border != Color.Transparent)
            {
                using var p = new Pen(border, 1);
                g.DrawPath(p, path);
            }
        }

        // icon + text
        var flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
        Font? iconFont = null;
        Size iconSize = Size.Empty;
        if (!string.IsNullOrEmpty(IconGlyph))
        {
            iconFont = new Font("Segoe MDL2 Assets", IconSize, FontStyle.Regular, GraphicsUnit.Point);
            iconSize = TextRenderer.MeasureText(g, IconGlyph, iconFont, new Size(int.MaxValue, int.MaxValue), flags);
        }
        var textSize = TextRenderer.MeasureText(g, Text, Font, new Size(int.MaxValue, int.MaxValue), flags);
        int gap = iconFont != null ? 7 : 0;
        int totalW = (iconFont != null ? iconSize.Width : 0) + gap + textSize.Width;
        int cx = (Width - totalW) / 2;
        int cy = Height / 2;

        if (iconFont != null)
        {
            TextRenderer.DrawText(g, IconGlyph, iconFont,
                new Point(cx, cy - iconSize.Height / 2), fg, flags);
            cx += iconSize.Width + gap;
            iconFont.Dispose();
        }
        TextRenderer.DrawText(g, Text, Font,
            new Point(cx, cy - textSize.Height / 2), fg, flags);
    }

    internal static GraphicsPath RoundRect(Rectangle r, int radius)
    {
        var p = new GraphicsPath();
        int d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        if (d <= 0) { p.AddRectangle(r); return p; }
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
}

public class ModernCard : Panel
{
    public int Radius { get; set; } = 12;

    public ModernCard()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                 ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        DoubleBuffered = true;
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        // 把控件区域裁剪为圆角矩形：圆角外直接由父容器绘制，杜绝任何黑色残留/边框
        var r = ClientRectangle;
        r.Width -= 1; r.Height -= 1;
        if (r.Width <= 0 || r.Height <= 0) return;
        using var path = ModernButton.RoundRect(r, Radius);
        Region?.Dispose();
        Region = new Region(path);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = ClientRectangle;
        r.Width -= 1; r.Height -= 1;
        if (r.Width <= 0 || r.Height <= 0) return;

        // 先用父容器背景铺满（抗锯齿边缘的混合底色），再绘制圆角卡片
        Color parentBg = Parent?.BackColor ?? Theme.Window;
        if (parentBg.A > 0)
        {
            using var pb = new SolidBrush(parentBg);
            g.FillRectangle(pb, ClientRectangle);
        }

        using var path = ModernButton.RoundRect(r, Radius);
        using var b = new SolidBrush(Theme.Card);
        g.FillPath(b, path);
        // Region 裁剪后描边外侧一半会被裁掉，用 2px 笔保证可见 1px 边框
        using var pen = new Pen(Theme.Border, 2);
        g.DrawPath(pen, path);
    }
}

public class ModernTrackBar : Control
{
    private enum DragPart { None, Value, Start, End }

    private int _min, _max = 1000, _value;
    private int? _selStart, _selEnd;
    private bool _hover;
    private DragPart _dragPart = DragPart.None;
    private DragPart _hoverPart = DragPart.None;
    private const int GrabPx = 8;

    public int Minimum { get => _min; set { _min = value; Invalidate(); } }
    public int Maximum { get => _max; set { _max = Math.Max(value, _min + 1); Invalidate(); } }
    public int Value   { get => _value; set { _value = Math.Clamp(value, _min, _max); Invalidate(); } }

    public int? SelectionStart { get => _selStart; set { _selStart = value; Invalidate(); } }
    public int? SelectionEnd   { get => _selEnd;   set { _selEnd   = value; Invalidate(); } }

    public event EventHandler? Scrubbing;
    public event EventHandler? ScrubEnded;

    // 标记拖动：拖动过程中持续触发 MarkerDragging，松开后触发 MarkerDragEnded
    public event EventHandler? MarkerDragging;
    public event EventHandler? MarkerDragEnded;

    // 最近一次按下命中的标记：true=终点把手，false=起点把手（松手后保留，供键盘微调判断目标）
    public bool LastDraggedIsEnd { get; private set; }

    public ModernTrackBar()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                 ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Height = 80; // 轨道靠下，上方留出拖动气泡 / 标记把手的空间
        Cursor = Cursors.Hand;
    }

    private Rectangle TrackRect => new Rectangle(12, Height - 22, Width - 24, 8);

    private int ValueToX(int v)
    {
        var t = TrackRect;
        if (t.Width <= 0 || _max <= _min) return t.Left;
        return t.Left + (int)Math.Round((double)(v - _min) / (_max - _min) * t.Width);
    }

    private int XToValue(int x)
    {
        var t = TrackRect;
        if (t.Width <= 0) return _min;
        double r = (double)(x - t.Left) / t.Width;
        r = Math.Clamp(r, 0, 1);
        return _min + (int)Math.Round(r * (_max - _min));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaintBackground(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var t = TrackRect;
        using (var path = ModernButton.RoundRect(t, 4))
        using (var b = new SolidBrush(Theme.TrackBg))
            g.FillPath(b, path);

        // 选区高亮
        if (_selStart.HasValue && _selEnd.HasValue && _selEnd.Value > _selStart.Value)
        {
            int x1 = ValueToX(_selStart.Value);
            int x2 = ValueToX(_selEnd.Value);
            var selRect = new Rectangle(x1, t.Top, Math.Max(2, x2 - x1), t.Height);
            using var sp = ModernButton.RoundRect(selRect, 4);
            using var sb = new SolidBrush(Theme.AccentSoft);
            g.FillPath(sb, sp);
        }

        // 已播放
        int vx = ValueToX(_value);
        if (vx > t.Left)
        {
            var fill = new Rectangle(t.Left, t.Top, vx - t.Left, t.Height);
            using var fp = ModernButton.RoundRect(fill, 4);
            using var fb = new SolidBrush(Theme.Accent);
            g.FillPath(fb, fp);
        }

        // 标记点（竖线 + 可拖拽小把手）
        DrawMarker(g, _selStart, t, DragPart.Start);
        DrawMarker(g, _selEnd, t, DragPart.End);

        // 手柄
        int hr = (_dragPart == DragPart.Value || _hover) ? 8 : 6;
        int cy = t.Top + t.Height / 2;
        var handle = new Rectangle(vx - hr, cy - hr, hr * 2, hr * 2);
        using (var hb = new SolidBrush(Theme.Card)) g.FillEllipse(hb, handle);
        using (var hp = new Pen(Theme.Accent, 2)) g.DrawEllipse(hp, handle);

        // 拖动气泡
        if (_dragPart == DragPart.Value)
        {
            string txt = FmtMs(_value);
            var size = TextRenderer.MeasureText(txt, Theme.FontUI, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding);
            int bw = size.Width + 16;
            int bh = size.Height + 8;
            int bx = Math.Clamp(vx - bw / 2, 0, Width - bw);
            int by = t.Top - bh - 20; // 留出标记把手的空间，避免气泡压住把手
            var bubble = new Rectangle(bx, by, bw, bh);
            using var bp = ModernButton.RoundRect(bubble, 6);
            using var bb = new SolidBrush(Theme.TextPrimary);
            g.FillPath(bb, bp);
            TextRenderer.DrawText(g, txt, Theme.FontUI,
                new Point(bubble.X + 8, bubble.Y + 4), Theme.Window,
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        }
    }

    private void DrawMarker(Graphics g, int? v, Rectangle t, DragPart part)
    {
        if (!v.HasValue) return;
        int x = ValueToX(v.Value);
        bool active = _dragPart == part || (_dragPart == DragPart.None && _hoverPart == part);

        using var pen = new Pen(Theme.AccentPressed, active ? 3f : 2f);
        g.DrawLine(pen, x, t.Top - 2, x, t.Bottom + 2);

        // 轨道上方的可拖拽小把手（提示用户可以拖动）
        int hw = active ? 5 : 4;
        int hh = active ? 7 : 6;
        var grip = new Rectangle(x - hw, t.Top - hh * 2 - 2, hw * 2, hh * 2);
        using var path = ModernButton.RoundRect(grip, 3);
        using var b = new SolidBrush(Theme.AccentPressed);
        g.FillPath(b, path);
        using var border = new Pen(Theme.Card, 1);
        g.DrawPath(border, path);
    }

    private static string FmtMs(int ms)
    {
        var ts = TimeSpan.FromMilliseconds(Math.Max(0, ms));
        return ts.TotalHours >= 1 ? ts.ToString(@"hh\:mm\:ss\.fff") : ts.ToString(@"mm\:ss\.fff");
    }

    // 命中检测：x 附近是否有标记把手（就近优先）
    private DragPart HitTestMarker(int x)
    {
        var part = DragPart.None;
        int best = GrabPx + 1;
        if (_selStart.HasValue)
        {
            int d = Math.Abs(x - ValueToX(_selStart.Value));
            if (d <= GrabPx && d < best) { best = d; part = DragPart.Start; }
        }
        if (_selEnd.HasValue)
        {
            int d = Math.Abs(x - ValueToX(_selEnd.Value));
            if (d <= GrabPx && d < best) { best = d; part = DragPart.End; }
        }
        return part;
    }

    private void MoveMarkerTo(DragPart part, int v)
    {
        if (part == DragPart.Start)
        {
            int upper = (_selEnd ?? _max) - 1;
            SelectionStart = Math.Clamp(v, _min, Math.Max(_min, upper));
        }
        else if (part == DragPart.End)
        {
            int lower = (_selStart ?? _min) + 1;
            SelectionEnd = Math.Clamp(v, Math.Min(_max, lower), _max);
        }
    }

    // 方向键交给表单做标记微调（KeyPreview → MainForm_KeyDown），不做焦点导航
    protected override bool IsInputKey(Keys keyData)
    {
        if ((keyData & Keys.KeyCode) is Keys.Left or Keys.Right) return true;
        return base.IsInputKey(keyData);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        Focus();
        _dragPart = HitTestMarker(e.X);
        if (_dragPart != DragPart.None)
        {
            LastDraggedIsEnd = _dragPart == DragPart.End;
            Cursor = Cursors.SizeWE;
            MoveMarkerTo(_dragPart, XToValue(e.X));
            MarkerDragging?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            _dragPart = DragPart.Value;
            Value = XToValue(e.X);
            Scrubbing?.Invoke(this, EventArgs.Empty);
        }
        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_dragPart == DragPart.Value)
        {
            Value = XToValue(e.X);
            Scrubbing?.Invoke(this, EventArgs.Empty);
        }
        else if (_dragPart != DragPart.None)
        {
            Cursor = Cursors.SizeWE;
            MoveMarkerTo(_dragPart, XToValue(e.X));
            MarkerDragging?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            var h = HitTestMarker(e.X);
            if (h != _hoverPart) { _hoverPart = h; Invalidate(); }
            Cursor = h != DragPart.None ? Cursors.SizeWE : Cursors.Hand;
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (_dragPart == DragPart.None) return;
        var part = _dragPart;
        _dragPart = DragPart.None;
        Cursor = Cursors.Hand;
        if (part == DragPart.Value) ScrubEnded?.Invoke(this, EventArgs.Empty);
        else MarkerDragEnded?.Invoke(this, EventArgs.Empty);
        Invalidate();
        base.OnMouseUp(e);
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
}

/// <summary>音轨列表里的一行：整行点击=选中该音轨，右侧胶囊=单独预览该音轨。</summary>
public class TrackRow : Control
{
    private static readonly Font TitleFont = new("Microsoft YaHei UI", 9.5F, FontStyle.Bold);
    private static readonly Font DetailFont = new("Microsoft YaHei UI", 8.25F, FontStyle.Regular);
    private static readonly Font PillFont = new("Microsoft YaHei UI", 8.25F, FontStyle.Regular);

    private bool _hover, _previewHover, _previewPressed;

    public int Index { get; init; }
    public string Title { get; set; } = "";
    public string Detail { get; set; } = "";

    private bool _selected;
    public bool IsSelected
    {
        get => _selected;
        set { if (_selected != value) { _selected = value; Invalidate(); } }
    }

    public event EventHandler? SelectRequested;
    public event EventHandler? PreviewRequested;

    public TrackRow()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                 ControlStyles.SupportsTransparentBackColor | ControlStyles.Selectable, true);
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
        Font = Theme.FontUI;
    }

    private float K => Height / 46f;

    private Rectangle PreviewRect
    {
        get
        {
            float k = K;
            int w = (int)(78 * k), h = (int)(28 * k);
            return new Rectangle(Width - (int)(12 * k) - w, (Height - h) / 2, w, h);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaintBackground(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        float k = K;
        var r = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
        using var path = ModernButton.RoundRect(r, (int)(9 * k));

        Color fill = _selected
            ? (Theme.IsDark ? Color.FromArgb(38, 44, 58) : Color.FromArgb(238, 244, 255))
            : _hover ? Theme.CardAlt : Color.Transparent;
        if (fill.A > 0)
        {
            using var b = new SolidBrush(fill);
            g.FillPath(b, path);
        }
        if (_selected)
        {
            var bar = new Rectangle((int)(2 * k), (int)(7 * k), Math.Max(2, (int)(3 * k)), Height - (int)(14 * k));
            using var bb = new SolidBrush(Theme.Accent);
            using var bp = ModernButton.RoundRect(bar, Math.Max(1, (int)(2 * k)));
            g.FillPath(bb, bp);
            using var pen = new Pen(Color.FromArgb(110, Theme.Accent), 1);
            g.DrawPath(pen, path);
        }

        int padLeft = (int)(18 * k);
        int textW = Math.Max(10, PreviewRect.Left - padLeft - (int)(10 * k));
        int titleY = Height / 2 - (int)(16 * k);
        var flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;
        TextRenderer.DrawText(g, Title, TitleFont, new Rectangle(padLeft, titleY, textW, (int)(18 * k)),
            Theme.TextPrimary, flags);
        TextRenderer.DrawText(g, Detail, DetailFont,
            new Rectangle(padLeft, titleY + (int)(19 * k), textW, (int)(16 * k)), Theme.TextMuted, flags);

        var pr = PreviewRect;
        Color pbg = _previewPressed ? Theme.CardAlt
            : _previewHover ? (Theme.IsDark ? Color.FromArgb(50, 50, 60) : Color.FromArgb(228, 233, 242))
            : Color.Transparent;
        using (var pp = ModernButton.RoundRect(pr, pr.Height / 2))
        {
            if (pbg.A > 0)
            {
                using var pb = new SolidBrush(pbg);
                g.FillPath(pb, pp);
            }
            using var pen = new Pen(Theme.Border, 1);
            g.DrawPath(pen, pp);
        }
        TextRenderer.DrawText(g, "预览", PillFont, pr,
            _previewHover ? Theme.TextPrimary : Theme.TextSecondary,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
            TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        bool overPreview = PreviewRect.Contains(e.Location);
        if (overPreview != _previewHover)
        {
            _previewHover = overPreview;
            Invalidate();
        }
        Cursor = Cursors.Hand;
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = false;
        _previewHover = false;
        _previewPressed = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && PreviewRect.Contains(e.Location))
        {
            _previewPressed = true;
            Invalidate();
        }
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            if (_previewPressed)
            {
                _previewPressed = false;
                Invalidate();
                if (PreviewRect.Contains(e.Location)) PreviewRequested?.Invoke(this, EventArgs.Empty);
            }
            else if (ClientRectangle.Contains(e.Location))
            {
                SelectRequested?.Invoke(this, EventArgs.Empty);
            }
        }
        base.OnMouseUp(e);
    }
}

/// <summary>
/// 纵向滚动容器：隐藏系统滚动条，改为右侧自绘的现代圆角滚动条（支持滚轮、拖动滑块、点轨道翻页）。
/// </summary>
public class ModernScrollPanel : Panel
{
    private readonly List<Control> _items = new();
    private int _offset;
    private int _rowHeight = 46;
    private int _gap = 6;
    private bool _thumbHover, _thumbDragging;
    private int _dragGrab;

    private const int BarMargin = 3;
    private const int Gutter = 14;

    // 悬停/拖动时滑块略微变宽，配合低对比轨道形成现代滚动条观感
    private int BarWidth => _thumbHover || _thumbDragging ? 8 : 5;

    public int RowHeight
    {
        get => _rowHeight;
        set { _rowHeight = Math.Max(1, value); Relayout(); }
    }

    public int Gap
    {
        get => _gap;
        set { _gap = Math.Max(0, value); Relayout(); }
    }

    public bool CanScroll => MaxOffset > 0;

    public ModernScrollPanel()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Color.Transparent;
    }

    private int ContentHeight => _items.Count == 0 ? 0 : _items.Count * (_rowHeight + _gap) - _gap;
    private int MaxOffset => Math.Max(0, ContentHeight - ClientSize.Height);

    public void ClearRows()
    {
        foreach (var c in _items)
        {
            Controls.Remove(c);
            c.Dispose();
        }
        _items.Clear();
        _offset = 0;
        Invalidate();
    }

    public void AddRow(Control row)
    {
        _items.Add(row);
        Controls.Add(row);
        Relayout();
    }

    public void ScrollBy(int delta)
    {
        if (MaxOffset <= 0) return;
        ScrollTo(_offset + delta);
    }

    public void ScrollTo(int offset)
    {
        offset = Math.Clamp(offset, 0, MaxOffset);
        if (offset == _offset) return;
        _offset = offset;
        Relayout();
        Invalidate();
    }

    /// <summary>滚动到列表末尾（新增行后自动露出最新内容）。</summary>
    public void ScrollToEnd() => ScrollTo(MaxOffset);

    private void Relayout()
    {
        int y = 0;
        foreach (var c in _items)
        {
            c.SetBounds(0, y - _offset, Math.Max(0, ClientSize.Width - Gutter), _rowHeight);
            y += _rowHeight + _gap;
        }
        _offset = Math.Clamp(_offset, 0, MaxOffset);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        Relayout();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaintBackground(e);
        if (MaxOffset <= 0 && !_thumbHover) return;

        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var track = TrackRect;
        using (var tp = ModernButton.RoundRect(track, track.Width / 2))
        using (var tb = new SolidBrush(Theme.IsDark ? Color.FromArgb(28, 30, 36) : Color.FromArgb(233, 236, 242)))
            g.FillPath(tb, tp);

        var thumb = ThumbRect(track);
        using var thp = ModernButton.RoundRect(thumb, thumb.Width / 2);
        using var thb = new SolidBrush(
            _thumbDragging ? Theme.AccentHover
            : _thumbHover ? Color.FromArgb(190, Theme.Accent)
            : Theme.IsDark ? Color.FromArgb(78, 84, 98) : Color.FromArgb(186, 192, 206));
        g.FillPath(thb, thp);
    }

    private Rectangle TrackRect
    {
        get
        {
            int x = Math.Max(0, ClientSize.Width - BarWidth - BarMargin);
            return new Rectangle(x, BarMargin, BarWidth, Math.Max(10, ClientSize.Height - BarMargin * 2));
        }
    }

    private Rectangle ThumbRect(Rectangle track)
    {
        int content = Math.Max(1, ContentHeight);
        int view = Math.Max(1, ClientSize.Height);
        int th = view >= content ? track.Height : Math.Max(28, (int)((long)track.Height * view / content));
        int range = track.Height - th;
        int y = track.Top + (MaxOffset <= 0 ? 0 : (int)((long)_offset * range / MaxOffset));
        return new Rectangle(track.X, y, track.Width, th);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && MaxOffset > 0)
        {
            var track = TrackRect;
            var thumb = ThumbRect(track);
            if (thumb.Contains(e.Location))
            {
                _thumbDragging = true;
                _dragGrab = e.Y - thumb.Top;
                Capture = true;
            }
            else if (track.Contains(e.Location))
            {
                ScrollTo(e.Y < thumb.Top ? _offset - ClientSize.Height : _offset + ClientSize.Height);
            }
        }
        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_thumbDragging && MaxOffset > 0)
        {
            var track = TrackRect;
            var thumb = ThumbRect(track);
            int range = track.Height - thumb.Height;
            if (range > 0)
            {
                double ratio = (double)(e.Y - _dragGrab - track.Top) / range;
                ScrollTo((int)Math.Round(Math.Clamp(ratio, 0, 1) * MaxOffset));
            }
        }
        else
        {
            bool over = MaxOffset > 0 && ThumbRect(TrackRect).Contains(e.Location);
            if (over != _thumbHover)
            {
                _thumbHover = over;
                Invalidate();
            }
            Cursor = over ? Cursors.Hand : Cursors.Default;
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _thumbDragging = false;
        Capture = false;
        Invalidate();
        base.OnMouseUp(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        if (!_thumbDragging)
        {
            _thumbHover = false;
            Invalidate();
        }
        base.OnMouseLeave(e);
    }
}

/// <summary>
/// 现代化下拉菜单：无边框圆角卡片 + 系统投影，图标与文字共用同一行高并垂直居中；
/// 悬停时整行显示圆角高亮并把图标点亮为主题色，配色与字体完全跟随 Theme。
/// </summary>
public sealed class ModernMenu : Form
{
    private sealed class Item
    {
        public required string Text { get; init; }
        public required string Glyph { get; init; }
        public required Action Click { get; init; }
        public Rectangle Rect;
    }

    private readonly List<Item> _items = new();
    private int _hover = -1;
    private int _active = -1;        // 键盘高亮行
    private float _scale = 1f;
    private long _lastHideTick = -1000;

    public ModernMenu()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        DoubleBuffered = true;
        KeyPreview = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }

    public void Add(string text, string glyph, Action click)
        => _items.Add(new Item { Text = text, Glyph = glyph, Click = click });

    // 系统投影 + 工具窗口（不进入 Alt-Tab / 任务栏）
    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ClassStyle |= 0x00020000;
            cp.ExStyle |= 0x00000080;
            return cp;
        }
    }

    /// <summary>在指定控件下方弹出菜单；再次点击同一控件则收起。</summary>
    public void ShowAt(Control anchor)
    {
        if (Visible) { HideMenu(); return; }
        // 刚因“点击外部”收起时，同一次点击不应立刻再次弹出
        if (Environment.TickCount64 - _lastHideTick < 200) return;

        _scale = Math.Max(1f, anchor.DeviceDpi / 96f);
        int pad = Scale(6), rowH = Scale(36);
        int iconSize = Scale(18), gap = Scale(10), lead = Scale(10);

        int textW = 0;
        foreach (var it in _items)
            textW = Math.Max(textW, TextRenderer.MeasureText(it.Text, Theme.FontUI, Size.Empty,
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width);

        int width = pad * 2 + lead + iconSize + gap + textW + Scale(12);
        int height = pad * 2 + rowH * _items.Count;
        ClientSize = new Size(width, height);
        BackColor = Theme.Window;   // 与主窗口背景统一

        for (int i = 0; i < _items.Count; i++)
            _items[i].Rect = new Rectangle(pad, pad + rowH * i, width - pad * 2, rowH);

        UpdateRegion();

        var below = anchor.PointToScreen(new Point(0, anchor.Height + Scale(6)));
        var wa = Screen.FromControl(anchor).WorkingArea;
        int x = Math.Min(below.X, wa.Right - Width - Scale(4));
        int y = below.Y;
        if (y + Height > wa.Bottom - Scale(4))
            y = anchor.PointToScreen(Point.Empty).Y - Height - Scale(6);   // 下方空间不足时翻到上方

        Location = new Point(Math.Max(wa.Left + Scale(4), x), Math.Max(wa.Top + Scale(4), y));
        _hover = _active = -1;
        Show(anchor.FindForm());
        Activate();
        Invalidate();
    }

    private void HideMenu()
    {
        _lastHideTick = Environment.TickCount64;
        Hide();
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        if (Visible) HideMenu();
    }

    private int Scale(int v) => (int)Math.Round(v * _scale);

    private void UpdateRegion()
    {
        if (Width <= 0 || Height <= 0) return;
        using var path = RoundedPath(new RectangleF(0, 0, Width, Height), Scale(10));
        var old = Region;
        Region = new Region(path);
        old?.Dispose();
    }

    internal static GraphicsPath RoundedPath(RectangleF r, float radius)
    {
        var p = new GraphicsPath();
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var border = new RectangleF(0.5f * _scale, 0.5f * _scale,
            Width - 1f * _scale, Height - 1f * _scale);
        using (var bgPath = RoundedPath(border, Scale(10)))
        {
            using var bg = new SolidBrush(Theme.Window);
            g.FillPath(bg, bgPath);
            using var pen = new Pen(Theme.Border, 1f * _scale);
            g.DrawPath(pen, bgPath);
        }

        int pad = Scale(6);
        int iconX = pad + Scale(10);
        int iconSize = Scale(18);
        int textX = iconX + iconSize + Scale(10);
        using var iconFont = new Font("Segoe MDL2 Assets", Scale(14), FontStyle.Regular, GraphicsUnit.Pixel);

        for (int i = 0; i < _items.Count; i++)
        {
            var it = _items[i];
            bool hot = i == _hover || i == _active;

            if (hot)
            {
                var hi = new Rectangle(it.Rect.X + Scale(4), it.Rect.Y + Scale(3),
                    it.Rect.Width - Scale(8), it.Rect.Height - Scale(6));
                using var hp = RoundedPath(hi, Scale(7));
                using var hb = new SolidBrush(Theme.AccentSoft);
                g.FillPath(hb, hp);
            }

            // 图标与文字都按整行垂直居中，保证两者在同一水平中线上
            var iconRect = new Rectangle(iconX, it.Rect.Y, iconSize, it.Rect.Height);
            TextRenderer.DrawText(g, it.Glyph, iconFont, iconRect,
                hot ? Theme.Accent : Theme.TextSecondary,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);

            var textRect = new Rectangle(textX, it.Rect.Y,
                it.Rect.Right - textX - Scale(10), it.Rect.Height);
            TextRenderer.DrawText(g, it.Text, Theme.FontUI, textRect, Theme.TextPrimary,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        }
    }

    private int HitTest(Point p)
    {
        for (int i = 0; i < _items.Count; i++)
            if (_items[i].Rect.Contains(p)) return i;
        return -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int idx = HitTest(e.Location);
        if (idx != _hover)
        {
            _hover = idx;
            _active = -1;
            Invalidate();
        }
        Cursor = idx >= 0 ? Cursors.Hand : Cursors.Default;
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        if (_hover != -1) { _hover = -1; Invalidate(); }
        base.OnMouseLeave(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            int idx = HitTest(e.Location);
            if (idx >= 0)
            {
                var click = _items[idx].Click;
                HideMenu();
                click();
                return;
            }
        }
        base.OnMouseUp(e);
    }

    protected override bool ProcessDialogKey(Keys keyData)
    {
        if (keyData == Keys.Escape) { HideMenu(); return true; }
        if (_items.Count == 0) return base.ProcessDialogKey(keyData);
        if (keyData == Keys.Down)
        {
            _active = (_active + 1) % _items.Count;
            Invalidate();
            return true;
        }
        if (keyData == Keys.Up)
        {
            _active = _active <= 0 ? _items.Count - 1 : _active - 1;
            Invalidate();
            return true;
        }
        if (keyData == Keys.Enter && _active >= 0)
        {
            var click = _items[_active].Click;
            HideMenu();
            click();
            return true;
        }
        return base.ProcessDialogKey(keyData);
    }
}