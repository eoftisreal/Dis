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
    private readonly ComboBox qualityPreset = Choice();
    private readonly ComboBox desktopSize = Choice();
    private readonly ComboBox orientation = Choice();
    private readonly NumericUpDown fps = new() { Minimum = 10, Maximum = 60, Value = 60, Dock = DockStyle.Fill };
    private readonly NumericUpDown port = new() { Minimum = 1024, Maximum = 65535, Value = 49830, Dock = DockStyle.Fill };
    private readonly TextBox adb = new() { Dock = DockStyle.Fill };
    private readonly TextBox serial = new() { PlaceholderText = "Automatic for one authorized phone", Dock = DockStyle.Fill };
    private readonly TextBox pairing = new() { ReadOnly = true, Multiline = true, Height = 65, Dock = DockStyle.Fill };
    private readonly CheckBox remoteInput = new() { Text = "Touch and pen", Checked = true, AutoSize = true };
    private readonly CheckBox autoConnect = new() { Text = "Connect USB when Dis opens", AutoSize = true };
    private readonly Label status = new() { Text = "Ready · connect your Android display", AutoSize = true, Dock = DockStyle.Fill };
    private readonly Button connect = Button("Connect");
    private readonly Button setup = Button("Set up second display");
    private readonly Button rotate = Button("Apply orientation");
    private readonly TableLayoutPanel advanced = new() { AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 2, Visible = false };
    private readonly List<Control> settings = [];
    private DisplayServer? server;
    private AdbBridge? tunnel;
    private PairingSession? session;
    private string? sessionDisplay;
    private DriverSession? driverSession;
    private bool busy, closing;
    private readonly System.Windows.Forms.Timer receiverWatch = new() { Interval = 1000 };
    private long receiverStarted;

    public MainForm()
    {
        Text = "Dis · Virtual Monitor"; Size = new(840, 850); MinimumSize = new(740, 650);
        Font = new("Segoe UI", 10); BackColor = Color.FromArgb(15, 23, 42); ForeColor = Color.FromArgb(226, 232, 240);
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new(24), ColumnCount = 2, RowCount = 1 };
        root.ColumnStyles.Add(new(SizeType.Absolute, 175)); root.ColumnStyles.Add(new(SizeType.Percent, 100)); Controls.Add(root);
        var title = new Label { Text = "Dis / Virtual monitor", Font = new("Segoe UI", 24, FontStyle.Bold), AutoSize = true };
        root.Controls.Add(title, 0, 0); root.SetColumnSpan(title, 2);
        qualityPreset.Items.AddRange(["Fast · 720p / 60 fps", "Balanced · 1080p / 30 fps", "Sharp · 1080p / 60 fps"]); qualityPreset.SelectedIndex = 0;
        desktopSize.Items.AddRange(["Match phone shape", "Full HD · 1920 × 1080", "Large text · compact desktop"]); desktopSize.SelectedIndex = 0;
        orientation.Items.AddRange(["Landscape", "Portrait", "Landscape flipped", "Portrait flipped"]); orientation.SelectedIndex = 0;
        connection.Items.Add(new Connection("USB · open and connect Android automatically", IPAddress.Loopback));
        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up))
        foreach (var address in adapter.GetIPProperties().UnicastAddresses)
            if (address.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address.Address))
                connection.Items.Add(new Connection($"{adapter.Name} · {address.Address}", address.Address));
        connection.SelectedIndex = 0;
        Add(root, "Connection", connection);
        Add(root, "Desktop size", desktopSize); Add(root, "Performance", qualityPreset); Add(root, "Target frame rate", fps);
        var rotationRow = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 2 };
        rotationRow.ColumnStyles.Add(new(SizeType.Percent, 100)); rotationRow.ColumnStyles.Add(new(SizeType.AutoSize));
        rotationRow.Controls.Add(orientation, 0, 0); rotationRow.Controls.Add(rotate, 1, 0); Add(root, "Windows orientation", rotationRow);
        Add(root, "Input", remoteInput);
        Add(root, "Startup", autoConnect);
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
        Add(advanced, "Device serial", serial); Add(advanced, "Port", port); Add(advanced, "Network link", pairing);
        Button copy = Button("Copy network connection");
        var pairButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true }; pairButtons.Controls.Add(copy); Add(advanced, "", pairButtons);
        Add(root, "", advanced);
        Add(root, "", new Label { AutoSize = true, MaximumSize = new(520, 0), Text = "USB: install the Android app once, authorize USB debugging, then press Connect. Second display needs the one-time driver setup. H.264 uses a hardware encoder when available; the live status shows the path actually in use." });
        adb.Text = "";
        var preferences = HostPreferences.Load();
        qualityPreset.SelectedIndex = Math.Clamp(preferences.Quality, 0, 2);
        desktopSize.SelectedIndex = Math.Clamp(preferences.Desktop, 0, 2);
        orientation.SelectedIndex = Math.Clamp(preferences.Orientation, 0, 3);
        fps.Value = Math.Clamp(preferences.Fps, 10, 60);
        remoteInput.Checked = preferences.Input; autoConnect.Checked = preferences.AutoConnect;
        if (!string.IsNullOrWhiteSpace(preferences.Adb)) adb.Text = preferences.Adb;
        serial.Text = preferences.Serial ?? "";
        RefreshDisplays();
        settings.AddRange([connection, qualityPreset, desktopSize, fps, remoteInput, autoConnect, adb, serial, port, browse, setup, installAndroid, refresh]);
        qualityPreset.SelectedIndexChanged += (_, _) => fps.Value = qualityPreset.SelectedIndex == 1 ? 30 : 60;
        connection.SelectedIndexChanged += (_, _) => { if (connection.SelectedIndex > 0) advanced.Visible = true; };
        connect.Click += async (_, _) => await ToggleAsync();
        rotate.Click += async (_, _) => await RotateAsync();
        setup.Click += async (_, _) => await SetupAsync();
        installAndroid.Click += async (_, _) => await InstallAndroidAsync();
        showAdvanced.Click += (_, _) => advanced.Visible = !advanced.Visible;
        refresh.Click += (_, _) => RefreshDisplays();
        windowsSettings.Click += (_, _) => OpenUrl("ms-settings:display");
        copy.Click += (_, _) => { if (pairing.Text.Length > 0) Clipboard.SetText(pairing.Text); };
        browse.Click += (_, _) => {
            using var picker = new OpenFileDialog { Title = "Select adb.exe", Filter = "Android Debug Bridge|adb.exe", CheckFileExists = true };
            if (picker.ShowDialog(this) == DialogResult.OK) adb.Text = picker.FileName;
        };
        display.Enabled = false;
        ApplyTheme(root);
        connect.BackColor = Color.FromArgb(37, 99, 235); connect.ForeColor = Color.White;
        connect.Font = new(Font, FontStyle.Bold); connect.Padding = new(18, 8, 18, 8);
        status.ForeColor = Color.FromArgb(94, 234, 212); status.MaximumSize = new(520, 0);
        Shown += async (_, _) => { if (autoConnect.Checked) await ToggleAsync(); };
        FormClosing += OnClosing;
        receiverWatch.Tick += async (_, _) => await CheckReceiverAsync();
        Disposed += (_, _) => receiverWatch.Dispose();
    }
    private static void ApplyTheme(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            child.ForeColor = Color.FromArgb(226, 232, 240);
            if (child is Button button) { button.FlatStyle = FlatStyle.Flat; button.FlatAppearance.BorderColor = Color.FromArgb(51, 65, 85); button.BackColor = Color.FromArgb(30, 41, 59); }
            else if (child is ComboBox choice) { choice.FlatStyle = FlatStyle.Flat; choice.BackColor = Color.FromArgb(30, 41, 59); }
            else if (child is TextBox or NumericUpDown) child.BackColor = Color.FromArgb(30, 41, 59);
            ApplyTheme(child);
        }
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
        display.Enabled = false;
    }
    private async Task StartAsync()
    {
        if (connection.SelectedItem is not Connection chosen) throw new IOException("Select a connection.");
        // Verify the phone before creating a virtual desktop or asking for elevation.
        if (chosen.Address.Equals(IPAddress.Loopback)) {
            adb.Text = BundledUsbTools.Resolve(adb.Text);
            tunnel = await AdbBridge.ConnectAsync(adb.Text, serial.Text, (int)port.Value);
        }
        {
            status.Text = "Activating second display…";
            string virtualName = await PrepareVirtualDisplayAsync();
            int width = desktopSize.SelectedIndex == 2 ? 800 : 1920;
            int height = desktopSize.SelectedIndex == 2 ? 600 : 1080;
            if (desktopSize.SelectedIndex == 0 && tunnel is not null) {
                var phone = await tunnel.ScreenSizeAsync();
                width = Math.Max(phone.Width, phone.Height); height = Math.Min(phone.Width, phone.Height);
            }
            if ((orientation.SelectedIndex & 1) != 0) (width, height) = (height, width);
            DisplayModes.SetDesktopSize(virtualName, width, height, orientation.SelectedIndex);
            Rectangle expectedBounds = DisplayModes.Bounds(virtualName);
            for (int i = 0; i < 30; i++) {
                RefreshDisplays(virtualName);
                if (display.SelectedItem is DisplayChoice selected && selected.Name == virtualName && selected.Bounds == expectedBounds) break;
                await Task.Delay(100);
            }
            if (display.SelectedItem is not DisplayChoice activated || activated.Name != virtualName || activated.Bounds != expectedBounds)
                throw new IOException("Windows has not finished activating the selected desktop size. Retry Connect.");
        }
        if (display.SelectedItem is not DisplayChoice screen) throw new IOException("Select an active screen.");
        status.Text = "Connecting…";
        session ??= new();
        SavePreferences();
        int pixels = qualityPreset.SelectedIndex == 0 ? 1280 * 720 : 1920 * 1080;
        server = new(new(chosen.Address, (int)port.Value, screen.Name, screen.Bounds, 1920, 1920, (int)fps.Value, 75, remoteInput.Checked, pixels), session);
        server.Status += UpdateStatus; pairing.Text = server.ConnectionUri(chosen.Address.ToString()); connect.Text = "Disconnect";
        receiverStarted = Stopwatch.GetTimestamp(); receiverWatch.Start();
        server.Statistics += _ => { if (server?.LastFrameAcknowledged > 0) BeginInvoke(() => driverSession?.KeepAlive()); };
        if (tunnel is not null) {
            try { await tunnel.OpenReceiverAsync(pairing.Text); status.Text = "Android opened · connecting automatically…"; }
            catch (IOException e) { status.Text = e.Message + " Use Install/update Android app, then Connect again."; await StopAsync(); throw; }
        }
        else { advanced.Visible = true; status.Text = "Waiting for Android · use the network link under Advanced"; }
    }
    private async Task<string> PrepareVirtualDisplayAsync()
    {
        if (driverSession is null)
        {
            driverSession = await DriverSession.StartAsync();
            for (int i = 0; i < 50 && !DisplayModes.Enumerate().Any(a => a.Virtual); i++) await Task.Delay(100);
        }
        // Claim cleanup before activation, so partial failures also detach the monitor.
        sessionDisplay = DisplayModes.Enumerate().FirstOrDefault(a => a.Virtual)?.Name;
        string name = await DisplayModes.EnsureExtendedAsync();
        sessionDisplay = name;
        return name;
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
        receiverWatch.Stop();
        try
        {
            if (server is not null) { var old = server; server = null; old.Status -= UpdateStatus; await old.DisposeAsync(); }
        }
        finally
        {
            try
            {
                if (tunnel is not null) {
                    var old = tunnel; tunnel = null;
                    try { await old.DisposeAsync(); } catch (Exception e) when (Handled(e)) { status.Text = e.Message; }
                }
            }
            finally
            {
                if (revoke)
                {
                    session?.Dispose(); session = null;
                    if (sessionDisplay is not null)
                    {
                        try { DisplayModes.Disconnect(sessionDisplay); sessionDisplay = null; }
                        catch (Exception e) when (Handled(e)) { status.Text = "Could not disconnect the virtual monitor: " + e.Message; }
                    }
                    if (driverSession is not null)
                    {
                        var old = driverSession; driverSession = null;
                        try { await old.DisposeAsync(); sessionDisplay = null; }
                        catch (Exception e) when (Handled(e) || e is TimeoutException) { status.Text = e.Message; }
                    }
                }
                pairing.Clear(); connect.Text = "Connect";
            }
        }
    }
    private async Task RotateAsync()
    {
        if (server is null) { SavePreferences(); status.Text = "Orientation saved for your next connection."; return; }
        if (busy) return; SetBusy(true); bool restart = server is not null;
        try {
            string name = await PrepareVirtualDisplayAsync();
            sessionDisplay = name;
            if (restart) await StopAsync(false);
            DisplayModes.Rotate(name, orientation.SelectedIndex);
            await Task.Delay(350); RefreshDisplays(name);
            if (restart) await StartAsync(); else status.Text = "Display orientation updated";
        }
        catch (Exception e) when (Handled(e)) { await StopAsync(); status.Text = e.Message; }
        finally { SetBusy(false); }
    }
    private async Task CheckReceiverAsync()
    {
        if (busy || closing || server is null) return;
        long acknowledged = server.LastFrameAcknowledged;
        TimeSpan? age = acknowledged == 0 ? null : Stopwatch.GetElapsedTime(acknowledged);
        if (age is not null && age < TimeSpan.FromSeconds(5)) driverSession?.KeepAlive();
        bool usb = connection.SelectedItem is Connection c && IPAddress.IsLoopback(c.Address);
        if (!ReceiverWatchdog.Expired(Stopwatch.GetElapsedTime(receiverStarted), age, server.HasReceiver, usb)) return;
        SetBusy(true);
        try { await StopAsync(); status.Text = "The phone is not displaying video. The virtual display was removed. Reconnect when the phone is ready."; }
        catch (Exception e) when (Handled(e)) { status.Text = e.Message; }
        finally { SetBusy(false); }
    }
    private void SavePreferences()
    {
        try { new HostPreferences(0, qualityPreset.SelectedIndex, desktopSize.SelectedIndex,
            orientation.SelectedIndex, (int)fps.Value, remoteInput.Checked, true, adb.Text, serial.Text, autoConnect.Checked).Save(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { status.Text = "Settings could not be saved: " + e.Message; }
    }
    private async Task SetupAsync()
    {
        if (busy) return; SetBusy(true);
        try {
            string executable = Environment.ProcessPath ?? throw new IOException("Cannot locate Dis.exe.");
            var info = new ProcessStartInfo(executable) { UseShellExecute = true, Verb = "runas", WorkingDirectory = AppContext.BaseDirectory };
            if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase)) info.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, typeof(Program).Assembly.GetName().Name + ".dll"));
            info.ArgumentList.Add("--install-display-driver");
            using var helper = Process.Start(info) ?? throw new IOException("Cannot open driver setup.");
            await helper.WaitForExitAsync();
            if (helper.ExitCode == 0)
                foreach (var adapter in DisplayModes.Enumerate().Where(a => a.Virtual && a.Active))
                    DisplayModes.Disconnect(adapter.Name);
            RefreshDisplays();
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
    private static bool Handled(Exception e) => e is IOException or TimeoutException or SocketException or System.ComponentModel.Win32Exception or
        System.Security.Cryptography.CryptographicException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException;
    private void UpdateStatus(string value) {
        if (IsDisposed || !IsHandleCreated) return;
        try { BeginInvoke(() => { if (!IsDisposed) status.Text = value; }); } catch (InvalidOperationException) when (IsDisposed || !IsHandleCreated) { }
    }
    private async void OnClosing(object? sender, FormClosingEventArgs e) {
        if (closing) return; e.Cancel = true;
        if (busy) { status.Text = "Wait for the current connection/setup operation to finish."; return; }
        closing = true; Enabled = false; SavePreferences(); try { await StopAsync(); } finally { Close(); }
    }
}
