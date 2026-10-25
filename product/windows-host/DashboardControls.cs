using System.Drawing.Drawing2D;

namespace ControllerBridge.Host;

// Native button text reserves more vertical padding than the compact editor has.
// Keep Button's keyboard/accessibility behavior and draw its content without that padding.
internal sealed class DashboardActionButton : Button
{
    public bool ActiveTab { get; set; }
    private bool _hovered;
    private bool _pressed;

    public DashboardActionButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hovered = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hovered = false; _pressed = false; Invalidate(); }
    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button == MouseButtons.Left) _pressed = true; Invalidate(); }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); _pressed = false; Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var background = _pressed ? FlatAppearance.MouseDownBackColor : _hovered ? FlatAppearance.MouseOverBackColor : BackColor;
        if (background.IsEmpty) background = BackColor;
        e.Graphics.Clear(background);
        if (FlatAppearance.BorderSize > 0)
        {
            using var border = new Pen(FlatAppearance.BorderColor, FlatAppearance.BorderSize);
            var inset = FlatAppearance.BorderSize / 2f;
            e.Graphics.DrawRectangle(border, inset, inset, Width - FlatAppearance.BorderSize, Height - FlatAppearance.BorderSize);
        }
        var bounds = Rectangle.FromLTRB(Padding.Left + 3, Padding.Top, Width - Padding.Right - 3, Height - Padding.Bottom);
        var flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis;
        flags |= TextAlign is ContentAlignment.MiddleLeft or ContentAlignment.TopLeft or ContentAlignment.BottomLeft ? TextFormatFlags.Left : TextFormatFlags.HorizontalCenter;
        TextRenderer.DrawText(e.Graphics, Text, Font, bounds, Enabled ? ForeColor : Color.FromArgb(135, 148, 155), flags);
        if (ActiveTab)
        {
            using var accent = new SolidBrush(Color.FromArgb(54, 153, 244));
            e.Graphics.FillRectangle(accent, 0, Height - 4, Width, 4);
        }
        if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -4, -4), ForeColor, background);
    }
}

internal sealed class DashboardComboBox : ComboBox
{
    protected override void WndProc(ref Message message)
    {
        base.WndProc(ref message);
        if (message.Msg != 0x000F || !IsHandleCreated || Width < 24 || Height < 10) return;
        using var graphics = CreateGraphics();
        var width = Math.Max(22, SystemInformation.VerticalScrollBarWidth + 5);
        var rectangle = new Rectangle(Width - width, 0, width, Height);
        using var fill = new SolidBrush(BackColor);
        graphics.FillRectangle(fill, rectangle);
        using var border = new Pen(Color.FromArgb(71, 82, 88));
        graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
        var x = rectangle.Left + rectangle.Width / 2f;
        var y = Height / 2f;
        using var arrow = new Pen(Enabled ? ForeColor : Color.FromArgb(135, 148, 155), 1.5f);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.DrawLines(arrow, [new PointF(x - 4, y - 2), new PointF(x, y + 2), new PointF(x + 4, y - 2)]);
    }
}
