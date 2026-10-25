using System.Drawing.Drawing2D;

namespace ControllerBridge.Host;

public sealed class DashboardForm : Form
{
    private readonly bool _english;
    private readonly HostServer _host;
    private readonly Action _stop;
    private readonly bool _wifiEnabled;
    private readonly ListBox _controllers = new();
    private readonly Label _connection = new();
    private readonly Label _network = new();
    private readonly Label _selectedName = new();
    private readonly Label _mappingHint = new();
    private readonly ComboBox _output = new();
    private readonly CheckBox _gyro = new();
    private readonly ControllerDiagram _diagram = new();
    private readonly Dictionary<string, ControllerSnapshot> _items = new(StringComparer.Ordinal);
    private string? _selectedKey;
    private bool _updating;

    public DashboardForm(bool english, HostServer host, string[] localIps, bool wifiEnabled, int wifiPort, string pairingCode, Action stop)
    {
        _english = english;
        _host = host;
        _stop = stop;
        _wifiEnabled = wifiEnabled;
        Text = "Controller Bridge";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(980, 650);
        Size = new Size(1220, 790);
        BackColor = Color.FromArgb(15, 19, 26);
        ForeColor = Color.FromArgb(232, 238, 246);
        Font = new Font("Segoe UI", 9.5f);

        var outer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(18), BackColor = BackColor };
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 84));
        outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(outer);
        outer.Controls.Add(BuildHeader(), 0, 0);

        var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = BackColor, Padding = new Padding(0, 12, 0, 0) };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 276));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        outer.Controls.Add(body, 0, 1);
        body.Controls.Add(BuildControllerList(), 0, 0);
        body.Controls.Add(BuildDetails(pairingCode), 1, 0);

        var ipText = localIps.Length == 0
            ? T("Nenhum IP local encontrado — conecte o PC e o celular à mesma rede.", "No local IP found — connect the PC and phone to the same network.")
            : string.Join("   ·   ", localIps);
        _network.Text = wifiEnabled
            ? $"{T("PORTA WI-FI", "WI-FI PORT")}  {wifiPort}     {T("IP DO PC", "PC IP")}  {ipText}\r\n{T("CÓDIGO DE PAREAMENTO", "PAIRING CODE")}  {pairingCode}     {T("Use o mesmo Wi-Fi no celular e no PC.", "Use the same Wi-Fi on the phone and PC.")}"
            : T("MODO USB/ADB · 127.0.0.1:47652\r\nConecte o celular por USB, ative a depuração USB e autorize este PC.", "USB/ADB MODE · 127.0.0.1:47652\r\nConnect the phone by USB, enable USB debugging, and authorize this PC.");
        OnConnectionCountChanged(host.PhoneConnectionCount);
        _host.ControllerChanged += OnControllerChanged;
        _host.PhoneConnectionCountChanged += OnConnectionCountChanged;
        FormClosing += (_, _) => _stop();
        Shown += (_, _) => RefreshControllerList();
    }

    private Control BuildHeader()
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(23, 30, 40), Padding = new Padding(18, 12, 18, 10) };
        var title = new Label { Text = "CONTROLLER BRIDGE", Font = new Font("Segoe UI Semibold", 18), ForeColor = Color.White, AutoSize = true, Location = new Point(0, 0) };
        var subtitle = new Label { Text = T("Receptor de controles · Windows 11", "Controller receiver · Windows 11"), ForeColor = Color.FromArgb(144, 159, 177), AutoSize = true, Location = new Point(2, 36) };
        _connection.Text = T("●  INICIANDO · abrindo a porta para o celular", "●  STARTING · opening the phone connection port");
        _connection.ForeColor = Color.FromArgb(249, 190, 74);
        _connection.Font = new Font("Segoe UI Semibold", 10);
        _connection.AutoSize = true;
        _connection.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _connection.Location = new Point(670, 22);
        panel.Controls.Add(title); panel.Controls.Add(subtitle); panel.Controls.Add(_connection);
        panel.Resize += (_, _) => _connection.Left = panel.ClientSize.Width - _connection.Width - 18;
        return panel;
    }

    private Control BuildControllerList()
    {
        var panel = Card(); panel.Dock = DockStyle.Fill; panel.Padding = new Padding(14);
        var heading = Label(T("CONTROLES CONECTADOS", "CONNECTED CONTROLLERS"), 10, true, Color.FromArgb(142, 158, 177));
        heading.Dock = DockStyle.Top; heading.Height = 28;
        _controllers.Dock = DockStyle.Fill;
        _controllers.BorderStyle = BorderStyle.None;
        _controllers.BackColor = Color.FromArgb(23, 30, 40);
        _controllers.ForeColor = Color.FromArgb(232, 238, 246);
        _controllers.Font = new Font("Segoe UI", 10);
        _controllers.IntegralHeight = false;
        _controllers.DrawMode = DrawMode.OwnerDrawFixed;
        _controllers.ItemHeight = 58;
        _controllers.DrawItem += DrawControllerItem;
        _controllers.SelectedIndexChanged += (_, _) => SelectControllerFromList();
        panel.Controls.Add(_controllers); panel.Controls.Add(heading);
        return panel;
    }

    private Control BuildDetails(string pairingCode)
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = BackColor, Padding = new Padding(12, 0, 0, 0) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 112));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));

        var info = Card(); info.Dock = DockStyle.Fill; info.Padding = new Padding(14, 10, 14, 10);
        var netHeader = Label(T("CONECTAR O CELULAR", "CONNECT YOUR PHONE"), 9.5f, true, Color.FromArgb(105, 183, 255));
        netHeader.Dock = DockStyle.Top; netHeader.Height = 21;
        _network.AutoSize = false; _network.Dock = DockStyle.Fill; _network.Font = new Font("Segoe UI", 9); _network.ForeColor = Color.FromArgb(218, 226, 237); _network.Padding = new Padding(0, 6, 0, 0);
        var copy = ActionButton(T("Copiar código", "Copy code")); copy.Dock = DockStyle.Right; copy.Width = 116;
        copy.Enabled = _wifiEnabled;
        copy.Click += (_, _) => { Clipboard.SetText(pairingCode); _mappingHint.Text = T("Código de pareamento copiado.", "Pairing code copied."); };
        info.Controls.Add(_network); info.Controls.Add(copy); info.Controls.Add(netHeader);
        layout.Controls.Add(info, 0, 0);

        var diagramCard = Card(); diagramCard.Dock = DockStyle.Fill; diagramCard.Padding = new Padding(14);
        var diagramLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = diagramCard.BackColor };
        diagramLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); diagramLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 224));
        _diagram.Dock = DockStyle.Fill; _diagram.LogicalButtonClicked += RemapButton;
        var settings = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 10, 8, 4), BackColor = diagramCard.BackColor };
        _selectedName.Text = T("Selecione um controle", "Select a controller"); _selectedName.Font = new Font("Segoe UI Semibold", 14); _selectedName.ForeColor = Color.White; _selectedName.AutoSize = true; _selectedName.Dock = DockStyle.Top; _selectedName.Height = 54;
        var outputLabel = Label(T("SAÍDA VIRTUAL", "VIRTUAL OUTPUT"), 9, true, Color.FromArgb(142, 158, 177)); outputLabel.Dock = DockStyle.Top; outputLabel.Height = 24;
        _output.DropDownStyle = ComboBoxStyle.DropDownList; _output.Items.AddRange(["Xbox 360 (XInput)", "DualShock 4"]); _output.Dock = DockStyle.Top; _output.Height = 34; _output.BackColor = Color.FromArgb(31, 40, 52); _output.ForeColor = Color.White;
        _output.SelectedIndexChanged += (_, _) => ChangeOutput();
        _gyro.Text = T("Giroscópio DS4", "DS4 gyro"); _gyro.AutoSize = true; _gyro.ForeColor = Color.FromArgb(222, 231, 242); _gyro.Dock = DockStyle.Top; _gyro.Padding = new Padding(0, 13, 0, 0); _gyro.CheckedChanged += (_, _) => ChangeOutput();
        _mappingHint.Text = T("Clique em um botão do desenho para escolher o botão físico no celular.", "Click a button on the diagram, then press its physical replacement on the phone."); _mappingHint.Dock = DockStyle.Bottom; _mappingHint.Height = 58; _mappingHint.ForeColor = Color.FromArgb(148, 163, 181); _mappingHint.AutoSize = false;
        settings.Controls.Add(_mappingHint); settings.Controls.Add(_gyro); settings.Controls.Add(_output); settings.Controls.Add(outputLabel); settings.Controls.Add(_selectedName);
        diagramLayout.Controls.Add(_diagram, 0, 0); diagramLayout.Controls.Add(settings, 1, 0); diagramCard.Controls.Add(diagramLayout);
        layout.Controls.Add(diagramCard, 0, 1);

        var bottom = Card(); bottom.Dock = DockStyle.Fill; bottom.Padding = new Padding(14, 11, 14, 8);
        var tip = Label(T("Mantenha esta janela aberta enquanto joga. Para encerrar, digite stop ou parar no console.", "Keep this window open while playing. To stop, type stop or parar in the console."), 9.5f, false, Color.FromArgb(161, 174, 190));
        tip.Dock = DockStyle.Fill;
        bottom.Controls.Add(tip); layout.Controls.Add(bottom, 0, 2);
        return layout;
    }

    private void OnControllerChanged(ControllerSnapshot snapshot)
    {
        if (IsDisposed) return;
        if (InvokeRequired) { BeginInvoke(new Action(() => OnControllerChanged(snapshot))); return; }
        var key = Key(snapshot);
        if (snapshot.Connected)
        {
            if (_items.TryGetValue(key, out var previous) && snapshot.Revision > previous.Revision)
                _mappingHint.Text = T("Perfil salvo e sincronizado com o celular.", "Profile saved and synced to the phone.");
            _items[key] = snapshot;
        }
        else _items.Remove(key);
        RefreshControllerList(key);
    }

    private void OnConnectionCountChanged(int count)
    {
        if (IsDisposed) return;
        if (InvokeRequired) { BeginInvoke(new Action(() => OnConnectionCountChanged(count))); return; }
        _connection.Text = count > 0
            ? T($"●  CELULAR CONECTADO · {count} sessão", $"●  PHONE CONNECTED · {count} session")
            : T("●  AGUARDANDO CELULAR · porta aberta", "●  WAITING FOR PHONE · port is open");
        _connection.ForeColor = count > 0 ? Color.FromArgb(92, 218, 154) : Color.FromArgb(249, 190, 74);
        RefreshControllerList();
    }

    private void RefreshControllerList(string? preferKey = null)
    {
        if (IsDisposed) return;
        var old = _selectedKey;
        if (preferKey != null) old = preferKey;
        var snapshots = _host.GetControllers();
        _items.Clear(); foreach (var item in snapshots) _items[Key(item)] = item;
        var keys = _items.Keys.ToArray();
        _updating = true;
        _controllers.Items.Clear(); foreach (var key in keys) _controllers.Items.Add(key);
        _controllers.SelectedItem = old != null && _items.ContainsKey(old) ? old : keys.FirstOrDefault();
        _updating = false;
        if (_controllers.SelectedItem == null) { _selectedKey = null; UpdateSelection(null); }
        else { _selectedKey = (string)_controllers.SelectedItem; UpdateSelection(_items[_selectedKey]); }
        _controllers.Invalidate();
    }

    private void SelectControllerFromList()
    {
        if (_updating || _controllers.SelectedItem is not string key) return;
        _selectedKey = key; UpdateSelection(_items.GetValueOrDefault(key)); _controllers.Invalidate();
    }

    private void UpdateSelection(ControllerSnapshot? snapshot)
    {
        _updating = true;
        _selectedName.Text = snapshot?.Name ?? T("Selecione um controle", "Select a controller");
        _output.Enabled = snapshot != null; _gyro.Enabled = snapshot?.Output == "ds4";
        _output.SelectedIndex = snapshot?.Output == "ds4" ? 1 : 0; _gyro.Checked = snapshot?.GyroEnabled ?? false;
        _diagram.SetController(snapshot);
        _mappingHint.Text = snapshot == null
            ? T("Conecte o app do celular para começar.", "Connect the phone app to get started.")
            : T("Clique em um botão do desenho para escolher o botão físico no celular.", "Click a button on the diagram, then press its physical replacement on the phone.");
        _updating = false;
    }

    private void ChangeOutput()
    {
        if (_updating || _selectedKey == null || !_items.TryGetValue(_selectedKey, out var snapshot) || _output.SelectedIndex < 0) return;
        try
        {
            var output = _output.SelectedIndex == 1 ? "ds4" : "xbox360";
            _host.SetControllerOutput(snapshot, output, output == "ds4" && _gyro.Checked);
            _mappingHint.Text = T("Perfil atualizado e sincronizado com o celular.", "Profile updated and synced to the phone.");
        }
        catch (Exception error) { _mappingHint.Text = error.GetBaseException().Message; }
    }

    private void RemapButton(string logicalButton)
    {
        if (_selectedKey == null || !_items.TryGetValue(_selectedKey, out var snapshot)) return;
        try
        {
            _host.RequestButtonCapture(snapshot, logicalButton);
            _mappingHint.Text = T("Agora pressione no celular o botão físico que vai substituir ", "Now press on the phone the physical button to use for ") + DisplayButton(logicalButton) + ".";
        }
        catch (Exception error) { _mappingHint.Text = error.GetBaseException().Message; }
    }

    private void DrawControllerItem(object? sender, DrawItemEventArgs e)
    {
        e.DrawBackground();
        if (e.Index < 0 || e.Index >= _controllers.Items.Count) return;
        var key = (string)_controllers.Items[e.Index]; if (!_items.TryGetValue(key, out var snapshot)) return;
        var selected = (e.State & DrawItemState.Selected) != 0;
        using var brush = new SolidBrush(selected ? Color.FromArgb(43, 62, 82) : Color.FromArgb(23, 30, 40));
        using var boldFont = new Font(Font, FontStyle.Bold);
        e.Graphics.FillRectangle(brush, e.Bounds);
        TextRenderer.DrawText(e.Graphics, snapshot.Name, boldFont, new Rectangle(e.Bounds.X + 12, e.Bounds.Y + 10, e.Bounds.Width - 20, 20), Color.White, TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(e.Graphics, (snapshot.Output == "ds4" ? "DualShock 4" : "Xbox 360") + "  ·  " + T("ativo", "active"), Font, new Rectangle(e.Bounds.X + 12, e.Bounds.Y + 32, e.Bounds.Width - 20, 18), Color.FromArgb(150, 170, 190), TextFormatFlags.EndEllipsis);
    }

    private Panel Card() => new() { BackColor = Color.FromArgb(23, 30, 40), Margin = new Padding(0) };
    private Label Label(string text, float size, bool bold, Color color) => new() { Text = text, Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular), ForeColor = color, BackColor = Color.Transparent, AutoEllipsis = true };
    private Button ActionButton(string text) => new() { Text = text, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(42, 88, 130), ForeColor = Color.White, Cursor = Cursors.Hand };
    private string T(string pt, string en) => _english ? en : pt;
    private static string Key(ControllerSnapshot snapshot) => snapshot.SessionId + "/" + snapshot.ControllerId;
    private string DisplayButton(string name) => name switch
    {
        "south" => "✕ / A", "east" => "○ / B", "west" => "□ / X", "north" => "△ / Y",
        "l1" => "L1 / LB", "r1" => "R1 / RB", "l2" => "L2 / LT", "r2" => "R2 / RT",
        "share" => T("Compartilhar / View", "Share / View"), "options" => "Options / Menu", "l3" => "L3 / LS", "r3" => "R3 / RS", "ps" => "PS / Guide",
        "dpad_up" => "D-pad ↑", "dpad_down" => "D-pad ↓", "dpad_left" => "D-pad ←", "dpad_right" => "D-pad →", _ => name
    };
}

