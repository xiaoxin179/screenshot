using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Interop;

namespace ScreenshotTool;

public partial class MainWindow : Window
{
    private const int HotkeyId = 1;
    private readonly NotifyIcon _tray;
    private HwndSource? _source;
    private uint _modifiers = 5;
    private uint _key = 0x53;
    private bool _recordingHotkey;
    private bool _hotkeyRegistered;
    private bool _exitRequested;

    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr window, int id);

    public MainWindow()
    {
        InitializeComponent();
        StartupCheck.IsChecked = StartupManager.IsEnabled();
        HotkeyText.Text = ShortcutSettings.Load();
        TryParseHotkey(HotkeyText.Text, out _modifiers, out _key);

        _tray = new NotifyIcon { Icon = SystemIcons.Application, Visible = true, Text = "Snaply" };
        var menu = new ContextMenuStrip();
        menu.Items.Add("打开设置", null, (_, _) => ShowSettings());
        menu.Items.Add("截图", null, (_, _) => CaptureAndCopy());
        menu.Items.Add("退出", null, (_, _) => { _exitRequested = true; Close(); });
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => ShowSettings();
    }

    // A hidden autostart window still needs an HWND to receive WM_HOTKEY.
    // Do not wait for Show()/SourceInitialized to register the shortcut.
    internal void InitializeBackgroundServices() => RegisterCurrentHotkey();

    private void ShowSettings()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void RegisterCurrentHotkey()
    {
        if (_source == null)
        {
            var handle = new WindowInteropHelper(this).EnsureHandle();
            _source = HwndSource.FromHwnd(handle)
                ?? throw new InvalidOperationException("无法创建快捷键消息窗口。");
            _source.AddHook(WndProc);
        }

        if (!_hotkeyRegistered)
        {
            _hotkeyRegistered = RegisterHotKey(_source.Handle, HotkeyId, _modifiers, _key);
            if (!_hotkeyRegistered)
            {
                var error = Marshal.GetLastWin32Error();
                _tray.ShowBalloonTip(5000, "Snaply", $"截图快捷键注册失败（错误 {error}），可能已被其他软件占用。请打开设置更换快捷键。", ToolTipIcon.Warning);
            }
        }
    }

    private void UnregisterCurrentHotkey()
    {
        if (_source == null || !_hotkeyRegistered) return;
        UnregisterHotKey(_source.Handle, HotkeyId);
        _hotkeyRegistered = false;
    }

    private IntPtr WndProc(IntPtr window, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == 0x0312 && wParam.ToInt32() == HotkeyId)
        {
            Dispatcher.BeginInvoke(CaptureAndCopy);
            handled = true;
        }
        return IntPtr.Zero;
    }

    private void CaptureClick(object sender, RoutedEventArgs e) => CaptureAndCopy();

    private void SaveClick(object sender, RoutedEventArgs e)
    {
        if (!TryParseHotkey(HotkeyText.Text, out var modifiers, out var key))
        {
            System.Windows.MessageBox.Show("请先按下有效的组合键。", "Snaply");
            return;
        }

        UnregisterCurrentHotkey();
        _modifiers = modifiers;
        _key = key;
        ShortcutSettings.Save(HotkeyText.Text);
        StartupManager.SetEnabled(StartupCheck.IsChecked == true);
        RegisterCurrentHotkey();
        System.Windows.MessageBox.Show("设置已保存。", "Snaply");
    }

    private void HotkeyGotFocus(object sender, RoutedEventArgs e)
    {
        _recordingHotkey = true;
        UnregisterCurrentHotkey();
        HotkeyText.Text = "请按快捷键…";
    }

    private void HotkeyLostFocus(object sender, RoutedEventArgs e)
    {
        _recordingHotkey = false;
        if (!TryParseHotkey(HotkeyText.Text, out _, out _))
            HotkeyText.Text = ShortcutSettings.Load();
        RegisterCurrentHotkey();
    }

    private void HotkeyPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (!_recordingHotkey) return;
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return;

        var parts = new List<string>();
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) parts.Add("Ctrl");
        if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0) parts.Add("Alt");
        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) parts.Add("Shift");
        if ((Keyboard.Modifiers & ModifierKeys.Windows) != 0) parts.Add("Win");
        parts.Add(key.ToString());
        HotkeyText.Text = string.Join("+", parts);
    }

    private static bool TryParseHotkey(string value, out uint modifiers, out uint key)
    {
        modifiers = 0;
        key = 0;
        var parts = value.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2) return false;

        foreach (var part in parts[..^1])
        {
            switch (part.ToUpperInvariant())
            {
                case "CTRL": case "CONTROL": modifiers |= 2; break;
                case "ALT": modifiers |= 1; break;
                case "SHIFT": modifiers |= 4; break;
                case "WIN": case "WINDOWS": modifiers |= 8; break;
                default: return false;
            }
        }

        var finalKey = parts[^1].ToUpperInvariant();
        if (finalKey.Length == 1 && char.IsLetterOrDigit(finalKey[0])) key = finalKey[0];
        else if (Enum.TryParse<Key>(finalKey, true, out var parsed)) key = (uint)KeyInterop.VirtualKeyFromKey(parsed);
        return modifiers != 0 && key != 0;
    }

    private void CaptureAndCopy()
    {
        // Keep one editing/capture session; repeated hotkeys must not stack overlays.
        if (System.Windows.Application.Current.Windows.OfType<Window>().Any(window => window is SelectionWindow or ScrollCaptureWindow)) return;
        try
        {
            var bounds = SystemInformation.VirtualScreen;
            using var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppPArgb);
            using (var graphics = Graphics.FromImage(bitmap))
                graphics.CopyFromScreen(bounds.Location, System.Drawing.Point.Empty, bounds.Size, CopyPixelOperation.SourceCopy);
            new SelectionWindow(new Bitmap(bitmap), bounds).ShowDialog();
        }
        catch (Exception exception)
        {
            _tray.ShowBalloonTip(3000, "Snaply", "截图启动失败，请重新尝试。", ToolTipIcon.Error);
            System.Diagnostics.Debug.WriteLine(exception);
        }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_exitRequested) { e.Cancel = true; Hide(); return; }
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        UnregisterCurrentHotkey();
        _tray.Visible = false;
        _tray.Dispose();
        base.OnClosed(e);
        System.Windows.Application.Current.Shutdown();
    }
}
