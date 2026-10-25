using System.Text.Json;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace ControllerBridge.Host;

public sealed class DashboardForm : Form
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int valueSize);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        var dark = 1;
        _ = DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int));
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            var caption = ColorTranslator.ToWin32(Color.FromArgb(30, 37, 40));
            _ = DwmSetWindowAttribute(Handle, 35, ref caption, sizeof(int));
        }
    }
    private readonly bool _english;
    private readonly HostServer _host;
    private readonly Action _stop;
    private readonly bool _wifiEnabled;
    private readonly FlowLayoutPanel _playerTabs = new();
    private readonly ComboBox _physicalInput = new DashboardComboBox();
    private readonly Dictionary<string, Button> _navigationButtons = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Control> _pages = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Label> _axisFields = new(StringComparer.Ordinal);
    private readonly Label _sensorDetails = new();
    private readonly Label _connection = new();
    private readonly Label _network = new();
    private readonly Label _selectedName = new();
    private readonly Label _physicalStatus = new();
    private readonly Label _mappingHint = new();
    private readonly ComboBox _output = new DashboardComboBox();
    private readonly CheckBox _gyro = new();
    private readonly ControllerDiagram _diagram = new();
    private readonly Label _motion = new();
    private readonly Label _axes = new();
    private readonly Label _rightTriggerAxis = new();
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
        Text = T("Configurar controles", "Configure controllers") + " · Controller Bridge";
        StartPosition = FormStartPosition.CenterScreen;
        try
        {
            if (Environment.ProcessPath is { } executablePath) Icon = Icon.ExtractAssociatedIcon(executablePath) ?? SystemIcons.Application;
        }
        catch (ArgumentException) { }
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        MinimumSize = new Size(1000, 600);
        var workArea = Screen.FromPoint(Cursor.Position).WorkingArea;
        Size = new Size(Math.Min(1540, workArea.Width), Math.Min(1000, workArea.Height));
        DoubleBuffered = true;
        BackColor = Color.FromArgb(25, 30, 32);
        ForeColor = Color.FromArgb(230, 235, 238);
        Font = new Font("Segoe UI", 10f);

        var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = BackColor, Margin = Padding.Empty };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Controls.Add(body);
        body.Controls.Add(BuildNavigation(), 0, 0);
        body.Controls.Add(BuildDetails(pairingCode), 1, 0);
        NavigateTo("controls");

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

    private Control BuildNavigation()
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(22, 27, 29), Margin = Padding.Empty, Padding = new Padding(10, 24, 0, 0) };
        panel.Paint += (_, e) => { using var border = new Pen(Color.FromArgb(51, 58, 61)); e.Graphics.DrawLine(border, panel.Width - 1, 0, panel.Width - 1, panel.Height); };
        var sections = new[]
        {
            (Id: "general", Icon: "⚙", Pt: "Geral", En: "General"),
            (Id: "connection", Icon: "↔", Pt: "Conexão", En: "Connection"),
            (Id: "controls", Icon: "🎮", Pt: "Controles", En: "Controls"),
            (Id: "sensors", Icon: "◉", Pt: "Sensores", En: "Sensors")
        };
        for (var index = sections.Length - 1; index >= 0; index--)
        {
            var section = sections[index];
            var button = ActionButton($"{section.Icon}   {T(section.Pt, section.En)}");
            button.Dock = DockStyle.Top; button.Height = 58; button.TextAlign = ContentAlignment.MiddleLeft;
            button.Padding = new Padding(12, 0, 0, 0); button.Font = new Font("Segoe UI", 12);
            button.FlatAppearance.BorderSize = 0;
            button.AccessibleName = T(section.Pt, section.En);
            button.Click += (_, _) => NavigateTo(section.Id);
            _navigationButtons[section.Id] = button;
            panel.Controls.Add(button);
        }
        var version = typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "";
        var footer = Label("Controller Bridge\r\nv" + version, 9, false, Color.FromArgb(125, 141, 149));
        footer.Dock = DockStyle.Bottom; footer.Height = 58; footer.Padding = new Padding(12, 0, 0, 12);
        panel.Controls.Add(footer);
        return panel;
    }

    private void NavigateTo(string section)
    {
        foreach (var (id, page) in _pages) page.Visible = id == section;
        if (_pages.TryGetValue(section, out var selected)) selected.BringToFront();
        foreach (var (id, button) in _navigationButtons)
        {
            var active = id == section;
            button.BackColor = active ? Color.FromArgb(30, 53, 70) : Color.FromArgb(22, 27, 29);
            button.ForeColor = active ? Color.FromArgb(121, 199, 255) : Color.FromArgb(183, 194, 201);
            button.FlatAppearance.MouseOverBackColor = Color.FromArgb(37, 46, 51);
        }
    }

    private Control BuildDetails(string pairingCode)
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(18, 10, 18, 8), Margin = Padding.Empty, BackColor = BackColor };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        _playerTabs.Dock = DockStyle.Fill; _playerTabs.Margin = Padding.Empty;
        _playerTabs.WrapContents = false; _playerTabs.AutoScroll = true;
        _playerTabs.AccessibleName = T("Abas dos jogadores", "Player tabs");
        layout.Controls.Add(_playerTabs, 0, 0);
        var deck = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        layout.Controls.Add(deck, 0, 1);
        _pages["controls"] = BuildControlsPage();
        _pages["connection"] = BuildConnectionPage(pairingCode);
        _pages["general"] = BuildGeneralPage();
        _pages["sensors"] = BuildSensorsPage();
        foreach (var page in _pages.Values) { page.Dock = DockStyle.Fill; deck.Controls.Add(page); }

        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty, Padding = new Padding(0, 10, 0, 0) };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 172));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 152));
        var refresh = ActionButton(T("Atualizar controles", "Refresh controllers")); refresh.Dock = DockStyle.Fill; refresh.Margin = new Padding(0, 0, 8, 0); refresh.Click += (_, _) => RefreshControllerList();
        var saved = Label(T("Alterações salvas e sincronizadas automaticamente", "Changes are saved and synced automatically"), 9, false, Color.FromArgb(151, 165, 171)); saved.Dock = DockStyle.Fill; saved.TextAlign = ContentAlignment.MiddleCenter;
        var minimize = ActionButton(T("Minimizar", "Minimize")); minimize.Dock = DockStyle.Fill; minimize.Margin = new Padding(8, 0, 0, 0); minimize.BackColor = Color.FromArgb(31, 78, 127); minimize.FlatAppearance.BorderColor = Color.FromArgb(60, 148, 235); minimize.Click += (_, _) => WindowState = FormWindowState.Minimized;
        footer.Controls.Add(refresh, 0, 0); footer.Controls.Add(saved, 1, 0); footer.Controls.Add(minimize, 2, 0);
        layout.Controls.Add(footer, 0, 2);
        return layout;
    }

    private Control BuildControlsPage()
    {
        var viewport = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Margin = Padding.Empty, BackColor = BackColor };
        var page = Card(); page.Dock = DockStyle.Fill; page.Padding = new Padding(18, 8, 18, 4);
        page.Dock = DockStyle.None; viewport.Controls.Add(page);
        var rows = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Margin = Padding.Empty, BackColor = page.BackColor };
        rows.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
        rows.RowStyles.Add(new RowStyle(SizeType.Absolute, 126));
        rows.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        rows.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
        rows.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
        page.Controls.Add(rows);

        var settings = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 2, Margin = Padding.Empty };
        settings.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132));
        settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 53));
        settings.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 122));
        settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 47));
        settings.RowStyles.Add(new RowStyle(SizeType.Absolute, 38)); settings.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _selectedName.Text = T("Controle físico:", "Physical controller:"); _selectedName.Dock = DockStyle.Fill; _selectedName.TextAlign = ContentAlignment.MiddleLeft;
        settings.Controls.Add(_selectedName, 0, 0);
        ConfigureCombo(_physicalInput, T("Controle físico", "Physical controller"));
        _physicalInput.SelectedIndexChanged += (_, _) => { if (!_updating && _physicalInput.SelectedItem is ControllerChoice choice) SelectControllerTab(choice.Key); };
        _physicalInput.Items.Add(T("Nenhum controle conectado", "No controller connected")); _physicalInput.SelectedIndex = 0;
        settings.Controls.Add(_physicalInput, 1, 0);
        var outputLabel = Label(T("Saída virtual:", "Virtual output:"), 10, false, ForeColor); outputLabel.Dock = DockStyle.Fill; outputLabel.Padding = new Padding(8, 0, 0, 0); outputLabel.TextAlign = ContentAlignment.MiddleLeft; settings.Controls.Add(outputLabel, 2, 0);
        ConfigureCombo(_output, T("Saída virtual para o jogo", "Virtual output to the game")); _output.Items.AddRange(["Xbox 360", "DualShock 4"]); _output.SelectionChangeCommitted += (_, _) => ChangeOutput(); settings.Controls.Add(_output, 3, 0);
        _physicalStatus.Dock = DockStyle.Fill; _physicalStatus.Font = new Font("Segoe UI", 8.5f); _physicalStatus.ForeColor = Color.FromArgb(150, 164, 171); _physicalStatus.TextAlign = ContentAlignment.MiddleLeft;
        settings.Controls.Add(_physicalStatus, 1, 1); settings.SetColumnSpan(_physicalStatus, 3);
        rows.Controls.Add(settings, 0, 0);

        var shoulders = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = new Padding(14, 2, 14, 6) };
        shoulders.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f)); shoulders.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34f)); shoulders.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
        shoulders.Controls.Add(BuildMappingGroup(T("Gatilhos superiores", "Left shoulder"), ["l2", "l1"]), 0, 0);
        shoulders.Controls.Add(BuildMappingGroup(T("Botões centrais", "System buttons"), ["share", "options", "ps"]), 1, 0);
        shoulders.Controls.Add(BuildMappingGroup(T("Gatilhos superiores", "Right shoulder"), ["r2", "r1"]), 2, 0);
        foreach (Control group in shoulders.Controls) group.Margin = new Padding(10, 0, 10, 0);
        rows.Controls.Add(shoulders, 0, 1);

        var map = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty };
        map.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25)); map.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); map.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        var left = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 54)); left.RowStyles.Add(new RowStyle(SizeType.Percent, 46));
        left.Controls.Add(BuildMappingGroup(T("Stick esquerdo", "Left stick"), ["l3"], "lx", "ly"), 0, 0);
        left.Controls.Add(BuildMappingGroup(T("Direcional (D-Pad)", "Direction pad (D-Pad)"), ["dpad_up", "dpad_down", "dpad_left", "dpad_right"]), 0, 1);
        map.Controls.Add(left, 0, 0);
        var center = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0) };
        center.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); center.RowStyles.Add(new RowStyle(SizeType.Absolute, 29));
        _diagram.Dock = DockStyle.Fill; _diagram.BackColor = BackColor; _diagram.Margin = Padding.Empty; _diagram.AccessibleName = T("Resposta ao vivo da saída virtual", "Live virtual output response"); _diagram.LogicalButtonClicked += RemapButton;
        center.Controls.Add(_diagram, 0, 0);
        var triggerReadings = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty, Padding = new Padding(12, 0, 12, 0) };
        triggerReadings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); triggerReadings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        _axes.Dock = DockStyle.Fill; _axes.TextAlign = ContentAlignment.MiddleLeft; _axes.Font = new Font("Segoe UI", 8.5f); _axes.ForeColor = Color.FromArgb(137, 154, 163);
        _rightTriggerAxis.Dock = DockStyle.Fill; _rightTriggerAxis.TextAlign = ContentAlignment.MiddleRight; _rightTriggerAxis.Font = _axes.Font; _rightTriggerAxis.ForeColor = _axes.ForeColor;
        triggerReadings.Controls.Add(_axes, 0, 0); triggerReadings.Controls.Add(_rightTriggerAxis, 1, 0);
        center.Controls.Add(triggerReadings, 0, 1); map.Controls.Add(center, 1, 0);
        var right = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 46)); right.RowStyles.Add(new RowStyle(SizeType.Percent, 54));
        right.Controls.Add(BuildMappingGroup(T("Botões de ação", "Action buttons"), ["south", "east", "west", "north"]), 0, 0);
        right.Controls.Add(BuildMappingGroup(T("Stick direito", "Right stick"), ["r3"], "rx", "ry"), 0, 1);
        map.Controls.Add(right, 2, 0); rows.Controls.Add(map, 0, 2);

        var motion = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = new Padding(0, 8, 0, 0), Padding = new Padding(0, 9, 0, 0) };
        motion.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 226)); motion.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 182)); motion.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        motion.Paint += (_, e) => { using var border = new Pen(Color.FromArgb(54, 63, 66)); e.Graphics.DrawLine(border, 0, 0, motion.Width, 0); };
        _gyro.Text = T("Movimento / Giroscópio", "Motion / Gyroscope"); _gyro.Dock = DockStyle.Fill; _gyro.CheckedChanged += (_, _) => ChangeOutput(); motion.Controls.Add(_gyro, 0, 0);
        var configure = ActionButton(T("Configurar sensor", "Configure sensor")); configure.Dock = DockStyle.Fill; configure.Margin = new Padding(0, 0, 12, 0); configure.Click += (_, _) => NavigateTo("sensors"); motion.Controls.Add(configure, 1, 0);
        _motion.Dock = DockStyle.Fill; _motion.TextAlign = ContentAlignment.MiddleLeft; _motion.Font = new Font("Segoe UI", 9); _motion.ForeColor = Color.FromArgb(163, 177, 184); motion.Controls.Add(_motion, 2, 0);
        rows.Controls.Add(motion, 0, 3);
        _mappingHint.Dock = DockStyle.Fill; _mappingHint.Font = new Font("Segoe UI", 8.5f); _mappingHint.TextAlign = ContentAlignment.MiddleLeft; _mappingHint.ForeColor = Color.FromArgb(148, 173, 188); rows.Controls.Add(_mappingHint, 0, 4);
        void FitEditor()
        {
            var scale = DeviceDpi / 96f;
            var compact = viewport.ClientSize.Height < 650 * scale;
            var minimumWidth = (int)Math.Round(880 * scale);
            var minimumHeight = (int)Math.Round(478 * scale);
            var needsVerticalScroll = viewport.ClientSize.Height < minimumHeight;
            page.Size = new Size(Math.Max(minimumWidth, viewport.ClientSize.Width - (needsVerticalScroll ? SystemInformation.VerticalScrollBarWidth : 0)), Math.Max(minimumHeight, viewport.ClientSize.Height));
            rows.RowStyles[0].Height = (compact ? 48 : 68) * scale;
            rows.RowStyles[1].Height = (compact ? 108 : 126) * scale;
            rows.RowStyles[3].Height = (compact ? 42 : 60) * scale;
            rows.RowStyles[4].Height = (compact ? 18 : 25) * scale;
            settings.RowStyles[0].Height = (compact ? 31 : 38) * scale;
            _physicalInput.ItemHeight = _output.ItemHeight = (int)Math.Round((compact ? 23 : 28) * scale);
            _physicalInput.Font = _output.Font = new Font("Segoe UI", compact ? 10 : 11);
            _physicalStatus.Font = new Font("Segoe UI", compact ? 7.5f : 8.5f);
            foreach (var field in _mappingFields.Values) { field.Font = new Font("Segoe UI", compact ? 8.25f : 9.5f); field.Margin = new Padding(0, compact ? 1 : 3, 0, compact ? 1 : 3); }
            foreach (var caption in _mappingLabels.Values) caption.Font = new Font("Segoe UI", compact ? 8.75f : 10);
            foreach (var field in _axisFields.Values) { field.Font = new Font("Segoe UI", compact ? 8.25f : 9.5f); field.Margin = new Padding(0, compact ? 1 : 3, 0, compact ? 1 : 3); }
        }
        viewport.Resize += (_, _) => FitEditor();
        FitEditor();
        return viewport;
    }

    private void ConfigureCombo(ComboBox combo, string accessibleName)
    {
        combo.DropDownStyle = ComboBoxStyle.DropDownList; combo.Dock = DockStyle.Fill; combo.BackColor = Color.FromArgb(34, 41, 44); combo.ForeColor = ForeColor; combo.FlatStyle = FlatStyle.Flat; combo.AccessibleName = accessibleName;
        combo.DrawMode = DrawMode.OwnerDrawFixed; combo.ItemHeight = 28; combo.Font = new Font("Segoe UI", 11); combo.Margin = new Padding(0, 0, 0, 2);
        combo.DrawItem += (_, e) => DrawComboItem(combo, e);
    }

    private Control BuildMappingGroup(string title, string[] buttons, string? xAxis = null, string? yAxis = null)
    {
        var axisRows = xAxis == null ? 0 : 4;
        var count = buttons.Length + axisRows;
        var box = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = count + 1, Margin = new Padding(0, 0, 0, 10), Padding = new Padding(10, 0, 10, 8), BackColor = Color.FromArgb(27, 33, 35) };
        box.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72)); box.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        box.RowStyles.Add(new RowStyle(SizeType.Absolute, 29));
        var heading = Label(title, 10.5f, false, Color.FromArgb(224, 232, 236)); heading.Dock = DockStyle.Fill; heading.TextAlign = ContentAlignment.MiddleLeft; box.Controls.Add(heading, 0, 0); box.SetColumnSpan(heading, 2);
        box.Paint += (_, e) => { using var border = new Pen(Color.FromArgb(53, 62, 66)); e.Graphics.DrawRectangle(border, 0, 0, box.Width - 1, box.Height - 1); e.Graphics.DrawLine(border, 0, 28, box.Width, 28); };
        for (var index = 0; index < count; index++) box.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / count));
        if (xAxis != null && yAxis != null)
        {
            var definitions = new[] { (Caption: "X+", Axis: xAxis, Sign: "+"), (Caption: "X−", Axis: xAxis, Sign: "−"), (Caption: "Y+", Axis: yAxis, Sign: "+"), (Caption: "Y−", Axis: yAxis, Sign: "−") };
            for (var index = 0; index < definitions.Length; index++)
            {
                var item = definitions[index];
                var caption = Label(item.Caption, 10, false, ForeColor); caption.Dock = DockStyle.Fill; caption.TextAlign = ContentAlignment.MiddleLeft;
                var value = Label(T("Ao vivo  0,00", "Live  0.00"), 9.5f, false, Color.FromArgb(159, 174, 181)); value.Dock = DockStyle.Fill; value.TextAlign = ContentAlignment.MiddleCenter; value.Margin = new Padding(0, 3, 0, 3); value.BackColor = Color.FromArgb(34, 41, 44); value.AccessibleName = item.Caption + T(" · leitura do eixo, não editável", " · live axis, read only");
                _axisFields[item.Axis + item.Sign] = value;
                box.Controls.Add(caption, 0, index + 1); box.Controls.Add(value, 1, index + 1);
            }
        }
        for (var index = 0; index < buttons.Length; index++)
        {
            var logical = buttons[index];
            var caption = Label(logical, 10, false, ForeColor); caption.Dock = DockStyle.Fill; caption.TextAlign = ContentAlignment.MiddleLeft; _mappingLabels[logical] = caption;
            var field = ActionButton("—"); field.Dock = DockStyle.Fill; field.Margin = new Padding(0, 3, 0, 3); field.Font = new Font("Segoe UI", 9.5f); field.Click += (_, _) => RemapButton(logical); _mappingFields[logical] = field;
            box.Controls.Add(caption, 0, axisRows + index + 1); box.Controls.Add(field, 1, axisRows + index + 1);
        }
        return box;
    }

    private Control BuildConnectionPage(string pairingCode)
    {
        var page = InformationPage(T("Conexão com o celular", "Phone connection"), out var content);
        _connection.Dock = DockStyle.Top; _connection.Height = 42; _connection.Font = new Font("Segoe UI Semibold", 12);
        _network.Dock = DockStyle.Top; _network.Height = 140; _network.Font = new Font("Segoe UI", 11); _network.Padding = new Padding(0, 14, 0, 0); _network.ForeColor = ForeColor;
        var copy = ActionButton(T("Copiar código de pareamento", "Copy pairing code")); copy.Dock = DockStyle.Top; copy.Height = 42; copy.Visible = _wifiEnabled; copy.Click += (_, _) => { Clipboard.SetText(pairingCode); copy.Text = T("Código copiado", "Code copied"); };
        var tips = Label(T("Abra o Controller Bridge no Android e conecte seu controle ao celular.\r\n\r\nWi-Fi: use a mesma rede e informe o IP do PC e o código de pareamento.\r\nUSB: selecione USB no app e autorize a depuração USB, se solicitada.", "Open Controller Bridge on Android and connect your controller to the phone.\r\n\r\nWi-Fi: use the same network and enter the PC IP and pairing code.\r\nUSB: choose USB in the app and allow USB debugging if prompted."), 11, false, Color.FromArgb(171, 184, 191)); tips.Dock = DockStyle.Top; tips.Height = 180; tips.Padding = new Padding(0, 28, 0, 0);
        content.Controls.Add(tips); content.Controls.Add(copy); content.Controls.Add(_network); content.Controls.Add(_connection);
        return page;
    }

    private Control BuildGeneralPage()
    {
        var page = InformationPage(T("Geral", "General"), out var content);
        var version = typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "";
        var about = Label(T($"Controller Bridge · v{version}\r\n\r\nSeu controle. Do celular para o jogo.\r\n\r\nEscolha um jogador no topo. Em Controles, selecione a saída virtual e clique em um campo para capturar um botão do controle físico.\r\n\r\nOs eixos dos sticks são exibidos ao vivo. O remapeamento de eixos deve ser feito no app Android.\r\n\r\nAs alterações são salvas automaticamente. Mantenha o Controller Bridge aberto enquanto joga; você pode minimizar esta janela.", $"Controller Bridge · v{version}\r\n\r\nYour controller. From phone to game.\r\n\r\nChoose a player above. In Controls, choose the virtual output and click a field to capture a physical controller button.\r\n\r\nStick axes show live input. Configure axis mappings in the Android app.\r\n\r\nChanges save automatically. Keep Controller Bridge running while playing; you can minimize this window."), 12, false, Color.FromArgb(187, 199, 206)); about.Dock = DockStyle.Top; about.Height = 360;
        content.Controls.Add(about);
        return page;
    }

    private Control BuildSensorsPage()
    {
        var page = InformationPage(T("Movimento / Giroscópio", "Motion / Gyroscope"), out var content);
        _sensorDetails.Dock = DockStyle.Top; _sensorDetails.Height = 180; _sensorDetails.Font = new Font("Segoe UI", 12); _sensorDetails.ForeColor = ForeColor;
        var help = Label(T("Selecione a saída DualShock 4 e ative Movimento / Giroscópio na página Controles.\r\n\r\nO sensor do controle precisa ser exposto pelo Android. A leitura do giroscópio e da aceleração aparece acima quando há dados.\r\n\r\nNo emulador, habilite a fonte de movimento UDP / DSU em 127.0.0.1, porta 26760.", "Choose DualShock 4 output and enable Motion / Gyroscope in Controls.\r\n\r\nAndroid must expose the controller sensor. Gyroscope and acceleration readings appear above when data is available.\r\n\r\nIn the emulator, enable the UDP / DSU motion source at 127.0.0.1, port 26760."), 11, false, Color.FromArgb(171, 184, 191)); help.Dock = DockStyle.Top; help.Height = 220;
        var back = ActionButton(T("Abrir controles", "Open controls")); back.Dock = DockStyle.Top; back.Height = 44; back.Click += (_, _) => NavigateTo("controls");
        content.Controls.Add(back); content.Controls.Add(help); content.Controls.Add(_sensorDetails);
        return page;
    }

    private Panel InformationPage(string title, out Panel content)
    {
        var page = Card(); page.Padding = new Padding(28);
        var heading = Label(title, 20, false, ForeColor); heading.Dock = DockStyle.Top; heading.Height = 64;
        content = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(0, 12, 0, 0) };
        page.Controls.Add(content); page.Controls.Add(heading);
        return page;
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
            var key = index < keys.Length ? keys[index] : null;
            var selected = key != null && key == _selectedKey;
            var tab = ActionButton($"{T("Jogador", "Player")} {index + 1}");
            tab.Size = new Size(154, 45); tab.Margin = new Padding(0, 0, 4, 0); tab.Font = new Font("Segoe UI", 11);
            tab.Enabled = true; tab.Cursor = key == null ? Cursors.Default : Cursors.Hand;
            tab.BackColor = selected ? Color.FromArgb(34, 47, 55) : Color.FromArgb(25, 30, 32);
            tab.ForeColor = key != null ? Color.FromArgb(231, 239, 243) : Color.FromArgb(139, 151, 157);
            tab.FlatAppearance.BorderColor = Color.FromArgb(57, 67, 72);
            ((DashboardActionButton)tab).ActiveTab = selected;
            tab.AccessibleName = key == null
                ? $"{T("Jogador", "Player")} {index + 1}: {T("aguardando controle", "waiting for controller")}"
                : $"{T("Jogador", "Player")} {index + 1}: {_items[key].Name}";
            if (key != null) tab.Click += (_, _) => SelectControllerTab(key);
            _playerTabs.Controls.Add(tab);
        }        _playerTabs.ResumeLayout();
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
        _selectedName.Text = T("Controle físico:", "Physical controller:");
        _physicalInput.Enabled = snapshot != null;
        if (snapshot != null) _physicalInput.SelectedItem = _physicalInput.Items.Cast<ControllerChoice>().FirstOrDefault(choice => choice.Key == Key(snapshot));
        _output.Enabled = snapshot != null; _gyro.Enabled = snapshot?.Output == "ds4";
        // Native dropdown navigation is tentative until the user commits it. Live input
        // refreshes must not send CB_SETCURSEL and replace that pending selection.
        var selectedOutput = snapshot?.Output == "ds4" ? 1 : 0;
        if (!_output.DroppedDown && _output.SelectedIndex != selectedOutput) _output.SelectedIndex = selectedOutput;
        _gyro.Checked = snapshot?.GyroEnabled ?? false;
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
            pair.Value.BackColor = pending ? Color.FromArgb(63, 57, 38) : active ? Color.FromArgb(25, 60, 67) : Color.FromArgb(34, 41, 44);
            pair.Value.FlatAppearance.BorderColor = pending ? Color.FromArgb(249, 190, 74) : active ? Color.FromArgb(58, 218, 240) : Color.FromArgb(71, 82, 88);
            _mappingLabels[pair.Key].Text = pair.Key is "l3" or "r3" ? T("Pressão", "Press") : DisplayButton(pair.Key);
            pair.Value.AccessibleName = DisplayButton(pair.Key) + ": " + pair.Value.Text;
        }
        if (snapshot == null) _mappingHint.Text = T("Conecte o app do celular para começar.", "Connect the phone app to get started.");
        else if (_captures.TryGetValue(Key(snapshot), out var waiting)) _mappingHint.Text = T("Pressione no controle físico o botão para ", "Press the physical controller button for ") + DisplayButton(waiting.Button) + T(". Clique em outro campo para trocar o destino.", ". Click another field to change the target.");
        else if (string.IsNullOrEmpty(_mappingHint.Text)) _mappingHint.Text = T("Escolha um campo para remapear este controle.", "Choose a field to remap this controller.");
        var state = visible?.State ?? GamepadState.Neutral();
        foreach (var (axis, field) in _axisFields)
        {
            var raw = state.Axis(axis[..^1]);
            var amount = Math.Max(0, axis.EndsWith('+') ? raw : -raw);
            field.Text = T("Ao vivo  ", "Live  ") + amount.ToString("0.00");
            field.ForeColor = amount > 0.1f ? Color.FromArgb(91, 225, 238) : Color.FromArgb(159, 174, 181);
        }
        _axes.Text = $"{(snapshot?.Output == "ds4" ? "L2" : "LT")}  {state.Axis("lt"):0.00}";
        _rightTriggerAxis.Text = $"{(snapshot?.Output == "ds4" ? "R2" : "RT")}  {state.Axis("rt"):0.00}";
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
        _sensorDetails.Text = _motion.Text;
        _motion.Text = _motion.Text.Split('\r', '\n')[0];
        _updating = false;
    }

    private void ChangeOutput()
    {
        if (_updating || _selectedKey == null || !_items.TryGetValue(_selectedKey, out var snapshot) || _output.SelectedIndex < 0) return;
        try
        {
            var output = _output.SelectedIndex == 1 ? "ds4" : "xbox360";
            var gyroEnabled = output == "ds4" && _gyro.Checked;
            if (snapshot.Output == output && snapshot.GyroEnabled == gyroEnabled) return;
            _host.SetControllerOutput(snapshot, output, gyroEnabled);
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

    private Panel Card()
    {
        var panel = new Panel { BackColor = Color.FromArgb(25, 30, 32), Margin = Padding.Empty };
        panel.Paint += (_, e) => { using var border = new Pen(Color.FromArgb(52, 61, 65)); e.Graphics.DrawRectangle(border, 0, 0, panel.Width - 1, panel.Height - 1); };
        return panel;
    }
    private Label Label(string text, float size, bool bold, Color color) => new() { Text = text, Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular), ForeColor = color, BackColor = Color.Transparent, AutoEllipsis = true };
    private Button ActionButton(string text)
    {
        var button = new DashboardActionButton { Text = text, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(34, 41, 44), ForeColor = Color.FromArgb(228, 235, 239), Cursor = Cursors.Hand, UseVisualStyleBackColor = false };
        button.FlatAppearance.BorderColor = Color.FromArgb(71, 82, 88);
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(43, 54, 60);
        button.FlatAppearance.MouseDownBackColor = Color.FromArgb(26, 67, 81);
        return button;
    }
    private static void DrawComboItem(ComboBox combo, DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        var selected = (e.State & DrawItemState.Selected) != 0;
        using var background = new SolidBrush(selected ? Color.FromArgb(40, 59, 69) : Color.FromArgb(34, 41, 44));
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
            "dpad_up" => T("Cima", "Up"), "dpad_down" => T("Baixo", "Down"), "dpad_left" => T("Esquerda", "Left"), "dpad_right" => T("Direita", "Right"), _ => name
        };
    }
}
