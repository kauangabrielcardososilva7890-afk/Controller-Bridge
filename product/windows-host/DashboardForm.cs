using System.Text.Json;
using System.Collections.Concurrent;

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
    private readonly Label _motion = new();
    private readonly Label _axes = new();
    private readonly Dictionary<string, Button> _mappingFields = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Label> _mappingLabels = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (string Button, int Revision)> _captures = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ControllerSnapshot> _incoming = new(StringComparer.Ordinal);
    private readonly System.Windows.Forms.Timer _refreshTimer = new() { Interval = 50 };
    private readonly Dictionary<string, long> _lastInput = new(StringComparer.Ordinal);
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
        MinimumSize = new Size(1120, 840);
        Size = new Size(1380, 980);
        BackColor = Color.FromArgb(15, 19, 26);
        ForeColor = Color.FromArgb(232, 238, 246);
        Font = new Font("Segoe UI", 9.5f);

        var outer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(18), BackColor = BackColor };
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
        outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(outer);
        outer.Controls.Add(BuildHeader(), 0, 0);

        var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = BackColor, Padding = new Padding(0, 12, 0, 0) };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 238));
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
        _refreshTimer.Tick += (_, _) => DrainControllerUpdates();
        _refreshTimer.Start();
        FormClosing += (_, _) => _stop();
        FormClosed += (_, _) =>
        {
            _host.ControllerChanged -= OnControllerChanged;
            _host.PhoneConnectionCountChanged -= OnConnectionCountChanged;
            _refreshTimer.Stop();
            _refreshTimer.Dispose();
        };
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
        var heading = Label(T("CONTROLES / JOGADORES", "CONTROLLERS / PLAYERS"), 10, true, Color.FromArgb(142, 158, 177));
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
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 96));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        var info = Card(); info.Dock = DockStyle.Fill; info.Padding = new Padding(14, 10, 14, 10);
        var netHeader = Label(T("CONECTAR O CELULAR", "CONNECT YOUR PHONE"), 9.5f, true, Color.FromArgb(105, 183, 255));
        netHeader.Dock = DockStyle.Top; netHeader.Height = 21;
        _network.AutoSize = false; _network.Dock = DockStyle.Fill; _network.Font = new Font("Segoe UI", 9); _network.ForeColor = Color.FromArgb(218, 226, 237); _network.Padding = new Padding(0, 6, 0, 0);
        var copy = ActionButton(T("Copiar código", "Copy code")); copy.Dock = DockStyle.Right; copy.Width = 116;
        copy.Enabled = _wifiEnabled;
        copy.Click += (_, _) => { Clipboard.SetText(pairingCode); _mappingHint.Text = T("Código de pareamento copiado.", "Pairing code copied."); };
        info.Controls.Add(_network); info.Controls.Add(copy); info.Controls.Add(netHeader);
        layout.Controls.Add(info, 0, 0);

        var editor = Card(); editor.Dock = DockStyle.Fill; editor.Padding = new Padding(12);
        var rows = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, BackColor = editor.BackColor };
        rows.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        rows.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        rows.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        rows.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        editor.Controls.Add(rows);

        var settings = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
        settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        settings.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 216));
        settings.RowStyles.Add(new RowStyle(SizeType.Absolute, 27));
        settings.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _selectedName.Text = T("Selecione um controle", "Select a controller");
        _selectedName.Font = new Font("Segoe UI Semibold", 12); _selectedName.ForeColor = Color.White; _selectedName.Dock = DockStyle.Fill; _selectedName.AutoEllipsis = true;
        settings.Controls.Add(_selectedName, 0, 0);
        var physical = Label(T("Entrada física selecionada à esquerda", "Physical input selected on the left"), 9, false, Color.FromArgb(144, 159, 177)); physical.Dock = DockStyle.Fill;
        settings.Controls.Add(physical, 0, 1);
        var outputLabel = Label(T("SAÍDA VIRTUAL PARA O JOGO", "VIRTUAL OUTPUT TO THE GAME"), 9, true, Color.FromArgb(142, 158, 177)); outputLabel.Dock = DockStyle.Fill;
        settings.Controls.Add(outputLabel, 1, 0);
        _output.DropDownStyle = ComboBoxStyle.DropDownList; _output.Items.AddRange(["Xbox 360 (XInput)", "DualShock 4"]); _output.Dock = DockStyle.Top; _output.BackColor = Color.FromArgb(31, 40, 52); _output.ForeColor = Color.White;
        _output.SelectedIndexChanged += (_, _) => ChangeOutput(); settings.Controls.Add(_output, 1, 1);
        rows.Controls.Add(settings, 0, 0);

        var map = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 2 };
        map.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        map.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        map.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        map.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        map.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
        map.Controls.Add(BuildMappingGroups((T("OMBRO ESQUERDO", "LEFT SHOULDER"), new[] { "l1", "l2" }), (T("DIRECIONAL", "DIRECTION PAD"), new[] { "dpad_up", "dpad_down", "dpad_left", "dpad_right" }), (T("ANALÓGICO ESQUERDO", "LEFT STICK"), new[] { "l3" })), 0, 0);
        map.Controls.Add(BuildMappingGroups((T("OMBRO DIREITO", "RIGHT SHOULDER"), new[] { "r1", "r2" }), (T("BOTÕES", "FACE BUTTONS"), new[] { "north", "west", "east", "south" }), (T("ANALÓGICO DIREITO", "RIGHT STICK"), new[] { "r3" })), 2, 0);
        var center = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        center.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); center.RowStyles.Add(new RowStyle(SizeType.Absolute, 26)); center.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        _diagram.Dock = DockStyle.Fill; _diagram.LogicalButtonClicked += RemapButton; center.Controls.Add(_diagram, 0, 0);
        var virtualHint = Label(T("CONTROLE VIRTUAL · resposta ao vivo", "VIRTUAL CONTROLLER · live response"), 8.5f, true, Color.FromArgb(142, 158, 177)); virtualHint.Dock = DockStyle.Fill; virtualHint.TextAlign = ContentAlignment.MiddleCenter;
        center.Controls.Add(virtualHint, 0, 1);
        _axes.Dock = DockStyle.Fill; _axes.TextAlign = ContentAlignment.MiddleCenter; _axes.Font = new Font("Segoe UI", 8.5f); _axes.ForeColor = Color.FromArgb(153, 168, 185);
        center.Controls.Add(_axes, 0, 2); map.Controls.Add(center, 1, 0);
        var systemButtons = BuildMappingGroups((T("SISTEMA", "SYSTEM"), new[] { "share", "ps", "options" }));
        map.Controls.Add(systemButtons, 1, 1);
        rows.Controls.Add(map, 0, 1);

        var motion = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(0, 8, 0, 0) };
        motion.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170)); motion.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _gyro.Text = T("Giroscópio DS4", "DS4 gyro"); _gyro.Dock = DockStyle.Fill; _gyro.ForeColor = Color.FromArgb(222, 231, 242); _gyro.CheckedChanged += (_, _) => ChangeOutput(); motion.Controls.Add(_gyro, 0, 0);
        _motion.Dock = DockStyle.Fill; _motion.ForeColor = Color.FromArgb(153, 168, 185); _motion.Font = new Font("Segoe UI", 9); motion.Controls.Add(_motion, 1, 0); rows.Controls.Add(motion, 0, 2);
        _mappingHint.Dock = DockStyle.Fill; _mappingHint.ForeColor = Color.FromArgb(159, 194, 224); _mappingHint.Padding = new Padding(0, 8, 0, 0); rows.Controls.Add(_mappingHint, 0, 3);
        layout.Controls.Add(editor, 0, 1);
        var tip = Label(T("Clique em um campo e pressione o botão no controle físico. Mantenha esta janela aberta enquanto joga.", "Click a field, then press a button on the physical controller. Keep this window open while playing."), 9, false, Color.FromArgb(161, 174, 190)); tip.Dock = DockStyle.Fill; tip.Padding = new Padding(4, 12, 4, 0); layout.Controls.Add(tip, 0, 2);
        return layout;
    }

    private Control BuildMappingGroups(params (string Title, string[] Buttons)[] groups)
    {
        if (groups.Length == 1)
        {
            var system = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 2, Margin = new Padding(10, 0, 10, 0) };
            for (var index = 0; index < groups[0].Buttons.Length; index++)
            {
                system.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3));
                var logical = groups[0].Buttons[index];
                var caption = Label(logical, 9, true, Color.FromArgb(222, 231, 242)); caption.Dock = DockStyle.Fill; caption.TextAlign = ContentAlignment.MiddleCenter; _mappingLabels[logical] = caption;
                var field = ActionButton("—"); field.Dock = DockStyle.Fill; field.Font = new Font("Segoe UI", 8.5f); field.Margin = new Padding(3); field.Click += (_, _) => RemapButton(logical); _mappingFields[logical] = field;
                system.Controls.Add(caption, index, 0); system.Controls.Add(field, index, 1);
            }
            system.RowStyles.Add(new RowStyle(SizeType.Absolute, 25)); system.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            return system;
        }
        var stack = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Margin = new Padding(0) };
        foreach (var group in groups)
        {
            var box = new TableLayoutPanel { ColumnCount = 2, RowCount = group.Buttons.Length + 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(0, 0, 0, 9), Padding = new Padding(3), Width = 162 };
            box.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 52)); box.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            var title = Label(group.Title, 8, true, Color.FromArgb(142, 158, 177)); title.Dock = DockStyle.Fill; title.Height = 23; box.Controls.Add(title, 0, 0); box.SetColumnSpan(title, 2);
            foreach (var logical in group.Buttons)
            {
                var row = Array.IndexOf(group.Buttons, logical) + 1;
                var caption = Label(logical, 9, true, Color.FromArgb(222, 231, 242)); caption.Dock = DockStyle.Fill; caption.TextAlign = ContentAlignment.MiddleLeft; _mappingLabels[logical] = caption;
                var field = ActionButton("—"); field.Height = 29; field.Dock = DockStyle.Fill; field.Margin = new Padding(0, 2, 0, 2); field.Font = new Font("Segoe UI", 8.5f); field.FlatAppearance.BorderColor = Color.FromArgb(64, 83, 103); field.Click += (_, _) => RemapButton(logical); _mappingFields[logical] = field;
                box.Controls.Add(caption, 0, row); box.Controls.Add(field, 1, row);
            }
            stack.Controls.Add(box);
        }
        return stack;
    }
    // Network input can arrive much faster than the screen can paint. Coalesce it per controller.
    private void OnControllerChanged(ControllerSnapshot snapshot) => _incoming[Key(snapshot)] = snapshot;

    private void DrainControllerUpdates()
    {
        var membershipChanged = false;
        foreach (var key in _incoming.Keys)
        {
            if (!_incoming.TryRemove(key, out var snapshot)) continue;
            if (!snapshot.Connected)
            {
                membershipChanged |= _items.Remove(key);
                _captures.Remove(key);
                _lastInput.Remove(key);
                continue;
            }
            membershipChanged |= !_items.ContainsKey(key);
            if (_captures.TryGetValue(key, out var capture) && snapshot.Revision > capture.Revision)
            {
                _captures.Remove(key);
                if (key == _selectedKey) _mappingHint.Text = T("Mapeamento salvo e sincronizado.", "Mapping saved and synced.");
            }
            _items[key] = snapshot;
            _lastInput[key] = Environment.TickCount64;
        }
        if (membershipChanged) RebuildControllerList();
        else if (_selectedKey != null && _items.TryGetValue(_selectedKey, out var selected)) UpdateSelection(selected);
        _controllers.Invalidate();
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

    private void RefreshControllerList()
    {
        if (IsDisposed) return;
        var snapshots = _host.GetControllers();
        _items.Clear(); foreach (var item in snapshots) _items[Key(item)] = item;
        RebuildControllerList();
    }

    private void RebuildControllerList()
    {
        var old = _selectedKey;
        var keys = _items.Keys.ToArray();
        _updating = true;
        _controllers.BeginUpdate();
        _controllers.Items.Clear(); foreach (var key in keys) _controllers.Items.Add(key);
        _controllers.SelectedItem = old != null && _items.ContainsKey(old) ? old : keys.FirstOrDefault();
        _controllers.EndUpdate();
        _updating = false;
        if (_controllers.SelectedItem == null) { _selectedKey = null; UpdateSelection(null); }
        else
        {
            _selectedKey = (string)_controllers.SelectedItem;
            if (old != _selectedKey) _mappingHint.Text = "";
            UpdateSelection(_items[_selectedKey]);
        }
        _controllers.Invalidate();
    }

    private void SelectControllerFromList()
    {
        if (_updating || _controllers.SelectedItem is not string key) return;
        _selectedKey = key; _mappingHint.Text = ""; UpdateSelection(_items.GetValueOrDefault(key)); _controllers.Invalidate();
    }

    private void UpdateSelection(ControllerSnapshot? snapshot)
    {
        _updating = true;
        _selectedName.Text = snapshot == null ? T("Selecione um controle", "Select a controller") : T("Entrada: ", "Input: ") + snapshot.Name;
        _output.Enabled = snapshot != null; _gyro.Enabled = snapshot?.Output == "ds4";
        _output.SelectedIndex = snapshot?.Output == "ds4" ? 1 : 0; _gyro.Checked = snapshot?.GyroEnabled ?? false;
        var fresh = snapshot != null && _lastInput.TryGetValue(Key(snapshot), out var last) && Environment.TickCount64 - last < 1000;
        var visible = snapshot == null || fresh ? snapshot : snapshot with { State = GamepadState.Neutral() };
        _diagram.Enabled = snapshot != null;
        _diagram.SetController(visible);
        foreach (var pair in _mappingFields)
        {
            var pending = snapshot != null && _captures.TryGetValue(Key(snapshot), out var capture) && capture.Button == pair.Key;
            pair.Value.Enabled = snapshot != null;
            pair.Value.Text = pending ? T("Pressione…", "Press…") : snapshot == null ? "—" : SourceLabel(snapshot, pair.Key);
            pair.Value.BackColor = pending ? Color.FromArgb(130, 88, 30) : visible?.State.Button(pair.Key) == true ? Color.FromArgb(39, 126, 185) : Color.FromArgb(31, 43, 56);
            _mappingLabels[pair.Key].Text = DisplayButton(pair.Key);
            pair.Value.AccessibleName = DisplayButton(pair.Key) + ": " + pair.Value.Text;
        }
        if (snapshot == null) _mappingHint.Text = T("Conecte o app do celular para começar.", "Connect the phone app to get started.");
        else if (_captures.TryGetValue(Key(snapshot), out var waiting)) _mappingHint.Text = T("Pressione no controle físico o botão para ", "Press the physical controller button for ") + DisplayButton(waiting.Button) + T(". Clique em outro campo para trocar o destino.", ". Click another field to change the target.");
        else if (string.IsNullOrEmpty(_mappingHint.Text)) _mappingHint.Text = T("Escolha um campo para remapear este controle.", "Choose a field to remap this controller.");
        var state = visible?.State ?? GamepadState.Neutral();
        _axes.Text = $"L  {state.Axis("lx"),5:0.00} / {state.Axis("ly"),5:0.00}    R  {state.Axis("rx"),5:0.00} / {state.Axis("ry"),5:0.00}\r\n{(snapshot?.Output == "ds4" ? "L2 / R2" : "LT / RT")}  {state.Axis("lt"):0.00} / {state.Axis("rt"):0.00}";
        _motion.Text = snapshot == null ? T("Movimento · nenhum controle selecionado", "Motion · no controller selected")
            : snapshot.Output != "ds4" ? T("Movimento · indisponível na saída Xbox 360", "Motion · unavailable on Xbox 360 output")
            : !snapshot.GyroEnabled ? T("Movimento · desativado\r\nRequer sensor do controle exposto pelo Android.", "Motion · disabled\r\nRequires a controller sensor exposed by Android.")
            : state.Motion is not { } motion ? T("Movimento · sensor não detectado / aguardando dados", "Motion · sensor not detected / waiting for data")
            : $"{T("Movimento · recebendo", "Motion · receiving")}\r\nGyro (rad/s)  X {motion.GyroRadiansPerSecond.X:0.00}   Y {motion.GyroRadiansPerSecond.Y:0.00}   Z {motion.GyroRadiansPerSecond.Z:0.00}";
        if (snapshot?.Output == "ds4" && snapshot.GyroEnabled)
        {
            var provider = _host.GetMotionProviderStatus();
            var endpoint = provider.Endpoint?.ToString() ?? $"127.0.0.1:{DsuMotionServer.DefaultPort}";
            var providerText = provider.Running
                ? T("UDP local ativo · ", "Local UDP active · ") + endpoint
                : provider.Error?.Contains("in use", StringComparison.OrdinalIgnoreCase) == true
                    ? T("UDP 26760 ocupado · feche outro programa de movimento", "UDP 26760 is busy · close the other motion app")
                    : T("Iniciando servidor UDP local…", "Starting local UDP server…");
            _motion.Text += "\r\n" + providerText;
        }
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
            // Android keeps one pending capture per phone session. Preserve captures on other phones.
            foreach (var key in _captures.Keys.Where(key => key.StartsWith(snapshot.SessionId + "/", StringComparison.Ordinal)).ToArray()) _captures.Remove(key);
            _captures[_selectedKey] = (logicalButton, snapshot.Revision);
            UpdateSelection(snapshot);
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
        TextRenderer.DrawText(e.Graphics, T($"Controle {e.Index + 1}", $"Controller {e.Index + 1}") + " · " + (snapshot.Output == "ds4" ? "DualShock 4" : "Xbox 360"), Font, new Rectangle(e.Bounds.X + 12, e.Bounds.Y + 32, e.Bounds.Width - 20, 18), Color.FromArgb(150, 170, 190), TextFormatFlags.EndEllipsis);
    }

    private Panel Card() => new() { BackColor = Color.FromArgb(23, 30, 40), Margin = new Padding(0) };
    private Label Label(string text, float size, bool bold, Color color) => new() { Text = text, Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular), ForeColor = color, BackColor = Color.Transparent, AutoEllipsis = true };
    private Button ActionButton(string text) => new() { Text = text, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(42, 88, 130), ForeColor = Color.White, Cursor = Cursors.Hand };
    private string T(string pt, string en) => _english ? en : pt;
    private static string Key(ControllerSnapshot snapshot) => snapshot.SessionId + "/" + snapshot.ControllerId;
    private string SourceLabel(ControllerSnapshot snapshot, string logical)
    {
        if (snapshot.Mapping is not { ValueKind: JsonValueKind.Object } mapping ||
            !mapping.TryGetProperty("buttons", out var buttons) || buttons.ValueKind != JsonValueKind.Object ||
            !buttons.TryGetProperty(logical, out var value) || value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var source)) return T("Padrão", "Default");
        return source switch
        {
            19 => "↑", 20 => "↓", 21 => "←", 22 => "→",
            96 => "A / ✕", 97 => "B / ○", 99 => "X / □", 100 => "Y / △",
            102 => "L1 / LB", 103 => "R1 / RB", 104 => "L2 / LT", 105 => "R2 / RT",
            106 => "L3 / LS", 107 => "R3 / RS", 108 => "Start", 109 => "Select", 110 => "Mode",
            _ => T("Botão ", "Button ") + source
        };
    }

    private string DisplayButton(string name)
    {
        var ds4 = _output.SelectedIndex == 1;
        return name switch
        {
            "south" => ds4 ? "✕" : "A", "east" => ds4 ? "○" : "B", "west" => ds4 ? "□" : "X", "north" => ds4 ? "△" : "Y",
            "l1" => ds4 ? "L1" : "LB", "r1" => ds4 ? "R1" : "RB", "l2" => ds4 ? "L2" : "LT", "r2" => ds4 ? "R2" : "RT",
            "share" => ds4 ? "Share" : "Back", "options" => ds4 ? "Options" : "Start", "l3" => ds4 ? "L3" : "LS", "r3" => ds4 ? "R3" : "RS", "ps" => ds4 ? "PS" : "Guide",
            "dpad_up" => "↑", "dpad_down" => "↓", "dpad_left" => "←", "dpad_right" => "→", _ => name
        };
    }
}
