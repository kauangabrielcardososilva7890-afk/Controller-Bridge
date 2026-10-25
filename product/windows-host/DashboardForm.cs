using System.Text.Json;
using System.Collections.Concurrent;

namespace ControllerBridge.Host;

public sealed class DashboardForm : Form
{
    private readonly bool _english;
    private readonly HostServer _host;
    private readonly Action _stop;
    private readonly bool _wifiEnabled;
    private readonly FlowLayoutPanel _playerTabs = new();
    private readonly ComboBox _physicalInput = new();
    private readonly Dictionary<string, Button> _navigationButtons = new(StringComparer.Ordinal);
    private Panel? _detailsScroll;
    private Control? _connectionSection;
    private Control? _editorSection;
    private Control? _motionSection;
    private readonly Label _connection = new();
    private readonly Label _network = new();
    private readonly Label _selectedName = new();
    private readonly Label _physicalStatus = new();
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
    private int _pendingPhoneConnections = -1;
    private sealed record ControllerChoice(string Key, string Name)
    {
        public override string ToString() => Name;
    }

    public DashboardForm(bool english, HostServer host, string[] localIps, bool wifiEnabled, int wifiPort, string pairingCode, Action stop)
    {
        _english = english;
        _host = host;
        _stop = stop;
        _wifiEnabled = wifiEnabled;
        Text = "Controller Bridge";
        StartPosition = FormStartPosition.CenterScreen;
        try
        {
            if (Environment.ProcessPath is { } executablePath) Icon = Icon.ExtractAssociatedIcon(executablePath) ?? SystemIcons.Application;
        }
        catch (ArgumentException) { }
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        MinimumSize = new Size(980, 600);
        var workArea = Screen.FromPoint(Cursor.Position).WorkingArea;
        Size = new Size(Math.Min(1380, workArea.Width), Math.Min(920, workArea.Height));
        DoubleBuffered = true;
        BackColor = Color.FromArgb(15, 19, 26);
        ForeColor = Color.FromArgb(232, 238, 246);
        Font = new Font("Segoe UI", 9.5f);

        var outer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(18), BackColor = BackColor };
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
        outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(outer);
        outer.Controls.Add(BuildHeader(), 0, 0);

