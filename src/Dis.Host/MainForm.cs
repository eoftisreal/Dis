using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Dis.Core;

namespace Dis.Host;

internal sealed class MainForm : Form
{
    private sealed record Connection(string Label, IPAddress Address) { public override string ToString() => Label; }
    private sealed record DisplayChoice(string Name, Rectangle Bounds) { public override string ToString() => $"{Name} · {Bounds.Width} × {Bounds.Height}"; }
    private readonly ComboBox connection = Choice();
    private readonly ComboBox display = Choice();
    private readonly ComboBox mode = Choice();
    private readonly ComboBox qualityPreset = Choice();
    private readonly ComboBox orientation = Choice();
    private readonly NumericUpDown fps = new() { Minimum = 10, Maximum = 60, Value = 60, Dock = DockStyle.Fill };
    private readonly NumericUpDown port = new() { Minimum = 1024, Maximum = 65535, Value = 49830, Dock = DockStyle.Fill };
    private readonly TextBox adb = new() { Dock = DockStyle.Fill };
    private readonly TextBox serial = new() { PlaceholderText = "Automatic for one authorized phone", Dock = DockStyle.Fill };
    private readonly TextBox pairing = new() { ReadOnly = true, Multiline = true, Height = 65, Dock = DockStyle.Fill };
    private readonly CheckBox remoteInput = new() { Text = "Touch and pen", Checked = true, AutoSize = true };
    private readonly Label status = new() { Text = "Ready · choose Second display or Mirror", AutoSize = true, Dock = DockStyle.Fill };
    private readonly Button connect = Button("Connect");
    private readonly Button setup = Button("Set up second display");
    private readonly Button rotate = Button("Apply orientation");
    private readonly TableLayoutPanel advanced = new() { AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 2, Visible = false };
    private readonly List<Control> settings = [];
    private DisplayServer? server;
    private AdbBridge? tunnel;
    private PairingSession? session;
    private bool busy, closing;