internal sealed class ControllerDiagram : Control
{
    private ControllerSnapshot? _snapshot;
    public event Action<string>? LogicalButtonClicked;
    private readonly Dictionary<string, RectangleF> _targets = new(StringComparer.Ordinal);

    public ControllerDiagram()
    {
        DoubleBuffered = true; BackColor = Color.FromArgb(23, 30, 40); Cursor = Cursors.Hand;
        ResizeRedraw = true;
    }

    public void SetController(ControllerSnapshot? snapshot) { _snapshot = snapshot; Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; _targets.Clear();
        var scale = Math.Min(ClientSize.Width / 500f, ClientSize.Height / 340f);
        if (scale <= 0) return;
        var ox = (ClientSize.Width - 500 * scale) / 2f; var oy = (ClientSize.Height - 340 * scale) / 2f;
        g.TranslateTransform(ox, oy); g.ScaleTransform(scale, scale);
        using var body = new SolidBrush(Color.FromArgb(42, 51, 64));
        using var edge = new Pen(Color.FromArgb(77, 92, 110), 2.5f);
        using var path = new GraphicsPath();
        path.AddBezier(85, 107, 45, 108, 48, 205, 25, 276); path.AddBezier(25, 276, 17, 309, 54, 328, 77, 306);
        path.AddBezier(77, 306, 118, 268, 143, 253, 180, 260); path.AddBezier(180, 260, 221, 270, 279, 270, 320, 260);
        path.AddBezier(320, 260, 357, 253, 382, 268, 423, 306); path.AddBezier(423, 306, 446, 328, 483, 309, 475, 276);
        path.AddBezier(475, 276, 452, 205, 455, 108, 415, 107); path.AddBezier(415, 107, 371, 104, 330, 134, 250, 134);
        path.AddBezier(250, 134, 170, 134, 129, 104, 85, 107); path.CloseFigure();
        g.FillPath(body, path); g.DrawPath(edge, path);
        DrawButton(g, "dpad_up", "▲", new RectangleF(105, 165, 34, 31)); DrawButton(g, "dpad_down", "▼", new RectangleF(105, 223, 34, 31));
        DrawButton(g, "dpad_left", "◀", new RectangleF(77, 194, 32, 31)); DrawButton(g, "dpad_right", "▶", new RectangleF(135, 194, 32, 31));
        DrawButton(g, "north", "△", new RectangleF(355, 162, 38, 38)); DrawButton(g, "east", "○", new RectangleF(388, 200, 38, 38));
        DrawButton(g, "south", "×", new RectangleF(355, 238, 38, 38)); DrawButton(g, "west", "□", new RectangleF(322, 200, 38, 38));
        DrawButton(g, "l1", "L1", new RectangleF(78, 109, 78, 26)); DrawButton(g, "r1", "R1", new RectangleF(344, 109, 78, 26));
        DrawButton(g, "l2", "L2", new RectangleF(78, 77, 78, 25)); DrawButton(g, "r2", "R2", new RectangleF(344, 77, 78, 25));
        DrawButton(g, "share", "SHARE", new RectangleF(183, 152, 55, 24)); DrawButton(g, "options", "OPTIONS", new RectangleF(263, 152, 55, 24));
        DrawStick(g, "l3", new PointF(185, 220)); DrawStick(g, "r3", new PointF(275, 220));
        DrawButton(g, "ps", "PS", new RectangleF(230, 183, 40, 34));
        var name = _snapshot?.Name ?? "";
        using var captionBrush = new SolidBrush(Color.FromArgb(153, 168, 185));
        using var font = new Font("Segoe UI", 9, FontStyle.Bold);
        g.DrawString(name, font, captionBrush, new RectangleF(145, 298, 210, 20), new StringFormat { Alignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter });
        g.ResetTransform();
    }

