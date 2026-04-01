using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using AprNesAvalonia.Platform;

namespace AprNesAvalonia.Views;

public partial class ConfigWindow : Window
{
    private readonly IniFile _ini;
    private readonly IGamepadBackend? _gamepad;

    // VK code ↔ Avalonia Key
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

    // P1 keyboard VK codes
    private int _p1_A, _p1_B, _p1_SELECT, _p1_START, _p1_UP, _p1_DOWN, _p1_LEFT, _p1_RIGHT;
    // P2 keyboard VK codes
    private int _p2_A, _p2_B, _p2_SELECT, _p2_START, _p2_UP, _p2_DOWN, _p2_LEFT, _p2_RIGHT;

    // The button currently waiting for a keypress
    private Button? _capturingButton;

    public ConfigWindow(IniFile ini, IGamepadBackend? gamepad = null)
    {
        InitializeComponent();
        _ini = ini;
        _gamepad = gamepad;
        KeyDown += OnWindowKeyDown;
        VolumeSlider.ValueChanged += (_, _) => UpdateVolumeLabel();
        LoadFromIni();
        ApplyLanguage();
        UpdateAnalogEnableState();

        // Wire up interactive events
        LangCombo.SelectionChanged += LangCombo_SelectionChanged;
        ChkAnalog.Click += (_, _) => UpdateAnalogEnableState();
        ChkUltraAnalog.Click += (_, _) => UpdateAnalogEnableState();
        CmbFilter1.SelectionChanged += CmbFilter_Changed;
        CmbFilter2.SelectionChanged += CmbFilter_Changed;
        CmbAnalogSize.SelectionChanged += (_, _) => UpdateResolutionLabel();
        UpdateResolutionLabel();
    }

    private string L(string key, string def) => LangHelper.Get(LangHelper.CurrentLang, key, def);

    private void ApplyLanguage()
    {
        if (!LangHelper.Loaded) return;

        Title = L("cfg_title", "Configuration");

        // Tabs
        TabP1.Header       = L("cfg_p1_input", "P1 Input");
        TabP2.Header       = L("cfg_p2_input", "P2 Input");
        TabGraphics.Header = L("cfg_graphics", "Graphics");
        TabAudio.Header    = L("cfg_audio",    "Audio");
        TabGeneral.Header  = L("cfg_general",  "General");

        // Input tabs
        LblP1Keyboard.Text = L("cfg_keyboard", "Keyboard");
        LblP1Gamepad.Text  = L("cfg_gamepad",  "Gamepad");
        LblP2Keyboard.Text = L("cfg_keyboard", "Keyboard");
        LblP2Gamepad.Text  = L("cfg_gamepad",  "Gamepad");

        // Graphics tab
        LblScreenFilters.Text = L("cfg_screen_filters", "Screen Filters");
        LblStage1.Text        = L("cfg_stage1", "Stage 1");
        LblStage2.Text        = L("cfg_stage2", "Stage 2");
        ChkScanline.Content   = L("cfg_scanline", "Scanline");
        LblOutput.Text        = L("cfg_output", "Output");
        LblAnalogVideo.Text   = L("cfg_analog_video", "Analog Video");
        ChkAnalog.Content     = L("cfg_enable_analog", "Enable Analog Mode");
        ChkUltraAnalog.Content = L("cfg_ultra_analog", "Ultra Analog");
        LblSize.Text          = L("cfg_size", "Size");
        LblInput.Text         = L("cfg_input", "Input");
        ChkCRT.Content        = L("cfg_crt_effect", "CRT Effect");
        BtnAnalogAdvanced.Content = L("cfg_advanced_settings", "Advanced Settings...");

        // Audio tab
        ChkSound.Content      = L("cfg_enable_sound", "Enable Sound");
        LblMode.Text           = L("cfg_mode", "Mode");
        LblVolumeLabel.Text    = L("cfg_volume", "Volume");
        BtnAudioAdvanced.Content = L("cfg_advanced_audio", "Advanced Audio Settings...");

        // Audio mode items
        if (CmbAudioMode.Items.Count >= 3)
        {
            ((ComboBoxItem)CmbAudioMode.Items[0]!).Content = "0 - " + L("audio_mode_pure", "Pure Digital");
            ((ComboBoxItem)CmbAudioMode.Items[1]!).Content = "1 - " + L("audio_mode_authentic", "Authentic");
            ((ComboBoxItem)CmbAudioMode.Items[2]!).Content = "2 - " + L("audio_mode_modern", "Modern");
        }

        // General tab
        LblLanguage.Text       = L("cfg_language", "Language");
        LblEmulation.Text      = L("cfg_emulation", "Emulation");
        ChkLimitFps.Content    = L("cfg_limit_fps", "Limit FPS");
        ChkAccuracyOptA.Content = L("cfg_accuracy_opta", "AccuracyOptA (per-dot sprite evaluation)");
        LblScreenshotPath.Text = L("cfg_screenshot_path", "Screenshot Path (Shift+P)");
        BtnBrowseScreenshot.Content = L("browse", "Browse...");
        LblRecording.Text      = L("cfg_recording", "Recording");
        LblVideoPath.Text      = L("cfg_video_path", "Video Output Path");
        BtnBrowseVideo.Content = L("browse", "Browse...");
        LblAudioPath.Text      = L("cfg_audio_path", "Audio Output Path");
        BtnBrowseAudio.Content = L("browse", "Browse...");
        LblVideoQuality.Text   = L("cfg_video_quality", "Video Quality");
        LblAudioBitrate.Text   = L("cfg_audio_bitrate", "Audio Bitrate");
        CmbVQ_Low.Content      = L("cfg_quality_low", "Low");
        CmbVQ_Med.Content      = L("cfg_quality_medium", "Medium");
        CmbVQ_High.Content     = L("cfg_quality_high", "High");
        CmbVQ_Ultra.Content    = L("cfg_quality_ultra", "Ultra");
        BtnOK.Content          = L("ok", "OK");
    }

