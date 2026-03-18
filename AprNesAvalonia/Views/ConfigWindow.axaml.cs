using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace AprNesAvalonia.Views;

public partial class ConfigWindow : Window
{
    private readonly IniFile _ini;

    // VK code ↔ Avalonia Key (same table as EmulatorEngine._vkMap)
    private static readonly Dictionary<int, Key> _vkToKey = new()
    {
        { 37, Key.Left }, { 38, Key.Up }, { 39, Key.Right }, { 40, Key.Down },
        { 13, Key.Return }, { 32, Key.Space },
        { 65, Key.A },  { 66, Key.B },  { 67, Key.C },  { 68, Key.D },  { 69, Key.E },
        { 70, Key.F },  { 71, Key.G },  { 72, Key.H },  { 73, Key.I },  { 74, Key.J },
        { 75, Key.K },  { 76, Key.L },  { 77, Key.M },  { 78, Key.N },  { 79, Key.O },
        { 80, Key.P },  { 81, Key.Q },  { 82, Key.R },  { 83, Key.S },  { 84, Key.T },
        { 85, Key.U },  { 86, Key.V },  { 87, Key.W },  { 88, Key.X },  { 89, Key.Y },
        { 90, Key.Z },
    };
    private static readonly Dictionary<Key, int> _keyToVk;
    static ConfigWindow()
    {
        _keyToVk = new();
        foreach (var kv in _vkToKey) _keyToVk[kv.Value] = kv.Key;
    }

    // Current VK codes (updated when user captures a key)
    private int _vk_A, _vk_B, _vk_SELECT, _vk_START, _vk_UP, _vk_DOWN, _vk_LEFT, _vk_RIGHT;

    // The KB button currently waiting for a keypress (null = not capturing)
    private Button? _capturingButton;

    public ConfigWindow(IniFile ini)
    {
        InitializeComponent();
        _ini = ini;
        KeyDown += OnWindowKeyDown;
        VolumeSlider.ValueChanged += (_, _) => UpdateSoundLabel();
        LoadFromIni();
    }

    // ── Load current INI → UI (mirrors GBEMU_ConfigureUI_Shown) ───────────
    private void LoadFromIni()
    {
        // Keyboard mapping
        _vk_A      = _ini.GetInt("key_A",      90); KB_A.Content      = VkName(_vk_A);
        _vk_B      = _ini.GetInt("key_B",      88); KB_B.Content      = VkName(_vk_B);
        _vk_SELECT = _ini.GetInt("key_SELECT", 83); KB_SELECT.Content = VkName(_vk_SELECT);
        _vk_START  = _ini.GetInt("key_START",  65); KB_START.Content  = VkName(_vk_START);
        _vk_UP     = _ini.GetInt("key_UP",     38); KB_UP.Content     = VkName(_vk_UP);
        _vk_DOWN   = _ini.GetInt("key_DOWN",   40); KB_DOWN.Content   = VkName(_vk_DOWN);
        _vk_LEFT   = _ini.GetInt("key_LEFT",   37); KB_LEFT.Content   = VkName(_vk_LEFT);
        _vk_RIGHT  = _ini.GetInt("key_RIGHT",  39); KB_RIGHT.Content  = VkName(_vk_RIGHT);

        // Options
        ChkLimitFps.IsChecked     = _ini.GetBool("LimitFPS",    false);
        ChkAccuracyOptA.IsChecked = _ini.GetBool("AccuracyOptA", true);
        TxtScreenshotPath.Text    = _ini.Get("CaptureScreenPath", "");

        // Screen size
        int scale = _ini.GetInt("ScreenSize", 1);
        Scale1.IsChecked = scale == 1; Scale2.IsChecked = scale == 2;
        Scale3.IsChecked = scale == 3; Scale4.IsChecked = scale == 4;
        Scale5.IsChecked = scale == 5; Scale6.IsChecked = scale == 6;
        Scale8.IsChecked = scale == 8; Scale9.IsChecked = scale == 9;

        // Language
        string lang = _ini.Get("Lang", "zh-tw");
        LangCombo.SelectedIndex = lang switch { "zh-cn" => 1, "en-us" => 2, _ => 0 };

        // Sound
        ChkSound.IsChecked   = _ini.GetBool("Sound", false);
        VolumeSlider.Value   = _ini.GetInt("Volume", 80);
        UpdateSoundLabel();
    }

