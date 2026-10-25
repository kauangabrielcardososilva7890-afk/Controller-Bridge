using System.Drawing.Drawing2D;

namespace ControllerBridge.Host;

internal sealed class ControllerDiagram : Control
{
    // A shared coordinate space keeps both rendering and remapping hit targets aligned at any DPI.
    private RectangleF Viewport => _snapshot?.Output == "ds4" ? new(0, 0, 640, 390) : new(20, 0, 600, 430);
    private static readonly Color Outline = Color.FromArgb(166, 175, 178);
    private static readonly Color Muted = Color.FromArgb(87, 98, 103);
    private static readonly Color Cyan = Color.FromArgb(55, 224, 255);
    private ControllerSnapshot? _snapshot;
    private string? _hovered;
    private string? _captureButton;
    private readonly Dictionary<string, RectangleF> _targets = new(StringComparer.Ordinal);

    public string? CaptureButton
    {
        get => _captureButton;
        set { if (_captureButton == value) return; _captureButton = value; Invalidate(); }
    }
    public event Action<string>? LogicalButtonClicked;

    public ControllerDiagram()
    {
        DoubleBuffered = true;
        BackColor = Color.FromArgb(25, 30, 32);
        ResizeRedraw = true;
        TabStop = false; // The adjacent mapping buttons also support keyboard navigation.
    }

    public void SetController(ControllerSnapshot? snapshot) { _snapshot = snapshot; Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        _targets.Clear();
        var view = Viewport;
        var scale = Math.Min(ClientSize.Width / view.Width, ClientSize.Height / view.Height);
        if (scale <= 0) return;
        var g = e.Graphics;
        var saved = g.Save();
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TranslateTransform((ClientSize.Width - view.Width * scale) / 2f - view.X * scale, (ClientSize.Height - view.Height * scale) / 2f);
        g.ScaleTransform(scale, scale);
        if (_snapshot?.Output == "ds4") DrawDualShock(g); else DrawXbox(g);
        g.Restore(saved);
    }

