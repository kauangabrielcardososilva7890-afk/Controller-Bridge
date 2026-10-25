using System.Drawing.Drawing2D;
namespace ControllerBridge.Host;
internal sealed class ControllerDiagram : Control
{
    private const float ViewWidth = 500;
    private const float ViewHeight = 270;
    private const float ViewTop = 70;
    private ControllerSnapshot? _snapshot;
    private string? _hovered;
    public string? CaptureButton { get; set; }
    public event Action<string>? LogicalButtonClicked;
    private readonly Dictionary<string, RectangleF> _targets = new(StringComparer.Ordinal);

    public ControllerDiagram()
    {
        DoubleBuffered = true; BackColor = Color.FromArgb(23, 30, 40); Cursor = Cursors.Default;
        ResizeRedraw = true;
        TabStop = false; // Every mapping is also a keyboard-accessible button beside the diagram.
    }

    public void SetController(ControllerSnapshot? snapshot) { _snapshot = snapshot; Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; _targets.Clear();
        var scale = Math.Min(ClientSize.Width / ViewWidth, ClientSize.Height / ViewHeight);
        if (scale <= 0) return;
        var ox = (ClientSize.Width - ViewWidth * scale) / 2f; var oy = (ClientSize.Height - ViewHeight * scale) / 2f;
        g.TranslateTransform(ox, oy - ViewTop * scale); g.ScaleTransform(scale, scale);
        var ds4 = _snapshot?.Output == "ds4";
        using var body = new SolidBrush(BackColor);
        using var edge = new Pen(Color.FromArgb(118, 157, 191), 2f);
        using var path = new GraphicsPath();
        if (ds4)
        {
            path.AddBezier(85, 107, 45, 108, 48, 205, 25, 276); path.AddBezier(25, 276, 17, 309, 54, 328, 77, 306);
            path.AddBezier(77, 306, 118, 268, 143, 253, 180, 260); path.AddBezier(180, 260, 221, 270, 279, 270, 320, 260);
            path.AddBezier(320, 260, 357, 253, 382, 268, 423, 306); path.AddBezier(423, 306, 446, 328, 483, 309, 475, 276);
            path.AddBezier(475, 276, 452, 205, 455, 108, 415, 107); path.AddBezier(415, 107, 371, 104, 330, 134, 250, 134);
            path.AddBezier(250, 134, 170, 134, 129, 104, 85, 107);
        }
        else
        {
            path.AddBezier(105, 109, 54, 111, 32, 197, 28, 280); path.AddBezier(28, 280, 24, 324, 65, 331, 90, 299);
            path.AddBezier(90, 299, 125, 253, 148, 247, 195, 260); path.AddBezier(195, 260, 230, 268, 270, 268, 305, 260);
            path.AddBezier(305, 260, 352, 247, 375, 253, 410, 299); path.AddBezier(410, 299, 435, 331, 476, 324, 472, 280);
            path.AddBezier(472, 280, 468, 197, 446, 111, 395, 109); path.AddBezier(395, 109, 340, 120, 160, 120, 105, 109);
        }
        path.CloseFigure(); g.FillPath(body, path); g.DrawPath(edge, path);
        // A subtle inner seam gives the shell depth without obscuring live inputs.
        using (var seam = new Pen(Color.FromArgb(72, 103, 132), 1.2f))
        {
            g.DrawBezier(seam, 55, 277, 80, 239, 102, 252, 131, 246);
            g.DrawBezier(seam, 445, 277, 420, 239, 398, 252, 369, 246);
        }
        var dpad = ds4 ? new PointF(115, 210) : new PointF(181, 239);
        DrawButton(g, "dpad_up", "▲", new RectangleF(dpad.X - 16, dpad.Y - 43, 32, 30));
        DrawButton(g, "dpad_down", "▼", new RectangleF(dpad.X - 16, dpad.Y + 13, 32, 30));
        DrawButton(g, "dpad_left", "◀", new RectangleF(dpad.X - 44, dpad.Y - 15, 30, 30));
        DrawButton(g, "dpad_right", "▶", new RectangleF(dpad.X + 14, dpad.Y - 15, 30, 30));
        DrawButton(g, "north", ds4 ? "△" : "Y", new RectangleF(358, 151, 34, 34));
        DrawButton(g, "east", ds4 ? "○" : "B", new RectangleF(391, 185, 34, 34));
        DrawButton(g, "south", ds4 ? "×" : "A", new RectangleF(358, 219, 34, 34));
        DrawButton(g, "west", ds4 ? "□" : "X", new RectangleF(325, 185, 34, 34));
        DrawButton(g, "l1", ds4 ? "L1" : "LB", new RectangleF(78, 109, 78, 26)); DrawButton(g, "r1", ds4 ? "R1" : "RB", new RectangleF(344, 109, 78, 26));
        DrawButton(g, "l2", ds4 ? "L2" : "LT", new RectangleF(78, 77, 78, 25)); DrawButton(g, "r2", ds4 ? "R2" : "RT", new RectangleF(344, 77, 78, 25));
        if (ds4)
        {
            using var touch = new SolidBrush(Color.FromArgb(27, 33, 42));
            using var touchEdge = new Pen(Color.FromArgb(82, 96, 114));
            g.FillRoundedRectangle(touch, new RectangleF(193, 143, 114, 55), 8);
            g.DrawRoundedRectangle(touchEdge, new RectangleF(193, 143, 114, 55), 8);
            DrawButton(g, "share", "SH", new RectangleF(158, 151, 32, 23)); DrawButton(g, "options", "OPT", new RectangleF(310, 151, 36, 23));
            DrawStick(g, "l3", new PointF(197, 241), "L3"); DrawStick(g, "r3", new PointF(295, 241), "R3");
            DrawButton(g, "ps", "PS", new RectangleF(232, 208, 32, 27));
        }
        else
        {
            DrawButton(g, "share", "BACK", new RectangleF(177, 158, 40, 24)); DrawButton(g, "options", "START", new RectangleF(283, 158, 40, 24));
            DrawStick(g, "l3", new PointF(114, 178), "LS"); DrawStick(g, "r3", new PointF(291, 239), "RS");
            DrawButton(g, "ps", "X", new RectangleF(232, 147, 36, 36));
        }
        g.ResetTransform();
    }

