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
        Height = 48;
        Cursor = Cursors.Hand;
    }

    private Rectangle TrackRect => new Rectangle(12, Height / 2 - 4, Width - 24, 8);

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
        var handle = new Rectangle(vx - hr, Height / 2 - hr, hr * 2, hr * 2);
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
            int by = t.Top - bh - 6;
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