    public MainForm()
    {
        Text = "Dis · Your second screen"; Size = new(790, 730); MinimumSize = new(700, 620);
        Font = new("Segoe UI", 10); BackColor = Color.FromArgb(244, 246, 250);
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new(24), ColumnCount = 2, RowCount = 1 };
        root.ColumnStyles.Add(new(SizeType.Absolute, 175)); root.ColumnStyles.Add(new(SizeType.Percent, 100)); Controls.Add(root);
        var title = new Label { Text = "Your next screen.", Font = new("Segoe UI", 24, FontStyle.Bold), AutoSize = true };
        root.Controls.Add(title, 0, 0); root.SetColumnSpan(title, 2);
        mode.Items.AddRange(["Second display", "Mirror selected screen"]); mode.SelectedIndex = 0;
        qualityPreset.Items.AddRange(["720p · smooth", "1080p · detailed"]); qualityPreset.SelectedIndex = 0;
        orientation.Items.AddRange(["Landscape", "Portrait", "Landscape flipped", "Portrait flipped"]); orientation.SelectedIndex = 0;
        connection.Items.Add(new Connection("USB · open and connect Android automatically", IPAddress.Loopback));
        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up))
        foreach (var address in adapter.GetIPProperties().UnicastAddresses)
            if (address.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address.Address))
                connection.Items.Add(new Connection($"{adapter.Name} · {address.Address}", address.Address));
        connection.SelectedIndex = 0;
        Add(root, "Connection", connection); Add(root, "Use Android as", mode); Add(root, "Mirror screen", display);
        Add(root, "Quality", qualityPreset); Add(root, "Target frame rate", fps);
        var rotationRow = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 2 };
        rotationRow.ColumnStyles.Add(new(SizeType.Percent, 100)); rotationRow.ColumnStyles.Add(new(SizeType.AutoSize));
        rotationRow.Controls.Add(orientation, 0, 0); rotationRow.Controls.Add(rotate, 1, 0); Add(root, "Windows orientation", rotationRow);
        Add(root, "Input", remoteInput);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        var installAndroid = Button("Install/update Android app"); actions.Controls.AddRange([connect, setup, installAndroid]); Add(root, "", actions);
        Add(root, "Status", status);
        var links = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        Button showAdvanced = Button("Advanced / network pairing"), refresh = Button("Refresh screens"), windowsSettings = Button("Windows display settings");
        links.Controls.AddRange([showAdvanced, refresh, windowsSettings]); Add(root, "", links);
        advanced.ColumnStyles.Add(new(SizeType.Absolute, 140)); advanced.ColumnStyles.Add(new(SizeType.Percent, 100));
        var adbRow = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2 };
        adbRow.ColumnStyles.Add(new(SizeType.Percent, 100)); adbRow.ColumnStyles.Add(new(SizeType.AutoSize));
        var browse = Button("Browse…"); adbRow.Controls.Add(adb, 0, 0); adbRow.Controls.Add(browse, 1, 0);
        Add(advanced, "ADB executable", adbRow); Add(advanced, "Device serial", serial); Add(advanced, "Port", port); Add(advanced, "Network link", pairing);
        Button copy = Button("Copy network connection"), getTools = Button("Get Platform Tools");
        var pairButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true }; pairButtons.Controls.AddRange([copy, getTools]); Add(advanced, "", pairButtons);
        Add(root, "", advanced);
        Add(root, "", new Label { AutoSize = true, MaximumSize = new(520, 0), Text = "USB: install the Android app once, authorize USB debugging, then press Connect. Second display needs the one-time driver setup. H.264 uses a hardware encoder when available; the live status shows the path actually in use." });
        adb.Text = AdbLocator.Find(null) ?? "adb.exe";
        RefreshDisplays();
        settings.AddRange([connection, mode, display, qualityPreset, fps, remoteInput, adb, serial, port, browse, setup, installAndroid, refresh]);
        mode.SelectedIndexChanged += (_, _) => display.Enabled = server is null && mode.SelectedIndex == 1;
        connection.SelectedIndexChanged += (_, _) => { if (connection.SelectedIndex > 0) advanced.Visible = true; };
        connect.Click += async (_, _) => await ToggleAsync();
        rotate.Click += async (_, _) => await RotateAsync();
        setup.Click += async (_, _) => await SetupAsync();
        installAndroid.Click += async (_, _) => await InstallAndroidAsync();
        showAdvanced.Click += (_, _) => advanced.Visible = !advanced.Visible;
        refresh.Click += (_, _) => RefreshDisplays();
        windowsSettings.Click += (_, _) => OpenUrl("ms-settings:display");
        getTools.Click += (_, _) => OpenUrl("https://developer.android.com/tools/releases/platform-tools");
        copy.Click += (_, _) => { if (pairing.Text.Length > 0) Clipboard.SetText(pairing.Text); };
        browse.Click += (_, _) => {
            using var picker = new OpenFileDialog { Title = "Select adb.exe", Filter = "Android Debug Bridge|adb.exe", CheckFileExists = true };
            if (picker.ShowDialog(this) == DialogResult.OK) adb.Text = picker.FileName;
        };
        display.Enabled = false;
        FormClosing += OnClosing;
    }
    private static ComboBox Choice() => new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private static Button Button(string text) => new() { Text = text, AutoSize = true };
    private static void Add(TableLayoutPanel panel, string label, Control control)
    {
        int row = panel.RowCount++;
        panel.RowStyles.Add(new(SizeType.AutoSize));
        panel.Controls.Add(new Label { Text = label, AutoSize = true, Padding = new(0, 8, 0, 8) }, 0, row);
        control.Margin = new(0, 5, 0, 5); panel.Controls.Add(control, 1, row);
    }
    private void RefreshDisplays(string? preferred = null)
    {
        string? old = preferred ?? (display.SelectedItem as DisplayChoice)?.Name;
        display.Items.Clear(); foreach (var screen in Screen.AllScreens) display.Items.Add(new DisplayChoice(screen.DeviceName, screen.Bounds));
        if (display.Items.Count > 0) display.SelectedIndex = Math.Max(0, display.Items.Cast<DisplayChoice>().ToList().FindIndex(s => s.Name == old));
    }
    private void SetBusy(bool value)
    {
        busy = value; connect.Enabled = !value; rotate.Enabled = !value; orientation.Enabled = !value;
        foreach (var item in settings) item.Enabled = !value && server is null;
        display.Enabled = !value && server is null && mode.SelectedIndex == 1;
    }
    private async Task StartAsync()
    {
        if (connection.SelectedItem is not Connection chosen) throw new IOException("Select a connection.");
        if (mode.SelectedIndex == 0)
        {
            string virtualName = DisplayModes.EnsureExtended();
            for (int i = 0; i < 30; i++) {
                RefreshDisplays(virtualName);
                if ((display.SelectedItem as DisplayChoice)?.Name == virtualName) break;
                await Task.Delay(100);
            }
            if ((display.SelectedItem as DisplayChoice)?.Name != virtualName) throw new IOException("Windows has not activated the virtual monitor. Open Windows display settings and choose Extend.");
        }
        if (display.SelectedItem is not DisplayChoice screen) throw new IOException("Select an active screen.");
        status.Text = "Connecting…";
        if (chosen.Address.Equals(IPAddress.Loopback)) {
            adb.Text = AdbLocator.Resolve(adb.Text);
            tunnel = await AdbBridge.ConnectAsync(adb.Text, serial.Text, (int)port.Value);
        }
        session ??= new();
        int pixels = qualityPreset.SelectedIndex == 0 ? 1280 * 720 : 1920 * 1080;
        server = new(new(chosen.Address, (int)port.Value, screen.Name, screen.Bounds, 1920, 1920, (int)fps.Value, 75, remoteInput.Checked, pixels), session);
        server.Status += UpdateStatus; pairing.Text = server.ConnectionUri(chosen.Address.ToString()); connect.Text = "Disconnect";
        if (tunnel is not null) {
            try { await tunnel.OpenReceiverAsync(pairing.Text); status.Text = "Android opened · connecting automatically…"; }
            catch (IOException e) { status.Text = e.Message + " Use Install/update Android app, then Connect again."; await StopAsync(); throw; }
        }
        else { advanced.Visible = true; status.Text = "Waiting for Android · use the network link under Advanced"; }
    }
    private async Task ToggleAsync()
    {
        if (busy) return; SetBusy(true);
        try { if (server is not null) await StopAsync(); else await StartAsync(); }
        catch (Exception e) when (Handled(e)) { await StopAsync(); status.Text = e.Message; }
        finally { SetBusy(false); }
    }
    private async Task StopAsync(bool revoke = true)
    {
        if (server is not null) { var old = server; server = null; old.Status -= UpdateStatus; await old.DisposeAsync(); }
        if (tunnel is not null) {
            var old = tunnel; tunnel = null;
            try { await old.DisposeAsync(); } catch (Exception e) when (Handled(e)) { status.Text = e.Message; }
        }
        if (revoke) { session?.Dispose(); session = null; }
        pairing.Clear(); connect.Text = "Connect";
    }
    private async Task RotateAsync()
    {
        if (busy) return; SetBusy(true); bool restart = server is not null;
        try {
            string name = mode.SelectedIndex == 0 ? DisplayModes.EnsureExtended()
                : (display.SelectedItem as DisplayChoice)?.Name ?? throw new IOException("Select a screen.");
            if (restart) await StopAsync(false);
            DisplayModes.Rotate(name, orientation.SelectedIndex);
            await Task.Delay(350); RefreshDisplays(name);
            if (restart) await StartAsync(); else status.Text = "Display orientation updated";
        }
        catch (Exception e) when (Handled(e)) { await StopAsync(); status.Text = e.Message; }
        finally { SetBusy(false); }
    }
    private async Task SetupAsync()
    {
        if (busy) return; SetBusy(true);
        try {
            string executable = Environment.ProcessPath ?? throw new IOException("Cannot locate Dis.exe.");
            var info = new ProcessStartInfo(executable) { UseShellExecute = true, Verb = "runas", WorkingDirectory = AppContext.BaseDirectory };
            if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase)) info.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "Dis.dll"));
            info.ArgumentList.Add("--install-display-driver");
            using var helper = Process.Start(info) ?? throw new IOException("Cannot open driver setup.");
            await helper.WaitForExitAsync(); RefreshDisplays();
            status.Text = helper.ExitCode == 0 ? "Driver setup complete · press Connect in Second display mode" : "Driver setup did not finish. Check the setup window's message.";
        }
        catch (Exception e) when (Handled(e)) { status.Text = e.Message; }
        finally { SetBusy(false); }
    }
    private async Task InstallAndroidAsync()
    {
        if (busy) return; SetBusy(true);
        try {
            string apk = Path.Combine(AppContext.BaseDirectory, "Dis.apk");
            if (!File.Exists(apk)) {
                using var picker = new OpenFileDialog { Title = "Choose the Dis Android APK", Filter = "Android app|*.apk", CheckFileExists = true };
                if (picker.ShowDialog(this) != DialogResult.OK) return;
                apk = picker.FileName;
            }
            status.Text = "Installing Android app over USB…";
            await AdbBridge.InstallReceiverAsync(adb.Text, serial.Text, apk);
            status.Text = "Android app installed · press Connect";
        }
        catch (Exception e) when (Handled(e)) { status.Text = e.Message; }
        finally { SetBusy(false); }
    }
    private void OpenUrl(string url) { try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch (System.ComponentModel.Win32Exception e) { status.Text = e.Message; } }
    private static bool Handled(Exception e) => e is IOException or SocketException or System.ComponentModel.Win32Exception or
        System.Security.Cryptography.CryptographicException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException;
    private void UpdateStatus(string value) {
        if (IsDisposed || !IsHandleCreated) return;
        try { BeginInvoke(() => { if (!IsDisposed) status.Text = value; }); } catch (InvalidOperationException) when (IsDisposed || !IsHandleCreated) { }
    }
    private async void OnClosing(object? sender, FormClosingEventArgs e) {
        if (closing) return; e.Cancel = true;
        if (busy) { status.Text = "Wait for the current connection/setup operation to finish."; return; }
        closing = true; Enabled = false; try { await StopAsync(); } finally { Close(); }
    }
}