    private void DrawStick(Graphics g, string logical, PointF center)
    {
        var pressed = _snapshot?.State.Button(logical) ?? false;
        var fillColor = pressed ? Color.FromArgb(74, 164, 255) : Color.FromArgb(31, 39, 51);
        using var brush = new SolidBrush(fillColor); using var pen = new Pen(Color.FromArgb(94, 111, 132), 2);
        var rect = new RectangleF(center.X - 26, center.Y - 26, 52, 52); g.FillEllipse(brush, rect); g.DrawEllipse(pen, rect);
        var hit = new RectangleF(center.X - 31, center.Y - 31, 62, 62); _targets[logical] = hit;
        using var font = new Font("Segoe UI", 8, FontStyle.Bold); using var text = new SolidBrush(Color.FromArgb(223, 232, 243));
        g.DrawString(logical.ToUpperInvariant(), font, text, new RectangleF(rect.X, rect.Y + 17, rect.Width, 18), new StringFormat { Alignment = StringAlignment.Center });
    }

    private void DrawButton(Graphics g, string logical, string text, RectangleF rect)
    {
        var pressed = _snapshot?.State.Button(logical) ?? false;
        var active = Color.FromArgb(70, 164, 255); var idle = Color.FromArgb(31, 39, 51);
        using var brush = new SolidBrush(pressed ? active : idle); using var pen = new Pen(pressed ? Color.FromArgb(130, 203, 255) : Color.FromArgb(94, 111, 132), 1.5f);
        g.FillRoundedRectangle(brush, rect, 8); g.DrawRoundedRectangle(pen, rect, 8);
        _targets[logical] = RectangleF.Inflate(rect, 4, 4);
        using var font = new Font("Segoe UI", text.Length > 3 ? 7 : 11, FontStyle.Bold); using var ink = new SolidBrush(Color.FromArgb(231, 238, 247));
        g.DrawString(text, font, ink, rect, new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        var scale = Math.Min(ClientSize.Width / 500f, ClientSize.Height / 340f);
        if (scale <= 0) return;
        var x = (e.X - (ClientSize.Width - 500 * scale) / 2f) / scale;
        var y = (e.Y - (ClientSize.Height - 340 * scale) / 2f) / scale;
        foreach (var pair in _targets) if (pair.Value.Contains(x, y)) { LogicalButtonClicked?.Invoke(pair.Key); return; }
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