    // ── Load INI → UI ───────────────────────────────────────────────────

    private void LoadFromIni()
    {
        // P1 Keyboard
        _p1_A      = _ini.GetInt("key_A",      90); P1_KB_A.Content      = VkName(_p1_A);
        _p1_B      = _ini.GetInt("key_B",      88); P1_KB_B.Content      = VkName(_p1_B);
        _p1_SELECT = _ini.GetInt("key_SELECT", 83); P1_KB_SELECT.Content = VkName(_p1_SELECT);
        _p1_START  = _ini.GetInt("key_START",  65); P1_KB_START.Content  = VkName(_p1_START);
        _p1_UP     = _ini.GetInt("key_UP",     38); P1_KB_UP.Content     = VkName(_p1_UP);
        _p1_DOWN   = _ini.GetInt("key_DOWN",   40); P1_KB_DOWN.Content   = VkName(_p1_DOWN);
        _p1_LEFT   = _ini.GetInt("key_LEFT",   37); P1_KB_LEFT.Content   = VkName(_p1_LEFT);
        _p1_RIGHT  = _ini.GetInt("key_RIGHT",  39); P1_KB_RIGHT.Content  = VkName(_p1_RIGHT);

        // P2 Keyboard
        _p2_A      = _ini.GetInt("key_P2_A",      0); P2_KB_A.Content      = VkName(_p2_A);
        _p2_B      = _ini.GetInt("key_P2_B",      0); P2_KB_B.Content      = VkName(_p2_B);
        _p2_SELECT = _ini.GetInt("key_P2_SELECT", 0); P2_KB_SELECT.Content = VkName(_p2_SELECT);
        _p2_START  = _ini.GetInt("key_P2_START",  0); P2_KB_START.Content  = VkName(_p2_START);
        _p2_UP     = _ini.GetInt("key_P2_UP",     0); P2_KB_UP.Content     = VkName(_p2_UP);
        _p2_DOWN   = _ini.GetInt("key_P2_DOWN",   0); P2_KB_DOWN.Content   = VkName(_p2_DOWN);
        _p2_LEFT   = _ini.GetInt("key_P2_LEFT",   0); P2_KB_LEFT.Content   = VkName(_p2_LEFT);
        _p2_RIGHT  = _ini.GetInt("key_P2_RIGHT",  0); P2_KB_RIGHT.Content  = VkName(_p2_RIGHT);

        // P1/P2 Gamepad — display only the button name (2nd field of "id,name,raw[,val]")
        P1_GP_A.Content      = JoypadDisplayName(_ini.Get("joypad_A",      "--"));
        P1_GP_B.Content      = JoypadDisplayName(_ini.Get("joypad_B",      "--"));
        P1_GP_SELECT.Content = JoypadDisplayName(_ini.Get("joypad_SELECT", "--"));
        P1_GP_START.Content  = JoypadDisplayName(_ini.Get("joypad_START",  "--"));
        P1_GP_UP.Content     = JoypadDisplayName(_ini.Get("joypad_UP",     "--"));
        P1_GP_DOWN.Content   = JoypadDisplayName(_ini.Get("joypad_DOWN",   "--"));
        P1_GP_LEFT.Content   = JoypadDisplayName(_ini.Get("joypad_LEFT",   "--"));
        P1_GP_RIGHT.Content  = JoypadDisplayName(_ini.Get("joypad_RIGHT",  "--"));

        P2_GP_A.Content      = JoypadDisplayName(_ini.Get("joypad_P2_A",      "--"));
        P2_GP_B.Content      = JoypadDisplayName(_ini.Get("joypad_P2_B",      "--"));
        P2_GP_SELECT.Content = JoypadDisplayName(_ini.Get("joypad_P2_SELECT", "--"));
        P2_GP_START.Content  = JoypadDisplayName(_ini.Get("joypad_P2_START",  "--"));
        P2_GP_UP.Content     = JoypadDisplayName(_ini.Get("joypad_P2_UP",     "--"));
        P2_GP_DOWN.Content   = JoypadDisplayName(_ini.Get("joypad_P2_DOWN",   "--"));
        P2_GP_LEFT.Content   = JoypadDisplayName(_ini.Get("joypad_P2_LEFT",   "--"));
        P2_GP_RIGHT.Content  = JoypadDisplayName(_ini.Get("joypad_P2_RIGHT",  "--"));

        // Graphics
        CmbFilter1.SelectedIndex  = _ini.GetInt("ResizeStage1", 0);
        CmbFilter2.SelectedIndex  = _ini.GetInt("ResizeStage2", 0);
        ChkScanline.IsChecked     = _ini.GetBool("Scanline", false);
        ChkAnalog.IsChecked       = _ini.GetBool("AnalogMode", false);
        ChkUltraAnalog.IsChecked  = _ini.GetBool("UltraAnalog", false);
        CmbAnalogSize.SelectedIndex = _ini.Get("AnalogSize", "4") switch
            { "2" => 0, "6" => 2, "8" => 3, _ => 1 };
        CmbAnalogInput.SelectedIndex = _ini.Get("AnalogOutput", "AV") switch
            { "RF" => 0, "SVideo" => 1, _ => 2 };
        ChkCRT.IsChecked = _ini.GetBool("crt", false);

        // Audio
        ChkSound.IsChecked       = _ini.GetBool("Sound", false);
        CmbAudioMode.SelectedIndex = _ini.GetInt("AudioMode", 0);
        VolumeSlider.Value       = _ini.GetInt("Volume", 80);
        UpdateVolumeLabel();

        // General
        ChkLimitFps.IsChecked     = _ini.GetBool("LimitFPS", false);
        ChkAccuracyOptA.IsChecked = _ini.GetBool("AccuracyOptA", true);
        TxtScreenshotPath.Text    = _ini.Get("CaptureScreenPath", "");

        string lang = _ini.Get("Lang", "zh-tw");
        LangCombo.SelectedIndex = lang switch { "zh-cn" => 1, "en-us" => 2, _ => 0 };

        // Recording
        TxtVideoPath.Text = _ini.Get("CaptureVideoPath", "");
        TxtAudioPath.Text = _ini.Get("CaptureAudioPath", "");
        // VideoQuality: INI stores 90/80/70/60 → index 0(Ultra)/1(High)/2(Medium)/3(Low)
        CmbVideoQuality.SelectedIndex = _ini.Get("VideoQuality", "90") switch
            { "60" => 0, "70" => 1, "80" => 2, _ => 3 };
        CmbAudioBitrate.SelectedIndex = _ini.Get("AudioBitrate", "160") switch
            { "128" => 0, "192" => 2, _ => 1 };
    }