    private void DrawStick(Graphics g, string logical, PointF center, string label)
    {
        var pressed = _snapshot?.State.Button(logical) ?? false;
        var capturing = CaptureButton == logical;
        var fillColor = capturing ? Color.FromArgb(130, 88, 30) : pressed ? Color.FromArgb(74, 164, 255) : Color.FromArgb(24, 34, 46);
        using var brush = new SolidBrush(fillColor); using var pen = new Pen(capturing ? Color.FromArgb(249, 190, 74) : _hovered == logical ? Color.FromArgb(164, 211, 248) : Color.FromArgb(94, 111, 132), 2);
        var ring = new RectangleF(center.X - 29, center.Y - 29, 58, 58);
        g.DrawEllipse(pen, ring);
        var axis = logical == "l3" ? "l" : "r";
        var dx = (_snapshot?.State.Axis(axis + "x") ?? 0) * 10;
        var dy = (_snapshot?.State.Axis(axis + "y") ?? 0) * 10;
        var rect = new RectangleF(center.X - 23 + dx, center.Y - 23 + dy, 46, 46); g.FillEllipse(brush, rect); g.DrawEllipse(pen, rect);
        var hit = new RectangleF(center.X - 31, center.Y - 31, 62, 62); _targets[logical] = hit;
        using var font = new Font("Segoe UI", 8, FontStyle.Bold); using var text = new SolidBrush(Color.FromArgb(223, 232, 243));
        using var format = new StringFormat { Alignment = StringAlignment.Center };
        g.DrawString(label, font, text, new RectangleF(rect.X, rect.Y + 14, rect.Width, 18), format);
    }