    // ── Save UI → INI + apply immediately (mirrors BeforClose) ────────────
    private void SaveToIni()
    {
        _ini.Set("key_A",      _vk_A.ToString());
        _ini.Set("key_B",      _vk_B.ToString());
        _ini.Set("key_SELECT", _vk_SELECT.ToString());
        _ini.Set("key_START",  _vk_START.ToString());
        _ini.Set("key_UP",     _vk_UP.ToString());
        _ini.Set("key_DOWN",   _vk_DOWN.ToString());
        _ini.Set("key_LEFT",   _vk_LEFT.ToString());
        _ini.Set("key_RIGHT",  _vk_RIGHT.ToString());

        _ini.Set("LimitFPS",         ChkLimitFps.IsChecked    == true ? "1" : "0");
        _ini.Set("AccuracyOptA",     ChkAccuracyOptA.IsChecked == true ? "1" : "0");
        _ini.Set("CaptureScreenPath", TxtScreenshotPath.Text ?? "");

        int scale = Scale2.IsChecked == true ? 2 : Scale3.IsChecked == true ? 3 :
                    Scale4.IsChecked == true ? 4 : Scale5.IsChecked == true ? 5 :
                    Scale6.IsChecked == true ? 6 : Scale8.IsChecked == true ? 8 :
                    Scale9.IsChecked == true ? 9 : 1;
        _ini.Set("ScreenSize", scale.ToString());

        string lang = LangCombo.SelectedIndex switch { 1 => "zh-cn", 2 => "en-us", _ => "zh-tw" };
        _ini.Set("Lang", lang);

        bool soundOn = ChkSound.IsChecked == true;
        _ini.Set("Sound",  soundOn ? "1" : "0");
        _ini.Set("Volume", ((int)VolumeSlider.Value).ToString());

        _ini.Save();
        // NesCore properties + audio are applied by MainWindow.ApplyIniSettings() after dialog closes
    }

    // ── Keyboard capture ──────────────────────────────────────────────────
    private void KB_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        // Cancel previous capture
        if (_capturingButton != null && _capturingButton != btn)
            _capturingButton.Content = VkName(GetVkFor(_capturingButton));
        _capturingButton = btn;
        btn.Content = "...";
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (_capturingButton == null) return;
        if (!_keyToVk.TryGetValue(e.Key, out int vk)) return;

        switch (_capturingButton.Name)
        {
            case "KB_A":      _vk_A      = vk; break;
            case "KB_B":      _vk_B      = vk; break;
            case "KB_SELECT": _vk_SELECT = vk; break;
            case "KB_START":  _vk_START  = vk; break;
            case "KB_UP":     _vk_UP     = vk; break;
            case "KB_DOWN":   _vk_DOWN   = vk; break;
            case "KB_LEFT":   _vk_LEFT   = vk; break;
            case "KB_RIGHT":  _vk_RIGHT  = vk; break;
        }
        _capturingButton.Content = VkName(vk);
        _capturingButton = null;
        e.Handled = true;
    }

    // ── Sound UI ──────────────────────────────────────────────────────────
    private void ChkSound_Click(object? sender, RoutedEventArgs e) => UpdateSoundLabel();

    private void UpdateSoundLabel()
    {
        bool on = ChkSound.IsChecked == true;
        ChkSound.Content = "音效 - " + (on ? ((int)VolumeSlider.Value + "%") : "關閉");
        VolumeSlider.IsEnabled = on;
    }

    // ── Helpers ───────────────────────────────────────────────────────────
    private static string VkName(int vk) =>
        _vkToKey.TryGetValue(vk, out var k) ? k.ToString() : vk.ToString();

    private int GetVkFor(Button btn) => btn.Name switch
    {
        "KB_A"      => _vk_A,      "KB_B"      => _vk_B,
        "KB_SELECT" => _vk_SELECT, "KB_START"  => _vk_START,
        "KB_UP"     => _vk_UP,     "KB_DOWN"   => _vk_DOWN,
        "KB_LEFT"   => _vk_LEFT,   "KB_RIGHT"  => _vk_RIGHT,
        _ => 0
    };

    // ── Event handlers ────────────────────────────────────────────────────
    private async void BtnBrowse_Click(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "選擇截圖存放目錄", AllowMultiple = false });
        if (folders.Count > 0) TxtScreenshotPath.Text = folders[0].Path.LocalPath;
    }

    private void BtnOK_Click(object? sender, RoutedEventArgs e)
    {
        SaveToIni();
        Close();
    }
}