    // ── Save UI → INI ───────────────────────────────────────────────────

    private void SaveToIni()
    {
        // P1 Keyboard
        _ini.Set("key_A",      _p1_A.ToString());
        _ini.Set("key_B",      _p1_B.ToString());
        _ini.Set("key_SELECT", _p1_SELECT.ToString());
        _ini.Set("key_START",  _p1_START.ToString());
        _ini.Set("key_UP",     _p1_UP.ToString());
        _ini.Set("key_DOWN",   _p1_DOWN.ToString());
        _ini.Set("key_LEFT",   _p1_LEFT.ToString());
        _ini.Set("key_RIGHT",  _p1_RIGHT.ToString());

        // P2 Keyboard
        _ini.Set("key_P2_A",      _p2_A.ToString());
        _ini.Set("key_P2_B",      _p2_B.ToString());
        _ini.Set("key_P2_SELECT", _p2_SELECT.ToString());
        _ini.Set("key_P2_START",  _p2_START.ToString());
        _ini.Set("key_P2_UP",     _p2_UP.ToString());
        _ini.Set("key_P2_DOWN",   _p2_DOWN.ToString());
        _ini.Set("key_P2_LEFT",   _p2_LEFT.ToString());
        _ini.Set("key_P2_RIGHT",  _p2_RIGHT.ToString());

        // P1/P2 Gamepad — save captured keys (or keep existing if not re-captured)
        SaveGpKey("P1_GP_A",      "joypad_A");
        SaveGpKey("P1_GP_B",      "joypad_B");
        SaveGpKey("P1_GP_SELECT", "joypad_SELECT");
        SaveGpKey("P1_GP_START",  "joypad_START");
        SaveGpKey("P1_GP_UP",     "joypad_UP");
        SaveGpKey("P1_GP_DOWN",   "joypad_DOWN");
        SaveGpKey("P1_GP_LEFT",   "joypad_LEFT");
        SaveGpKey("P1_GP_RIGHT",  "joypad_RIGHT");

        SaveGpKey("P2_GP_A",      "joypad_P2_A");
        SaveGpKey("P2_GP_B",      "joypad_P2_B");
        SaveGpKey("P2_GP_SELECT", "joypad_P2_SELECT");
        SaveGpKey("P2_GP_START",  "joypad_P2_START");
        SaveGpKey("P2_GP_UP",     "joypad_P2_UP");
        SaveGpKey("P2_GP_DOWN",   "joypad_P2_DOWN");
        SaveGpKey("P2_GP_LEFT",   "joypad_P2_LEFT");
        SaveGpKey("P2_GP_RIGHT",  "joypad_P2_RIGHT");

        // Graphics
        _ini.Set("ResizeStage1", CmbFilter1.SelectedIndex.ToString());
        _ini.Set("ResizeStage2", CmbFilter2.SelectedIndex.ToString());
        _ini.Set("Scanline",     ChkScanline.IsChecked == true ? "1" : "0");
        _ini.Set("AnalogMode",   ChkAnalog.IsChecked == true ? "1" : "0");
        _ini.Set("UltraAnalog",  ChkUltraAnalog.IsChecked == true ? "1" : "0");
        string analogSize = CmbAnalogSize.SelectedIndex switch { 0 => "2", 2 => "6", 3 => "8", _ => "4" };
        _ini.Set("AnalogSize", analogSize);
        string analogInput = CmbAnalogInput.SelectedIndex switch { 0 => "RF", 1 => "SVideo", _ => "AV" };
        _ini.Set("AnalogOutput", analogInput);
        _ini.Set("crt", ChkCRT.IsChecked == true ? "1" : "0");

        // Audio
        _ini.Set("Sound",     ChkSound.IsChecked == true ? "1" : "0");
        _ini.Set("AudioMode", CmbAudioMode.SelectedIndex.ToString());
        _ini.Set("Volume",    ((int)VolumeSlider.Value).ToString());

        // General
        _ini.Set("LimitFPS",         ChkLimitFps.IsChecked == true ? "1" : "0");
        _ini.Set("AccuracyOptA",     ChkAccuracyOptA.IsChecked == true ? "1" : "0");
        _ini.Set("CaptureScreenPath", TxtScreenshotPath.Text ?? "");

        // Recording
        _ini.Set("CaptureVideoPath", TxtVideoPath.Text ?? "");
        _ini.Set("CaptureAudioPath", TxtAudioPath.Text ?? "");
        // VideoQuality: index 0=Low(60), 1=Medium(70), 2=High(80), 3=Ultra(90)
        string videoQuality = CmbVideoQuality.SelectedIndex switch { 0 => "60", 1 => "70", 2 => "80", _ => "90" };
        _ini.Set("VideoQuality", videoQuality);
        string audioBitrate = CmbAudioBitrate.SelectedIndex switch { 0 => "128", 2 => "192", _ => "160" };
        _ini.Set("AudioBitrate", audioBitrate);

        string lang = LangCombo.SelectedIndex switch { 1 => "zh-cn", 2 => "en-us", _ => "zh-tw" };
        _ini.Set("Lang", lang);

        _ini.Save();
    }