        var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = BackColor, Padding = new Padding(0, 12, 0, 0) };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        outer.Controls.Add(body, 0, 1);
        body.Controls.Add(BuildNavigation(), 0, 0);
        body.Controls.Add(BuildDetails(pairingCode), 1, 0);
        SetActiveNavigation("controls");

        var ipText = localIps.Length == 0
            ? T("Nenhum IP local encontrado — conecte o PC e o celular à mesma rede.", "No local IP found — connect the PC and phone to the same network.")
            : string.Join("   ·   ", localIps);
        _network.Text = wifiEnabled
            ? $"{T("PORTA WI-FI", "WI-FI PORT")}  {wifiPort}     {T("IP DO PC", "PC IP")}  {ipText}\r\n{T("CÓDIGO DE PAREAMENTO", "PAIRING CODE")}  {pairingCode}     {T("Use o mesmo Wi-Fi no celular e no PC.", "Use the same Wi-Fi on the phone and PC.")}"
            : T("MODO USB · conexão por cabo\r\nNo celular, selecione USB. Não precisa digitar IP; autorize a depuração USB se solicitado.", "USB MODE · cable connection\r\nSelect USB on the phone. No IP is needed; authorize USB debugging if prompted.");
        ApplyConnectionCount(host.PhoneConnectionCount);
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
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, BackColor = Color.FromArgb(23, 30, 40), Padding = new Padding(16, 8, 16, 8), Margin = Padding.Empty };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 46));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 54));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 62));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 38));
        var title = Label("CONTROLLER BRIDGE", 17, true, Color.White); title.Dock = DockStyle.Fill;
        var version = typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "";
        var subtitle = Label(T($"Seu controle. Do celular para o jogo.  ·  v{version}", $"Your controller. From phone to game.  ·  v{version}"), 9, false, Color.FromArgb(144, 159, 177)); subtitle.Dock = DockStyle.Fill;
        _connection.Text = T("●  INICIANDO · abrindo a porta para o celular", "●  STARTING · opening the phone connection port");
        _connection.ForeColor = Color.FromArgb(249, 190, 74);
        _connection.Font = new Font("Segoe UI Semibold", 10);
        _connection.Dock = DockStyle.Fill;
        _connection.AutoEllipsis = true;
        _connection.TextAlign = ContentAlignment.MiddleRight;
        panel.Controls.Add(title, 0, 0); panel.Controls.Add(subtitle, 0, 1); panel.Controls.Add(_connection, 1, 0); panel.SetRowSpan(_connection, 2);
        return panel;
    }

    private Control BuildNavigation()
    {
        var panel = Card(); panel.Dock = DockStyle.Fill; panel.Padding = new Padding(10, 14, 10, 10);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, BackColor = panel.BackColor };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        for (var row = 1; row < 5; row++) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var heading = Label(T("NAVEGAÇÃO", "NAVIGATION"), 9, true, Color.FromArgb(142, 158, 177)); heading.Dock = DockStyle.Fill;
        layout.Controls.Add(heading, 0, 0);
        var sections = new[]
        {
            (Id: "general", Icon: "⚙", Pt: "Geral", En: "General"),
            (Id: "connection", Icon: "↔", Pt: "Conexão", En: "Connection"),
            (Id: "controls", Icon: "🎮", Pt: "Controles", En: "Controls"),
            (Id: "sensors", Icon: "◉", Pt: "Sensores", En: "Sensors")
        };
        for (var index = 0; index < sections.Length; index++)
        {
            var section = sections[index];
            var button = ActionButton($"{section.Icon}   {T(section.Pt, section.En)}");
            button.Dock = DockStyle.Fill; button.Margin = new Padding(0, 3, 0, 3); button.TextAlign = ContentAlignment.MiddleLeft;
            button.Padding = new Padding(10, 0, 4, 0); button.Font = new Font("Segoe UI Semibold", 10f);
            button.FlatAppearance.BorderSize = 0;
            button.AccessibleName = T(section.Pt, section.En);
            button.Click += (_, _) => { SetActiveNavigation(section.Id); NavigateTo(section.Id); };
            _navigationButtons[section.Id] = button;
            layout.Controls.Add(button, 0, index + 1);
        }
        panel.Controls.Add(layout);
        return panel;
    }

    private void SetActiveNavigation(string section)
    {
        foreach (var (id, button) in _navigationButtons)
        {
            var active = id == section;
            button.BackColor = active ? Color.FromArgb(31, 82, 122) : Color.FromArgb(23, 30, 40);
            button.ForeColor = active ? Color.FromArgb(131, 202, 255) : Color.FromArgb(204, 216, 229);
            button.FlatAppearance.MouseOverBackColor = Color.FromArgb(39, 59, 78);
        }
    }

    private void NavigateTo(string section)
    {
        if (_detailsScroll == null) return;
        var target = section switch
        {
            "connection" => _connectionSection,
            "controls" => _editorSection,
            "sensors" => _motionSection,
            _ => null
        };
        if (target == null) _detailsScroll.AutoScrollPosition = Point.Empty;
        else _detailsScroll.ScrollControlIntoView(target);
    }

    private Control BuildPlayerTabs()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(2, 0, 2, 0) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 19));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var heading = Label(T("JOGADORES", "PLAYERS"), 9, true, Color.FromArgb(142, 158, 177)); heading.Dock = DockStyle.Fill;
        _playerTabs.Dock = DockStyle.Fill;
        _playerTabs.FlowDirection = FlowDirection.LeftToRight;
        _playerTabs.WrapContents = false;
        _playerTabs.AutoScroll = true;
        _playerTabs.AccessibleName = T("Abas dos jogadores", "Player tabs");
        layout.Controls.Add(heading, 0, 0); layout.Controls.Add(_playerTabs, 0, 1);
        return layout;
    }

    private Control BuildDetails(string pairingCode)
    {
        // Scroll the complete editor on short screens, keeping every mapping and motion row reachable.
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Margin = new Padding(0), Padding = new Padding(10, 0, 0, 0) };
        _detailsScroll = scroll;
        var layout = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 1, RowCount = 3, BackColor = BackColor, Margin = Padding.Empty };
        scroll.Controls.Add(layout);
        void SizeDetails()
        {
            var minimumHeight = (int)Math.Round(780 * DeviceDpi / 96f);
            layout.Height = Math.Max(minimumHeight, scroll.ClientSize.Height);
        }
        scroll.Resize += (_, _) => SizeDetails();
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 90));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        var info = Card(); info.Dock = DockStyle.Fill; info.Padding = new Padding(14, 8, 14, 8); _connectionSection = info;
        var netHeader = Label(T("CONECTAR O CELULAR", "CONNECT YOUR PHONE"), 9.5f, true, Color.FromArgb(105, 183, 255));
        netHeader.Dock = DockStyle.Top; netHeader.Height = 21;
        _network.AutoSize = false; _network.Dock = DockStyle.Fill; _network.Font = new Font("Segoe UI", 9); _network.ForeColor = Color.FromArgb(218, 226, 237); _network.Padding = new Padding(0, 6, 0, 0);
        var copy = ActionButton(T("Copiar código", "Copy code")); copy.Dock = DockStyle.Right; copy.Width = 116;
        copy.Visible = _wifiEnabled;
        copy.Click += (_, _) => { Clipboard.SetText(pairingCode); _mappingHint.Text = T("Código de pareamento copiado.", "Pairing code copied."); };
        info.Controls.Add(_network); info.Controls.Add(copy); info.Controls.Add(netHeader);
        layout.Controls.Add(info, 0, 0);

        var editor = Card(); editor.Dock = DockStyle.Fill; editor.Padding = new Padding(12); editor.Margin = new Padding(0, 10, 0, 0); _editorSection = editor;
        var rows = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, BackColor = editor.BackColor };
        rows.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
        rows.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
        rows.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        rows.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));
        rows.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        editor.Controls.Add(rows);

        var tabs = BuildPlayerTabs(); tabs.Margin = new Padding(4, 0, 4, 2); rows.Controls.Add(tabs, 0, 0);

        var settings = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3 };
        settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        settings.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        settings.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        settings.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
        _selectedName.Text = T("CONTROLE FÍSICO", "PHYSICAL CONTROLLER");
        _selectedName.Font = new Font("Segoe UI Semibold", 9); _selectedName.ForeColor = Color.FromArgb(142, 158, 177); _selectedName.Dock = DockStyle.Fill; _selectedName.AutoEllipsis = true;
        settings.Controls.Add(_selectedName, 0, 0);
        _physicalInput.DropDownStyle = ComboBoxStyle.DropDownList; _physicalInput.Dock = DockStyle.Fill; _physicalInput.BackColor = Color.FromArgb(31, 40, 52); _physicalInput.ForeColor = Color.White; _physicalInput.FlatStyle = FlatStyle.Flat; _physicalInput.AccessibleName = T("Controle físico", "Physical controller");
        _physicalInput.DrawMode = DrawMode.OwnerDrawFixed; _physicalInput.ItemHeight = 22;
        _physicalInput.DrawItem += (_, e) => DrawComboItem(_physicalInput, e);
        _physicalInput.SelectedIndexChanged += (_, _) =>
        {
            if (!_updating && _physicalInput.SelectedItem is ControllerChoice choice) SelectControllerTab(choice.Key);
        };
        if (_items.Count == 0) { _physicalInput.Items.Add(T("Nenhum controle conectado", "No controller connected")); _physicalInput.SelectedIndex = 0; }
        settings.Controls.Add(_physicalInput, 0, 1);
        var outputLabel = Label(T("SAÍDA VIRTUAL PARA O JOGO", "VIRTUAL OUTPUT TO THE GAME"), 9, true, Color.FromArgb(142, 158, 177)); outputLabel.Dock = DockStyle.Fill;
        settings.Controls.Add(outputLabel, 1, 0);
        _output.DropDownStyle = ComboBoxStyle.DropDownList; _output.Items.AddRange(["Xbox 360 (XInput)", "DualShock 4"]); _output.Dock = DockStyle.Fill; _output.BackColor = Color.FromArgb(31, 40, 52); _output.ForeColor = Color.White; _output.FlatStyle = FlatStyle.Flat; _output.AccessibleName = T("Saída virtual para o jogo", "Virtual output to the game");
        _output.DrawMode = DrawMode.OwnerDrawFixed;
        _output.ItemHeight = 22;
        _output.DrawItem += (_, e) => DrawComboItem(_output, e);
        _output.SelectedIndexChanged += (_, _) => ChangeOutput(); settings.Controls.Add(_output, 1, 1);
        _physicalStatus.Font = new Font("Segoe UI", 8.5f); _physicalStatus.ForeColor = Color.FromArgb(144, 159, 177); _physicalStatus.Dock = DockStyle.Fill; _physicalStatus.AutoEllipsis = true;
        settings.Controls.Add(_physicalStatus, 0, 2); settings.SetColumnSpan(_physicalStatus, 2);
        rows.Controls.Add(settings, 0, 1);

        var map = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 2 };
        map.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 162));
        map.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        map.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 162));
        map.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        map.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        map.Controls.Add(BuildMappingGroups((T("OMBRO ESQUERDO", "LEFT SHOULDER"), new[] { "l1", "l2" }), (T("DIRECIONAL", "DIRECTION PAD"), new[] { "dpad_up", "dpad_down", "dpad_left", "dpad_right" }), (T("ANALÓGICO ESQUERDO", "LEFT STICK"), new[] { "l3" })), 0, 0);
        map.Controls.Add(BuildMappingGroups((T("OMBRO DIREITO", "RIGHT SHOULDER"), new[] { "r1", "r2" }), (T("BOTÕES", "FACE BUTTONS"), new[] { "north", "west", "east", "south" }), (T("ANALÓGICO DIREITO", "RIGHT STICK"), new[] { "r3" })), 2, 0);
        map.SetRowSpan(map.GetControlFromPosition(0, 0)!, 2);
        map.SetRowSpan(map.GetControlFromPosition(2, 0)!, 2);
        var center = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        center.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); center.RowStyles.Add(new RowStyle(SizeType.Absolute, 22)); center.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        _diagram.Dock = DockStyle.Fill; _diagram.AccessibleName = T("Resposta ao vivo da saída virtual", "Live virtual output response"); _diagram.LogicalButtonClicked += RemapButton; center.Controls.Add(_diagram, 0, 0);
        var virtualHint = Label(T("CONTROLE VIRTUAL · resposta ao vivo", "VIRTUAL CONTROLLER · live response"), 8.5f, true, Color.FromArgb(142, 158, 177)); virtualHint.Dock = DockStyle.Fill; virtualHint.TextAlign = ContentAlignment.MiddleCenter;
        center.Controls.Add(virtualHint, 0, 1);
        _axes.Dock = DockStyle.Fill; _axes.TextAlign = ContentAlignment.MiddleCenter; _axes.Font = new Font("Segoe UI", 8.5f); _axes.ForeColor = Color.FromArgb(153, 168, 185);
        center.Controls.Add(_axes, 0, 2); map.Controls.Add(center, 1, 0);
        var systemButtons = BuildMappingGroups((T("SISTEMA", "SYSTEM"), new[] { "share", "ps", "options" }));
        map.Controls.Add(systemButtons, 1, 1);
        rows.Controls.Add(map, 0, 2);

        var motion = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(12, 8, 8, 6), BackColor = Color.FromArgb(18, 25, 34), Margin = new Padding(0, 6, 0, 0) }; _motionSection = motion;
        motion.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170)); motion.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _gyro.Text = T("Giroscópio DS4", "DS4 gyro"); _gyro.Dock = DockStyle.Fill; _gyro.ForeColor = Color.FromArgb(222, 231, 242); _gyro.CheckedChanged += (_, _) => ChangeOutput(); motion.Controls.Add(_gyro, 0, 0);
        _motion.Dock = DockStyle.Fill; _motion.ForeColor = Color.FromArgb(153, 168, 185); _motion.Font = new Font("Segoe UI", 8.5f); motion.Controls.Add(_motion, 1, 0); rows.Controls.Add(motion, 0, 3);
        _mappingHint.Dock = DockStyle.Fill; _mappingHint.ForeColor = Color.FromArgb(159, 194, 224); _mappingHint.Padding = new Padding(0, 8, 0, 0); rows.Controls.Add(_mappingHint, 0, 4);
        layout.Controls.Add(editor, 0, 1);
        var tip = Label(T("Clique em um campo e pressione o botão no controle físico. Mantenha esta janela aberta enquanto joga.", "Click a field, then press a button on the physical controller. Keep this window open while playing."), 9, false, Color.FromArgb(161, 174, 190)); tip.Dock = DockStyle.Fill; tip.Padding = new Padding(4, 12, 4, 0); layout.Controls.Add(tip, 0, 2);
        SizeDetails();
        return scroll;
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
        var stack = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = false, Margin = new Padding(0) };
        foreach (var group in groups)
        {
            var box = new TableLayoutPanel { ColumnCount = 2, RowCount = group.Buttons.Length + 1, AutoSize = false, Margin = new Padding(0, 0, 0, 7), Padding = new Padding(3), Width = 154, Height = 25 + group.Buttons.Length * 30, BackColor = Color.FromArgb(26, 34, 45), BorderStyle = BorderStyle.FixedSingle };
            box.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48)); box.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            box.RowStyles.Add(new RowStyle(SizeType.Absolute, 19));
            var title = Label(group.Title, 9, true, Color.FromArgb(142, 158, 177)); title.Dock = DockStyle.Fill; title.Height = 23; box.Controls.Add(title, 0, 0); box.SetColumnSpan(title, 2);
            foreach (var logical in group.Buttons)
            {
                var row = Array.IndexOf(group.Buttons, logical) + 1;
                box.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
                var caption = Label(logical, 9, true, Color.FromArgb(222, 231, 242)); caption.Dock = DockStyle.Fill; caption.TextAlign = ContentAlignment.MiddleLeft; _mappingLabels[logical] = caption;
                var field = ActionButton("—"); field.Height = 27; field.Dock = DockStyle.Fill; field.Margin = new Padding(0, 1, 0, 1); field.Font = new Font("Segoe UI", 8.5f); field.FlatAppearance.BorderColor = Color.FromArgb(64, 83, 103); field.Click += (_, _) => RemapButton(logical); _mappingFields[logical] = field;
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
        var phoneConnections = Interlocked.Exchange(ref _pendingPhoneConnections, -1);
        if (phoneConnections >= 0) ApplyConnectionCount(phoneConnections);
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
    }
    private void OnConnectionCountChanged(int count) => Interlocked.Exchange(ref _pendingPhoneConnections, count);

    private void ApplyConnectionCount(int count)
    {
        if (IsDisposed) return;
        _connection.Text = count > 0
            ? T($"●  CONECTADO · {count} {(count == 1 ? "celular" : "celulares")}", $"●  CONNECTED · {count} {(count == 1 ? "phone" : "phones")}")
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
        _selectedKey = old != null && _items.ContainsKey(old) ? old : keys.FirstOrDefault();
        if (old != _selectedKey) _mappingHint.Text = "";
        _updating = true;
        _playerTabs.SuspendLayout();
        _playerTabs.Controls.Clear();
        _physicalInput.BeginUpdate();
        _physicalInput.Items.Clear();
        foreach (var key in keys) _physicalInput.Items.Add(new ControllerChoice(key, _items[key].Name));
        if (keys.Length == 0) _physicalInput.Items.Add(T("Nenhum controle conectado", "No controller connected"));
        _physicalInput.SelectedItem = _selectedKey == null ? null : _physicalInput.Items.Cast<ControllerChoice>().FirstOrDefault(choice => choice.Key == _selectedKey);
        if (keys.Length == 0) _physicalInput.SelectedIndex = 0;
        _physicalInput.EndUpdate();
        for (var index = 0; index < Math.Max(4, keys.Length); index++)
        {
            var connected = index < keys.Length;
            var key = connected ? keys[index] : null;
            var snapshot = key == null ? null : _items[key];
            var selected = key != null && key == _selectedKey;
            if (!connected)
            {
                var placeholder = new Panel { Size = new Size(164, 42), Margin = new Padding(0, 0, 8, 0), BackColor = Color.FromArgb(20, 27, 36), BorderStyle = BorderStyle.FixedSingle, AccessibleName = $"{T("Jogador", "Player")} {index + 1}: {T("aguardando controle", "waiting for controller")}" };
                var title = Label($"{T("JOGADOR", "PLAYER")} {index + 1}", 8.5f, true, Color.FromArgb(135, 151, 169)); title.Dock = DockStyle.Top; title.Height = 19; title.Padding = new Padding(6, 2, 0, 0);
                var subtitle = Label(T("Aguardando controle", "Waiting for controller"), 7.5f, false, Color.FromArgb(105, 119, 136)); subtitle.Dock = DockStyle.Fill; subtitle.Padding = new Padding(6, 0, 0, 0);
                placeholder.Controls.Add(subtitle); placeholder.Controls.Add(title); _playerTabs.Controls.Add(placeholder);
                continue;
            }
            var tabText = $"{T("JOGADOR", "PLAYER")} {index + 1}\r\n{snapshot!.Name}";
            var tab = ActionButton(tabText);
            tab.Size = new Size(164, 42); tab.Margin = new Padding(0, 0, 8, 0); tab.TextAlign = ContentAlignment.MiddleLeft; tab.Padding = new Padding(9, 0, 4, 0);
            tab.Font = new Font("Segoe UI", 8.5f, selected ? FontStyle.Bold : FontStyle.Regular); tab.AutoEllipsis = true;
            tab.Enabled = connected;
            tab.BackColor = selected ? Color.FromArgb(31, 91, 139) : Color.FromArgb(23, 30, 40);
            tab.ForeColor = connected ? Color.FromArgb(225, 235, 246) : Color.FromArgb(104, 117, 132);
            tab.FlatAppearance.BorderColor = selected ? Color.FromArgb(46, 160, 236) : Color.FromArgb(57, 74, 93);
            tab.FlatAppearance.BorderSize = selected ? 2 : 1;
            tab.AccessibleName = connected
                ? $"{T("Jogador", "Player")} {index + 1}: {snapshot!.Name}, {T("saída", "output")} {(snapshot.Output == "ds4" ? "DualShock 4" : "Xbox 360")}"
                : $"{T("Jogador", "Player")} {index + 1}: {T("aguardando controle", "waiting for controller")}";
            if (key != null) tab.Click += (_, _) => SelectControllerTab(key);
            _playerTabs.Controls.Add(tab);
        }
        _playerTabs.ResumeLayout();
        _updating = false;
        UpdateSelection(_selectedKey == null ? null : _items[_selectedKey]);
    }

    private void SelectControllerTab(string key)
    {
        if (_updating || !_items.ContainsKey(key)) return;
        _selectedKey = key; _mappingHint.Text = ""; RebuildControllerList();
    }

    private void UpdateSelection(ControllerSnapshot? snapshot)
    {
        _updating = true;
        _selectedName.Text = snapshot == null ? T("CONTROLE FÍSICO", "PHYSICAL CONTROLLER") : T("CONTROLE FÍSICO · ", "PHYSICAL CONTROLLER · ") + snapshot.Name;
        _physicalInput.Enabled = snapshot != null;
        if (snapshot != null) _physicalInput.SelectedItem = _physicalInput.Items.Cast<ControllerChoice>().FirstOrDefault(choice => choice.Key == Key(snapshot));
        _output.Enabled = snapshot != null; _gyro.Enabled = snapshot?.Output == "ds4";
        _output.SelectedIndex = snapshot?.Output == "ds4" ? 1 : 0; _gyro.Checked = snapshot?.GyroEnabled ?? false;
        var fresh = snapshot != null && _lastInput.TryGetValue(Key(snapshot), out var last) && Environment.TickCount64 - last < 1000;
        _physicalStatus.Text = snapshot == null ? T("Nenhum controle conectado. Conecte um controle ao celular.", "No controller connected. Connect a controller to the phone.") : fresh ? T("● Controle conectado · recebendo dados", "● Controller connected · receiving input") : T("● Controle conectado · aguardando dados", "● Controller connected · waiting for input");
        _physicalStatus.ForeColor = fresh ? Color.FromArgb(92, 218, 154) : Color.FromArgb(144, 159, 177);
        var visible = snapshot == null || fresh ? snapshot : snapshot with { State = GamepadState.Neutral() };
        _diagram.Enabled = snapshot != null;
        _diagram.CaptureButton = snapshot != null && _captures.TryGetValue(Key(snapshot), out var diagramCapture) ? diagramCapture.Button : null;
        _diagram.SetController(visible);
        foreach (var pair in _mappingFields)
        {
            var pending = snapshot != null && _captures.TryGetValue(Key(snapshot), out var capture) && capture.Button == pair.Key;
            pair.Value.Enabled = snapshot != null;
            pair.Value.Text = pending ? T("Pressione…", "Press…") : snapshot == null ? "—" : SourceLabel(snapshot, pair.Key);
            var active = visible?.State.Button(pair.Key) == true || (pair.Key == "l2" && visible?.State.Axis("lt") > 0.1f) || (pair.Key == "r2" && visible?.State.Axis("rt") > 0.1f);
            pair.Value.BackColor = pending ? Color.FromArgb(130, 88, 30) : active ? Color.FromArgb(39, 126, 185) : Color.FromArgb(31, 43, 56);
            pair.Value.FlatAppearance.BorderColor = pending ? Color.FromArgb(249, 190, 74) : active ? Color.FromArgb(105, 183, 255) : Color.FromArgb(57, 74, 93);
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
            : $"{T("Movimento · recebendo", "Motion · receiving")}\r\nGyro (rad/s)  X {motion.GyroRadiansPerSecond.X:0.00}   Y {motion.GyroRadiansPerSecond.Y:0.00}   Z {motion.GyroRadiansPerSecond.Z:0.00}\r\nAccel (m/s²)  X {motion.AccelerationMetersPerSecondSquared.X:0.00}   Y {motion.AccelerationMetersPerSecondSquared.Y:0.00}   Z {motion.AccelerationMetersPerSecondSquared.Z:0.00}";
        if (snapshot?.Output == "ds4" && snapshot.GyroEnabled)
        {
            var provider = _host.GetMotionProviderStatus();
            var endpoint = provider.Endpoint?.ToString() ?? $"127.0.0.1:{DsuMotionServer.DefaultPort}";
            var providerText = provider.Running
                ? T("UDP local ativo · ", "Local UDP active · ") + endpoint
                : provider.Error?.Contains("in use", StringComparison.OrdinalIgnoreCase) == true
                    ? T("UDP 26760 ocupado · feche outro programa de movimento", "UDP 26760 is busy · close the other motion app")
                    : !string.IsNullOrEmpty(provider.Error) ? T("Falha no movimento: ", "Motion error: ") + provider.Error
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
        catch (Exception error) { UpdateSelection(snapshot); _mappingHint.Text = error.GetBaseException().Message; }
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

    private Panel Card() => new() { BackColor = Color.FromArgb(23, 30, 40), Margin = new Padding(0) };
    private Label Label(string text, float size, bool bold, Color color) => new() { Text = text, Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular), ForeColor = color, BackColor = Color.Transparent, AutoEllipsis = true };
    private Button ActionButton(string text)
    {
        var button = new Button { Text = text, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(31, 43, 56), ForeColor = Color.White, Cursor = Cursors.Hand, UseVisualStyleBackColor = false };
        button.FlatAppearance.BorderColor = Color.FromArgb(57, 74, 93);
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(48, 69, 91);
        button.FlatAppearance.MouseDownBackColor = Color.FromArgb(39, 126, 185);
        return button;
    }
    private static void DrawComboItem(ComboBox combo, DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        var selected = (e.State & DrawItemState.Selected) != 0;
        using var background = new SolidBrush(selected ? Color.FromArgb(43, 62, 82) : Color.FromArgb(31, 40, 52));
        e.Graphics.FillRectangle(background, e.Bounds);
        TextRenderer.DrawText(e.Graphics, combo.Items[e.Index]?.ToString() ?? "", e.Font ?? combo.Font, Rectangle.Inflate(e.Bounds, -7, 0), combo.Enabled ? Color.White : Color.FromArgb(144, 159, 177), TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        e.DrawFocusRectangle();
    }
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