    private void DrawButton(Graphics g, string logical, string text, RectangleF rect)
    {
        var pressed = (_snapshot?.State.Button(logical) ?? false) || (logical == "l2" && _snapshot?.State.Axis("lt") > 0.1f) || (logical == "r2" && _snapshot?.State.Axis("rt") > 0.1f);
        var active = Color.FromArgb(70, 164, 255); var idle = Color.FromArgb(24, 34, 46);
        var capturing = CaptureButton == logical;
        using var brush = new SolidBrush(capturing ? Color.FromArgb(130, 88, 30) : pressed ? active : idle);
        using var pen = new Pen(capturing ? Color.FromArgb(249, 190, 74) : pressed || _hovered == logical ? Color.FromArgb(164, 211, 248) : Color.FromArgb(94, 111, 132), 1.5f);
        g.FillRoundedRectangle(brush, rect, 8); g.DrawRoundedRectangle(pen, rect, 8);
        if (logical is "l2" or "r2")
        {
            var amount = Math.Clamp(_snapshot?.State.Axis(logical == "l2" ? "lt" : "rt") ?? 0, 0, 1);
            using var meter = new SolidBrush(Color.FromArgb(160, 220, 255));
            g.FillRectangle(meter, rect.X + 5, rect.Bottom - 4, (rect.Width - 10) * amount, 2);
        }
        _targets[logical] = RectangleF.Inflate(rect, 4, 4);
        var inkColor = !pressed && !capturing ? logical switch
        {
            "south" => Color.FromArgb(111, 220, 167), "east" => Color.FromArgb(255, 145, 153),
            "west" => Color.FromArgb(133, 191, 255), "north" => Color.FromArgb(247, 210, 115),
            _ => Color.FromArgb(231, 238, 247)
        } : Color.White;
        using var font = new Font("Segoe UI", logical is "share" or "options" ? 7 : 11, FontStyle.Bold); using var ink = new SolidBrush(inkColor);
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap };
        g.DrawString(text, font, ink, rect, format);
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (e.Button != MouseButtons.Left || _snapshot == null) return;
        var logical = HitTest(e.Location);
        if (logical != null) LogicalButtonClicked?.Invoke(logical);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var hovered = _snapshot == null ? null : HitTest(e.Location);
        if (_hovered == hovered) return;
        _hovered = hovered;
        Cursor = hovered == null ? Cursors.Default : Cursors.Hand;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hovered = null; Cursor = Cursors.Default; Invalidate();
    }

    private string? HitTest(Point point)
    {
        var scale = Math.Min(ClientSize.Width / ViewWidth, ClientSize.Height / ViewHeight);
        if (scale <= 0) return null;
        var x = (point.X - (ClientSize.Width - ViewWidth * scale) / 2f) / scale;
        var y = (point.Y - (ClientSize.Height - ViewHeight * scale) / 2f) / scale + ViewTop;
        foreach (var pair in _targets) if (pair.Value.Contains(x, y)) return pair.Key;
        return null;
    }
}

internal static class GraphicsRoundedExtensions
{
    public static void FillRoundedRectangle(this Graphics graphics, Brush brush, RectangleF rect, float radius)
    {
        using var path = Rounded(rect, radius); graphics.FillPath(brush, path);
    }
    public static void DrawRoundedRectangle(this Graphics graphics, Pen pen, RectangleF rect, float radius)
    {
        using var path = Rounded(rect, radius); graphics.DrawPath(pen, path);
    }
    private static GraphicsPath Rounded(RectangleF r, float radius)
    {
        var d = radius * 2; var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90); path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); path.AddArc(r.X, r.Bottom - d, d, d, 90, 90); path.CloseFigure();
        return path;
    }
}