    // ── Keyboard capture ────────────────────────────────────────────────

    private void KB_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        if (_capturingButton != null && _capturingButton != btn)
            _capturingButton.Content = VkName(GetVkFor(_capturingButton));
        _capturingButton = btn;
        btn.Content = "...";
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (_capturingButton == null) return;
        if (!_keyToVk.TryGetValue(e.Key, out int vk)) return;

        SetVkFor(_capturingButton, vk);
        _capturingButton.Content = VkName(vk);
        _capturingButton = null;
        e.Handled = true;
    }

    // ── Gamepad capture ─────────────────────────────────────────────────

    // Gamepad INI key strings (updated during capture, saved on OK)
    private readonly Dictionary<string, string> _gpIniKeys = new();

    private async void GP_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        if (_gamepad == null || !_gamepad.IsAvailable)
        {
            btn.Content = "N/A";
            return;
        }

        string prevContent = btn.Content?.ToString() ?? "--";
        btn.Content = "...";

        // Wait for button on background thread (5 second timeout)
        var result = await Task.Run(() => _gamepad.WaitForButton(5000));

        if (result != null)
        {
            btn.Content = result.DisplayName;
            _gpIniKeys[btn.Name!] = result.IniKey;
        }
        else
        {
            btn.Content = prevContent;
        }
    }

    // ── Language instant switch ──────────────────────────────────────────

    private void LangCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        string lang = LangCombo.SelectedIndex switch { 1 => "zh-cn", 2 => "en-us", _ => "zh-tw" };
        LangHelper.CurrentLang = lang;
        ApplyLanguage();
    }

    // ── Analog sub-controls enable/disable ───────────────────────────────

    private void UpdateAnalogEnableState()
    {
        bool analogOn = ChkAnalog.IsChecked == true;
        ChkUltraAnalog.IsEnabled    = analogOn;
        CmbAnalogSize.IsEnabled     = analogOn;
        CmbAnalogInput.IsEnabled    = analogOn;
        ChkCRT.IsEnabled            = analogOn;
        BtnAnalogAdvanced.IsEnabled = analogOn;

        // Analog mode disables digital filter controls
        CmbFilter1.IsEnabled  = !analogOn;
        CmbFilter2.IsEnabled  = !analogOn;
        ChkScanline.IsEnabled = !analogOn;

        // Ultra Analog sub-controls
        if (!analogOn)
        {
            ChkUltraAnalog.IsChecked = false;
        }

        UpdateResolutionLabel();
    }

    // ── Filter resolution ────────────────────────────────────────────────

    // Scale factors matching ComboBox order — Stage 1: None,xBRZ2-6,ScaleX2-3,NN2-4
    private static readonly int[] _s1Scale = { 1, 2, 3, 4, 5, 6, 2, 3, 2, 3, 4 };
    // Stage 2: None,ScaleX2-3,NN2-4
    private static readonly int[] _s2Scale = { 1, 2, 3, 2, 3, 4 };

    private void CmbFilter_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (sender == CmbFilter1)
        {
            bool s1None = CmbFilter1.SelectedIndex == 0;
            CmbFilter2.IsEnabled = !s1None;
            if (s1None) CmbFilter2.SelectedIndex = 0;
        }
        UpdateResolutionLabel();
    }

    private static readonly int[] _analogSizeValues = { 2, 4, 6, 8 };

    private void UpdateResolutionLabel()
    {
        if (ChkAnalog.IsChecked == true)
        {
            int idx = CmbAnalogSize.SelectedIndex;
            int sz = (idx >= 0 && idx < _analogSizeValues.Length) ? _analogSizeValues[idx] : 4;
            LblResolution.Text = (256 * sz) + " × " + (210 * sz);
        }
        else
        {
            int i1 = CmbFilter1.SelectedIndex;
            int i2 = CmbFilter2.SelectedIndex;
            int s1 = (i1 >= 0 && i1 < _s1Scale.Length) ? _s1Scale[i1] : 1;
            int s2 = (i2 >= 0 && i2 < _s2Scale.Length) ? _s2Scale[i2] : 1;
            int w = 256 * s1 * s2;
            int h = 240 * s1 * s2;
            LblResolution.Text = w + " × " + h;
        }
    }

    // ── Audio UI ────────────────────────────────────────────────────────

    private void ChkSound_Click(object? sender, RoutedEventArgs e)
    {
        // Immediately toggle NesCore audio flag (takes effect on next frame)
        AprNes.NesCore.AudioEnabled = ChkSound.IsChecked == true;
    }

    private void UpdateVolumeLabel()
    {
        LblVolume.Text = ((int)VolumeSlider.Value).ToString();
    }

    // ── Advanced dialogs (UI shell) ─────────────────────────────────────

    private async void BtnAnalogAdvanced_Click(object? sender, RoutedEventArgs e)
    {
        await new AnalogConfigWindow().ShowDialog(this);
    }

    private async void BtnAudioAdvanced_Click(object? sender, RoutedEventArgs e)
    {
        await new AudioPlusConfigWindow().ShowDialog(this);
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    /// <summary>Extract display name from joypad INI value "joystick_id,displayName,raw_id[,value]"</summary>
    private static string JoypadDisplayName(string iniVal)
    {
        if (string.IsNullOrEmpty(iniVal) || iniVal == "--") return "--";
        var parts = iniVal.Split(',');
        return parts.Length >= 2 ? parts[1] : iniVal;
    }

    private void SaveGpKey(string buttonName, string iniKey)
    {
        if (_gpIniKeys.TryGetValue(buttonName, out string? val))
            _ini.Set(iniKey, val);
        // else: not re-captured, keep existing INI value
    }

    private static string VkName(int vk)
    {
        if (vk == 0) return "--";
        return _vkToKey.TryGetValue(vk, out var k) ? k.ToString() : vk.ToString();
    }

    private int GetVkFor(Button btn) => btn.Name switch
    {
        "P1_KB_A" => _p1_A, "P1_KB_B" => _p1_B, "P1_KB_SELECT" => _p1_SELECT, "P1_KB_START" => _p1_START,
        "P1_KB_UP" => _p1_UP, "P1_KB_DOWN" => _p1_DOWN, "P1_KB_LEFT" => _p1_LEFT, "P1_KB_RIGHT" => _p1_RIGHT,
        "P2_KB_A" => _p2_A, "P2_KB_B" => _p2_B, "P2_KB_SELECT" => _p2_SELECT, "P2_KB_START" => _p2_START,
        "P2_KB_UP" => _p2_UP, "P2_KB_DOWN" => _p2_DOWN, "P2_KB_LEFT" => _p2_LEFT, "P2_KB_RIGHT" => _p2_RIGHT,
        _ => 0
    };

    private void SetVkFor(Button btn, int vk)
    {
        switch (btn.Name)
        {
            case "P1_KB_A": _p1_A = vk; break; case "P1_KB_B": _p1_B = vk; break;
            case "P1_KB_SELECT": _p1_SELECT = vk; break; case "P1_KB_START": _p1_START = vk; break;
            case "P1_KB_UP": _p1_UP = vk; break; case "P1_KB_DOWN": _p1_DOWN = vk; break;
            case "P1_KB_LEFT": _p1_LEFT = vk; break; case "P1_KB_RIGHT": _p1_RIGHT = vk; break;
            case "P2_KB_A": _p2_A = vk; break; case "P2_KB_B": _p2_B = vk; break;
            case "P2_KB_SELECT": _p2_SELECT = vk; break; case "P2_KB_START": _p2_START = vk; break;
            case "P2_KB_UP": _p2_UP = vk; break; case "P2_KB_DOWN": _p2_DOWN = vk; break;
            case "P2_KB_LEFT": _p2_LEFT = vk; break; case "P2_KB_RIGHT": _p2_RIGHT = vk; break;
        }
    }

    // ── Events ──────────────────────────────────────────────────────────

    private async void BtnBrowseScreenshot_Click(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions { Title = L("dlg_select_screenshot_folder", "Select screenshot folder"), AllowMultiple = false });
        if (folders.Count > 0) TxtScreenshotPath.Text = folders[0].Path.LocalPath;
    }

    private async void BtnBrowseVideo_Click(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions { Title = L("dlg_select_video_folder", "Select video output folder"), AllowMultiple = false });
        if (folders.Count > 0) TxtVideoPath.Text = folders[0].Path.LocalPath;
    }

    private async void BtnBrowseAudio_Click(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions { Title = L("dlg_select_audio_folder", "Select audio output folder"), AllowMultiple = false });
        if (folders.Count > 0) TxtAudioPath.Text = folders[0].Path.LocalPath;
    }

    private void BtnOK_Click(object? sender, RoutedEventArgs e)
    {
        SaveToIni();
        Close();
    }
}