    private void DrawDualShock(Graphics g)
    {
        DrawShoulder(g, "l2", "L2", 108, 24, false, true);
        DrawShoulder(g, "r2", "R2", 458, 24, false, true);
        using var shell = new GraphicsPath();
        shell.AddBezier(110, 70, 88, 83, 40, 202, 23, 291);
        shell.AddBezier(23, 291, 11, 338, 44, 367, 82, 367);
        shell.AddBezier(82, 367, 127, 367, 140, 324, 159, 301);
        shell.AddBezier(159, 301, 167, 290, 181, 294, 198, 295);
        shell.AddLine(198, 295, 442, 295);
        shell.AddBezier(442, 295, 459, 294, 473, 290, 481, 301);
        shell.AddBezier(481, 301, 500, 324, 513, 367, 558, 367);
        shell.AddBezier(558, 367, 596, 367, 629, 338, 617, 291);
        shell.AddBezier(617, 291, 600, 202, 552, 83, 530, 70);
        shell.AddBezier(530, 70, 506, 58, 479, 67, 461, 76);
        shell.AddBezier(461, 76, 442, 87, 428, 87, 411, 85);
        shell.AddBezier(411, 85, 352, 80, 288, 80, 229, 85);
        shell.AddBezier(229, 85, 212, 87, 198, 87, 179, 76);
        shell.AddBezier(179, 76, 161, 67, 134, 58, 110, 70);
        shell.CloseFigure();
        DrawShell(g, shell);
        using (var seam = new Pen(Muted, 1.2f))
        {
            g.DrawBezier(seam, 23, 291, 35, 336, 63, 359, 104, 350);
            g.DrawBezier(seam, 104, 350, 134, 343, 146, 315, 159, 301);
            g.DrawBezier(seam, 617, 291, 605, 336, 577, 359, 536, 350);
            g.DrawBezier(seam, 536, 350, 506, 343, 494, 315, 481, 301);
            g.DrawEllipse(seam, 66, 126, 138, 126);
            g.DrawEllipse(seam, 436, 126, 138, 126);
        }
        DrawShoulder(g, "l1", "L1", 101, 61, false, false);
        DrawShoulder(g, "r1", "R1", 451, 61, false, false);
        using (var barGlow = new Pen(Color.FromArgb(30, 42, 112, 255), 10))
            g.DrawLine(barGlow, 239, 93, 401, 93);
        using (var light = new Pen(Color.FromArgb(40, 109, 255), 4) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            g.DrawLine(light, 235, 93, 405, 93);
        using (var touch = Rounded(new RectangleF(226, 103, 188, 108), 9))
        {
            using var fill = new SolidBrush(Color.FromArgb(24, 29, 31));
            using var edge = new Pen(Outline, 1.35f);
            g.FillPath(fill, touch); g.DrawPath(edge, touch);
            using var texture = new SolidBrush(Color.FromArgb(70, 83, 89));
            for (var y = 137; y < 203; y += 6)
                for (var x = 234; x < 408; x += 6)
                    g.FillEllipse(texture, x, y, 1.5f, 1.5f);
        }
        DrawCenterButton(g, "share", new RectangleF(198, 133, 14, 26), true);
        DrawCenterButton(g, "options", new RectangleF(428, 133, 14, 26), true);
        DrawLabel(g, "SHARE", new RectangleF(184, 115, 42, 14), 7.5f, Muted);
        DrawLabel(g, "OPTIONS", new RectangleF(416, 115, 42, 14), 7.5f, Muted);
        DrawDpad(g, new PointF(135, 187), true);
        DrawFaceButtons(g, new PointF(505, 187), true);
        DrawStick(g, "l3", new PointF(225, 271), 46);
        DrawStick(g, "r3", new PointF(415, 271), 46);
        DrawHome(g, new PointF(320, 263), true);
    }

    private void DrawXbox(Graphics g)
    {
        DrawShoulder(g, "l2", "LT", 140, 26, true, true);
        DrawShoulder(g, "r2", "RT", 420, 26, true, true);
        DrawShoulder(g, "l1", "LB", 125, 59, true, false);
        DrawShoulder(g, "r1", "RB", 427, 59, true, false);
        using var shell = new GraphicsPath();
        shell.AddBezier(117, 90, 94, 133, 58, 260, 53, 350);
        shell.AddBezier(53, 350, 47, 393, 64, 416, 92, 413);
        shell.AddBezier(92, 413, 122, 410, 145, 365, 177, 337);
        shell.AddBezier(177, 337, 201, 315, 217, 313, 254, 313);
        shell.AddLine(254, 313, 386, 313);
        shell.AddBezier(386, 313, 423, 313, 439, 315, 463, 337);
        shell.AddBezier(463, 337, 495, 365, 518, 410, 548, 413);
        shell.AddBezier(548, 413, 576, 416, 593, 393, 587, 350);
        shell.AddBezier(587, 350, 582, 260, 546, 133, 523, 90);
        shell.AddBezier(523, 90, 501, 78, 478, 70, 456, 71);
        shell.AddBezier(456, 71, 435, 71, 421, 102, 393, 104);
        shell.AddBezier(393, 104, 352, 107, 288, 107, 247, 104);
        shell.AddBezier(247, 104, 219, 102, 205, 71, 184, 71);
        shell.AddBezier(184, 71, 162, 70, 139, 78, 117, 90);
        shell.CloseFigure();
        DrawShell(g, shell);
        using (var seam = new Pen(Muted, 1.05f))
        {
            g.DrawBezier(seam, 117, 90, 146, 76, 174, 66, 191, 80);
            g.DrawBezier(seam, 191, 80, 219, 106, 226, 120, 254, 120);
            g.DrawLine(seam, 254, 120, 386, 120);
            g.DrawBezier(seam, 386, 120, 414, 120, 421, 106, 449, 80);
            g.DrawBezier(seam, 449, 80, 466, 66, 494, 76, 523, 90);
            g.DrawBezier(seam, 92, 413, 135, 388, 158, 319, 205, 308);
            g.DrawBezier(seam, 205, 308, 231, 301, 252, 302, 320, 302);
            g.DrawBezier(seam, 320, 302, 388, 302, 409, 301, 435, 308);
            g.DrawBezier(seam, 435, 308, 482, 319, 505, 388, 548, 413);
        }
        DrawStick(g, "l3", new PointF(172, 170), 45);
        DrawStick(g, "r3", new PointF(396, 244), 47);
        DrawDpad(g, new PointF(246, 244), false);
        DrawFaceButtons(g, new PointF(484, 175), false);
        DrawHome(g, new PointF(320, 158), false);
        DrawCenterButton(g, "share", new RectangleF(260, 158, 25, 25), false);
        DrawCenterButton(g, "options", new RectangleF(355, 158, 25, 25), false);
    }

    private static void DrawShell(Graphics g, GraphicsPath path)
    {
        using var body = new LinearGradientBrush(path.GetBounds(), Color.FromArgb(29, 35, 37), Color.FromArgb(23, 28, 30), 90f);
        using var edge = new Pen(Outline, 1.45f) { LineJoin = LineJoin.Round };
        g.FillPath(body, path); g.DrawPath(edge, path);
    }

    private bool IsPressed(string logical) => (_snapshot?.State.Button(logical) ?? false)
        || (logical == "l2" && _snapshot?.State.Axis("lt") > 0.1f)
        || (logical == "r2" && _snapshot?.State.Axis("rt") > 0.1f);

    private Color ControlColor(string logical) => CaptureButton == logical ? Color.FromArgb(255, 201, 91)
        : IsPressed(logical) ? Cyan : _hovered == logical ? Color.FromArgb(224, 237, 240) : Outline;

    private void DrawControl(Graphics g, string logical, GraphicsPath path, float width = 1.3f)
    {
        var active = IsPressed(logical) || CaptureButton == logical;
        var color = ControlColor(logical);
        if (active)
        {
            // Layered translucent strokes produce a soft glow without bitmap assets.
            for (var halo = 12; halo >= 4; halo -= 2)
            {
                using var glow = new Pen(Color.FromArgb(9 + (12 - halo) * 2, color), halo) { LineJoin = LineJoin.Round };
                g.DrawPath(glow, path);
            }
        }
        using var fill = new LinearGradientBrush(path.GetBounds(), active ? Color.FromArgb(35, color) : Color.FromArgb(29, 35, 37), Color.FromArgb(23, 28, 30), 90f);
        using var pen = new Pen(color, active ? 1.8f : width) { LineJoin = LineJoin.Round };
        g.FillPath(fill, path); g.DrawPath(pen, path);
        _targets[logical] = RectangleF.Inflate(path.GetBounds(), 5, 5);
    }

    private void DrawShoulder(Graphics g, string logical, string label, float x, float y, bool xbox, bool trigger)
    {
        using var shape = new GraphicsPath();
        var w = xbox ? 88f : 82f;
        if (trigger)
        {
            shape.AddBezier(x + 5, y + 40, x + 10, y + 3, x + w - 17, y - 6, x + w - 10, y + 5);
            shape.AddBezier(x + w - 10, y + 5, x + w - 4, y + 15, x + w - 2, y + 29, x + w, y + 39);
            shape.AddBezier(x + w, y + 39, x + 57, y + 30, x + 30, y + 30, x + 5, y + 40);
        }
        else
        {
            shape.AddBezier(x, y + 10, x + 13, y - 2, x + w - 11, y - 2, x + w, y + 9);
            shape.AddBezier(x + w, y + 9, x + w + 4, y + 20, x + w + 1, y + 32, x + w - 13, y + 33);
            shape.AddBezier(x + w - 13, y + 33, x + 40, y + 38, x + 2, y + 36, x, y + 25);
            shape.AddLine(x, y + 25, x, y + 10);
        }
        shape.CloseFigure(); DrawControl(g, logical, shape);
        DrawLabel(g, label, new RectangleF(x, y + 8, w, 20), 9, ControlColor(logical));
    }

    private void DrawStick(Graphics g, string logical, PointF center, float radius)
    {
        using var housing = new SolidBrush(Color.FromArgb(25, 30, 32));
        g.FillEllipse(housing, Circle(center, radius));
        using var ring = new Pen(Muted, 1.1f);
        g.DrawEllipse(ring, Circle(center, radius));
        g.DrawEllipse(ring, Circle(center, radius - 7));
        var axis = logical == "l3" ? "l" : "r";
        var cap = new PointF(center.X + Math.Clamp(_snapshot?.State.Axis(axis + "x") ?? 0, -1, 1) * 9,
            center.Y + Math.Clamp(_snapshot?.State.Axis(axis + "y") ?? 0, -1, 1) * 9);
        using var outline = new GraphicsPath(); outline.AddEllipse(Circle(cap, radius - 12));
        DrawControl(g, logical, outline, 1.65f);
        using var inner = new Pen(Color.FromArgb(110, ControlColor(logical)), 1);
        g.DrawEllipse(inner, Circle(cap, radius - 17));
        _targets[logical] = Circle(center, radius);
    }

    private void DrawFaceButtons(Graphics g, PointF center, bool ds4)
    {
        var spacing = ds4 ? 39f : 40f;
        var radius = ds4 ? 18f : 20f;
        DrawFace(g, "north", ds4 ? "triangle" : "Y", new PointF(center.X, center.Y - spacing), radius);
        DrawFace(g, "east", ds4 ? "circle" : "B", new PointF(center.X + spacing, center.Y), radius);
        DrawFace(g, "south", ds4 ? "cross" : "A", new PointF(center.X, center.Y + spacing), radius);
        DrawFace(g, "west", ds4 ? "square" : "X", new PointF(center.X - spacing, center.Y), radius);
    }

    private void DrawFace(Graphics g, string logical, string symbol, PointF center, float radius)
    {
        using var shape = new GraphicsPath(); shape.AddEllipse(Circle(center, radius));
        DrawControl(g, logical, shape);
        using var ink = new Pen(ControlColor(logical), 1.85f) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
        var x = center.X; var y = center.Y;
        switch (symbol)
        {
            case "triangle": g.DrawPolygon(ink, new PointF[] { new(x, y - 10), new(x - 10, y + 8), new(x + 10, y + 8) }); break;
            case "circle": g.DrawEllipse(ink, Circle(center, 11)); break;
            case "cross": g.DrawLine(ink, x - 8, y - 8, x + 8, y + 8); g.DrawLine(ink, x + 8, y - 8, x - 8, y + 8); break;
            case "square": g.DrawRectangle(ink, x - 8, y - 8, 16, 16); break;
            default: DrawLabel(g, symbol, Circle(center, radius), 23, ControlColor(logical)); break;
        }
    }

    private void DrawDpad(Graphics g, PointF center, bool ds4)
    {
        if (!ds4)
        {
            using var ring = new Pen(Muted, 1);
            g.DrawEllipse(ring, Circle(center, 49));
            g.DrawEllipse(ring, Circle(center, 42));
        }
        var names = new[] { "dpad_up", "dpad_right", "dpad_down", "dpad_left" };
        for (var i = 0; i < 4; i++)
        {
            using var segment = new GraphicsPath();
            if (ds4)
            {
                segment.AddLine(-8, -39, 8, -39);
                segment.AddBezier(8, -39, 11, -39, 13, -37, 13, -34);
                segment.AddLine(13, -34, 12, -19);
                segment.AddBezier(12, -19, 12, -17, 3, -9, 0, -7);
                segment.AddBezier(0, -7, -3, -9, -12, -17, -12, -19);
                segment.AddLine(-12, -19, -13, -34);
                segment.AddBezier(-13, -34, -13, -37, -11, -39, -8, -39);
                segment.CloseFigure();
            }
            else
                segment.AddPolygon([new PointF(-11, -35), new(11, -35), new(11, -12), new(0, 0), new(-11, -12)]);
            using var matrix = new Matrix(); matrix.Rotate(i * 90); matrix.Translate(center.X, center.Y, MatrixOrder.Append); segment.Transform(matrix);
            DrawControl(g, names[i], segment, 1.3f);
            if (ds4)
            {
                using var arrow = new GraphicsPath(); arrow.AddPolygon([new PointF(-6, -46), new(6, -46), new(0, -52)]); arrow.Transform(matrix);
                using var ink = new SolidBrush(Color.FromArgb(145, ControlColor(names[i]))); g.FillPath(ink, arrow);
            }
        }
        if (!ds4)
        {
            // Hide the segment joins so the neutral Xbox pad reads as a single physical cross.
            using var centerFill = new SolidBrush(Color.FromArgb(25, 30, 32));
            g.FillRectangle(centerFill, center.X - 10, center.Y - 10, 20, 20);
        }
    }

    private void DrawCenterButton(Graphics g, string logical, RectangleF bounds, bool ds4)
    {
        using var shape = ds4 ? Rounded(bounds, 6) : new GraphicsPath();
        if (!ds4) shape.AddEllipse(bounds);
        DrawControl(g, logical, shape, 1);
        if (ds4) return;
        using var ink = new Pen(ControlColor(logical), .9f);
        if (logical == "share")
        {
            g.DrawRectangle(ink, bounds.X + 6, bounds.Y + 8, 9, 7);
            g.DrawRectangle(ink, bounds.X + 10, bounds.Y + 11, 9, 7);
        }
        else
            for (var i = 0; i < 3; i++) g.DrawLine(ink, bounds.X + 6, bounds.Y + 8 + i * 4, bounds.Right - 6, bounds.Y + 8 + i * 4);
    }

    private void DrawHome(Graphics g, PointF center, bool ds4)
    {
        var radius = ds4 ? 14f : 21f;
        using var shape = new GraphicsPath(); shape.AddEllipse(Circle(center, radius));
        DrawControl(g, "ps", shape, 1);
        if (ds4) { DrawLabel(g, "PS", Circle(center, radius), 11, ControlColor("ps")); return; }
        using var disc = new SolidBrush(ControlColor("ps"));
        g.FillEllipse(disc, Circle(center, radius - 2));
        using var cut = new Pen(Color.FromArgb(25, 30, 32), 5) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawBezier(cut, center.X - 14, center.Y - 15, center.X - 2, center.Y - 12, center.X + 12, center.Y + 3, center.X + 17, center.Y + 16);
        g.DrawBezier(cut, center.X + 14, center.Y - 15, center.X + 2, center.Y - 12, center.X - 12, center.Y + 3, center.X - 17, center.Y + 16);
    }

    private static RectangleF Circle(PointF center, float radius) => new(center.X - radius, center.Y - radius, radius * 2, radius * 2);

    private static void DrawLabel(Graphics g, string label, RectangleF bounds, float size, Color color)
    {
        using var font = new Font("Segoe UI", size, FontStyle.Regular, GraphicsUnit.Pixel);
        using var ink = new SolidBrush(color);
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap };
        g.DrawString(label, font, ink, bounds, format);
    }

    private static GraphicsPath Rounded(RectangleF r, float radius)
    {
        var d = radius * 2; var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90); path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); path.AddArc(r.X, r.Bottom - d, d, d, 90, 90); path.CloseFigure();
        return path;
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
        _hovered = hovered; Cursor = hovered == null ? Cursors.Default : Cursors.Hand; Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hovered = null; Cursor = Cursors.Default; Invalidate();
    }

    private string? HitTest(Point point)
    {
        var view = Viewport;
        var scale = Math.Min(ClientSize.Width / view.Width, ClientSize.Height / view.Height);
        if (scale <= 0) return null;
        var x = (point.X - (ClientSize.Width - view.Width * scale) / 2f) / scale + view.X;
        var y = (point.Y - (ClientSize.Height - view.Height * scale) / 2f) / scale;
        // Prefer the topmost painted control where enlarged shoulder hit areas overlap.
        foreach (var pair in _targets.Reverse()) if (pair.Value.Contains(x, y)) return pair.Key;
        return null;
    }
}
