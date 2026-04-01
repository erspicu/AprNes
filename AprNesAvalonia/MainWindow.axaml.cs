using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using AprNesAvalonia.Views;

namespace AprNesAvalonia;

public partial class MainWindow : Window
{
    private readonly EmulatorEngine _emu = new();
    private readonly IniFile _ini;
    private readonly WriteableBitmap _writeableBitmap = new WriteableBitmap(
        new PixelSize(256, 240),
        new Vector(96, 96),
        PixelFormats.Bgra8888,
        AlphaFormat.Unpremul);

    private readonly DispatcherTimer _fpsTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    // State tracking for toggle menus
    private bool _soundEnabled;
    private bool _ultraAnalogEnabled;
    private bool _limitFPS;
    private bool _accuracyOptA = true;
    private bool _isFullscreen;
    private string _currentRegion = "NTSC";

    // Recent ROMs
    private readonly List<string> _recentROMs = new();
    private const int MaxRecentROMs = 10;

    public MainWindow()
    {
        InitializeComponent();

        // Load INI settings
        string iniPath = Path.Combine(AppContext.BaseDirectory, "configure", "AprNes.ini");
        _ini = new IniFile(iniPath);
        ApplyIniSettings();

        // Canvas
        GameCanvas.Source = _writeableBitmap;

        // Frame-ready
        _emu.FrameReady += OnFrameReady;

        // FPS display
        _fpsTimer.Tick += (_, _) => StatusFps.Text = _emu.TakeFrameCount().ToString();
        _fpsTimer.Start();

        // Keyboard input
        KeyDown += OnKeyDown;
        KeyUp   += OnKeyUp;

        // Drag & drop
        AddHandler(DragDrop.DropEvent, OnDrop);
        DragDrop.SetAllowDrop(this, true);

        Closing += (_, _) =>
        {
            StopRecordingIfActive();
            _emu.Dispose();        // Stop + SaveSRam + cleanup
            _writeableBitmap.Dispose();
        };

        AprNes.NesCore.OnError = LogError;

        // Init gamepad after window has a native handle
        Opened += (_, _) =>
        {
            var ph = TryGetPlatformHandle();
            IntPtr hwnd = ph?.Handle ?? IntPtr.Zero;
            _emu.InitGamepad(hwnd, _ini);
        };

        InitRecentROMs();
        UpdateMenuStates();
        UpdateRecordMenuVisibility();
    }

    // ═══ Settings ═══════════════════════════════════════════════════════════

    private void ApplyIniSettings()
    {
        _emu.ApplyKeyMap(
            _ini.GetInt("key_A",      90),
            _ini.GetInt("key_B",      88),
            _ini.GetInt("key_SELECT", 83),
            _ini.GetInt("key_START",  65),
            _ini.GetInt("key_UP",     38),
            _ini.GetInt("key_DOWN",   40),
            _ini.GetInt("key_LEFT",   37),
            _ini.GetInt("key_RIGHT",  39));

        _accuracyOptA = _ini.GetBool("AccuracyOptA", true);
        _limitFPS     = _ini.GetBool("LimitFPS",     false);
        _soundEnabled = _ini.GetBool("Sound",        false);
        _currentRegion = _ini.Get("Region", "NTSC");

        AprNes.NesCore.AccuracyOptA = _accuracyOptA;
        AprNes.NesCore.LimitFPS     = _limitFPS;
        ApplyRegionToNesCore(_currentRegion);

        _emu.ApplyAudioSettings(_soundEnabled, _ini.GetInt("Volume", 80));

        // Reload gamepad mapping
        _emu.ReloadGamepadMapping(_ini);

        // Load separate INI files for Analog and AudioPlus
        LoadAnalogConfig();
        LoadAudioPlusIni();

        ApplyLanguage(_ini.Get("Lang", LangHelper.CurrentLang));
        UpdateMenuStates();
    }

    // ═══ Analog Config INI ═══════════════════════════════════════════════
    private static string ConfigDir => Path.Combine(AppContext.BaseDirectory, "configure");

    private void LoadAnalogConfig()
    {
        string analogFile = Path.Combine(ConfigDir, "AprNesAnalog.ini");
        if (!File.Exists(analogFile)) return; // use NesCore defaults

        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string line in File.ReadAllLines(analogFile))
        {
            string t = line.TrimStart();
            if (t.StartsWith(';') || t.StartsWith('#')) continue;
            var kv = line.Split(new[] { '=' }, 2);
            if (kv.Length == 2) dict[kv[0].Trim()] = kv[1].Trim();
        }

        float Get(string key, float def) =>
            dict.TryGetValue(key, out var s) &&
            float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : def;

        bool GetBool(string key, bool def) =>
            dict.TryGetValue(key, out var s) ? (s == "1" || s.Equals("true", StringComparison.OrdinalIgnoreCase)) : def;

        // Effect toggles
        AprNes.NesCore.HbiSimulation    = GetBool("HbiSimulation", true);
        AprNes.NesCore.ColorBurstJitter = GetBool("ColorBurstJitter", true);
        AprNes.NesCore.SymmetricIQ      = GetBool("SymmetricIQ", true);
        AprNes.NesCore.InterlaceJitter  = GetBool("InterlaceJitter", true);

        bool ringingOn     = GetBool("RingingEnabled", true);
        bool vignetteOn    = GetBool("VignetteEnabled", true);
        bool shadowMaskOn  = GetBool("ShadowMaskEnabled", true);
        bool curvatureOn   = GetBool("CurvatureEnabled", true);
        bool phosphorOn    = GetBool("PhosphorEnabled", true);
        bool hbeamOn       = GetBool("HBeamEnabled", true);
        bool convergenceOn = GetBool("ConvergenceEnabled", true);

        // Effect values
        AprNes.NesCore.RingStrength  = ringingOn ? Get("RingStrength", 0.3f) : 0f;
        AprNes.NesCore.GammaCoeff   = Get("GammaCoeff", 0.229f);
        AprNes.NesCore.ColorTempR   = Get("ColorTempR", 1.0f);
        AprNes.NesCore.ColorTempG   = Get("ColorTempG", 1.0f);
        AprNes.NesCore.ColorTempB   = Get("ColorTempB", 1.0f);
        AprNes.NesCore.VignetteStrength   = vignetteOn ? Get("VignetteStrength", 0.15f) : 0f;
        AprNes.NesCore.ShadowMaskMode     = shadowMaskOn
            ? (AprNes.NesCore.CrtMaskType)(int)Get("ShadowMaskMode", 1f)
            : AprNes.NesCore.CrtMaskType.None;
        AprNes.NesCore.ShadowMaskStrength = Get("ShadowMaskStrength", 0.3f);
        AprNes.NesCore.CurvatureStrength  = curvatureOn ? Get("CurvatureStrength", 0.12f) : 0f;
        AprNes.NesCore.PhosphorDecay      = phosphorOn ? Get("PhosphorDecay", 0.15f) : 0f;
        AprNes.NesCore.HBeamSpread        = hbeamOn ? Get("HBeamSpread", 0.4f) : 0f;
        AprNes.NesCore.ConvergenceStrength = convergenceOn ? Get("ConvergenceStrength", 2.0f) : 0f;

        // Stage 1 connector
        AprNes.NesCore.RF_NoiseIntensity = Get("RF_NoiseIntensity", 0.04f);
        AprNes.NesCore.RF_SlewRate       = Get("RF_SlewRate",       0.60f);
        AprNes.NesCore.RF_ChromaBlur     = Get("RF_ChromaBlur",     0.10f);
        AprNes.NesCore.AV_NoiseIntensity = Get("AV_NoiseIntensity", 0.003f);
        AprNes.NesCore.AV_SlewRate       = Get("AV_SlewRate",       0.80f);
        AprNes.NesCore.AV_ChromaBlur     = Get("AV_ChromaBlur",     0.35f);
        AprNes.NesCore.SV_NoiseIntensity = Get("SV_NoiseIntensity", 0.00f);
        AprNes.NesCore.SV_SlewRate       = Get("SV_SlewRate",       0.90f);
        AprNes.NesCore.SV_ChromaBlur     = Get("SV_ChromaBlur",     0.45f);

        // Stage 2 connector
        AprNes.NesCore.RF_BeamSigma       = Get("RF_BeamSigma",       1.10f);
        AprNes.NesCore.RF_BloomStrength   = Get("RF_BloomStrength",   0.50f);
        AprNes.NesCore.RF_BrightnessBoost = Get("RF_BrightnessBoost", 1.10f);
        AprNes.NesCore.AV_BeamSigma       = Get("AV_BeamSigma",       0.85f);
        AprNes.NesCore.AV_BloomStrength   = Get("AV_BloomStrength",   0.25f);
        AprNes.NesCore.AV_BrightnessBoost = Get("AV_BrightnessBoost", 1.25f);
        AprNes.NesCore.SV_BeamSigma       = Get("SV_BeamSigma",       0.65f);
        AprNes.NesCore.SV_BloomStrength   = Get("SV_BloomStrength",   0.10f);
        AprNes.NesCore.SV_BrightnessBoost = Get("SV_BrightnessBoost", 1.40f);

        AprNes.NesCore.UpdateGammaLUT();
    }

    // ═══ AudioPlus INI ═══════════════════════════════════════════════════
    private readonly int[,]  _chipChVol = new int[7, 8];
    private readonly bool[,] _chipChEn  = new bool[7, 8];
    private static readonly string[] _chipIniPrefix = { "", "VRC6", "VRC7", "N163", "S5B", "MMC5", "FDS" };

    private void InitChipDefaults()
    {
        for (int c = 0; c < 7; c++)
            for (int i = 0; i < 8; i++)
            { _chipChVol[c, i] = 70; _chipChEn[c, i] = true; }
    }

    private void ApplyChipChannelSettings(int chipIdx)
    {
        if (chipIdx < 0 || chipIdx >= 7) chipIdx = 0;
        for (int i = 0; i < 8; i++)
        {
            AprNes.NesCore.ChannelVolume[5 + i]  = _chipChVol[chipIdx, i];
            AprNes.NesCore.ChannelEnabled[5 + i] = _chipChEn[chipIdx, i];
        }
    }

    private void LoadAudioPlusIni()
    {
        InitChipDefaults();
        string iniPath = Path.Combine(ConfigDir, "AprNesAudioPlus.ini");

        if (!File.Exists(iniPath)) return; // use NesCore defaults

        var cfg = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string line in File.ReadAllLines(iniPath))
        {
            string t = line.TrimStart();
            if (t.StartsWith(';') || t.StartsWith('#')) continue;
            var kv = line.Split(new[] { '=' }, 2);
            if (kv.Length == 2) cfg[kv[0].Trim()] = kv[1].Trim();
        }

        int ReadInt(string key, int def, int min, int max) =>
            cfg.TryGetValue(key, out var s) && int.TryParse(s, out int v)
                ? Math.Clamp(v, min, max) : def;

        // Authentic
        AprNes.NesCore.ConsoleModel    = ReadInt("ConsoleModel", 0, 0, 6);
        AprNes.NesCore.RfCrosstalk     = cfg.TryGetValue("RfCrosstalk", out var rc) && rc == "1";
        AprNes.NesCore.CustomLpfCutoff = ReadInt("CustomLpfCutoff", 14000, 1000, 22000);
        AprNes.NesCore.CustomBuzz      = cfg.TryGetValue("CustomBuzz", out var cb) && cb == "1";
        AprNes.NesCore.BuzzAmplitude   = ReadInt("BuzzAmplitude", 30, 0, 100);
        AprNes.NesCore.BuzzFreq        = ReadInt("BuzzFreq", 60, 50, 60);
        AprNes.NesCore.RfVolume        = ReadInt("RfVolume", 50, 0, 200);

        // Modern
        AprNes.NesCore.StereoWidth   = ReadInt("StereoWidth", 50, 0, 100);
        AprNes.NesCore.HaasDelay     = ReadInt("HaasDelay", 20, 10, 30);
        AprNes.NesCore.HaasCrossfeed = ReadInt("HaasCrossfeed", 40, 0, 80);
        AprNes.NesCore.ReverbWet     = ReadInt("ReverbWet", 0, 0, 30);
        AprNes.NesCore.CombFeedback  = ReadInt("CombFeedback", 70, 30, 90);
        AprNes.NesCore.CombDamp      = ReadInt("CombDamp", 30, 10, 70);
        AprNes.NesCore.BassBoostDb   = ReadInt("BassBoostDb", 0, 0, 12);
        AprNes.NesCore.BassBoostFreq = ReadInt("BassBoostFreq", 150, 80, 300);

        // NES channel volume
        string[] nesChKeys = { "Pulse1", "Pulse2", "Triangle", "Noise", "DMC" };
        for (int i = 0; i < 5; i++)
        {
            AprNes.NesCore.ChannelVolume[i] = ReadInt("ChVol_" + nesChKeys[i], 70, 0, 100);
            AprNes.NesCore.ChannelEnabled[i] = !(cfg.TryGetValue("ChEn_" + nesChKeys[i], out var en) && en == "0");
        }

        // Per-chip expansion channels
        for (int chip = 1; chip < _chipIniPrefix.Length; chip++)
        {
            for (int ch = 0; ch < 8; ch++)
            {
                string volKey = "ChVol_" + _chipIniPrefix[chip] + "_" + ch;
                string enKey  = "ChEn_"  + _chipIniPrefix[chip] + "_" + ch;
                if (cfg.TryGetValue(volKey, out var vs) && int.TryParse(vs, out int v))
                    _chipChVol[chip, ch] = Math.Clamp(v, 0, 100);
                if (cfg.TryGetValue(enKey, out var es))
                    _chipChEn[chip, ch] = es != "0";
            }
        }

        ApplyChipChannelSettings((int)AprNes.NesCore.expansionChipType);
    }

    private string L(string key, string def) => LangHelper.Get(LangHelper.CurrentLang, key, def);

    private void ApplyLanguage(string lang)
    {
        LangHelper.CurrentLang = lang;
        if (!LangHelper.Loaded) return;

        // Menu bar headers
        MenuFile.Header      = L("menu_file",      "_File");
        MenuEmulation.Header = L("menu_emulation", "_Emulation");
        MenuView.Header      = L("menu_view",      "_View");
        MenuTools.Header     = L("menu_tools",     "_Tools");
        MenuHelp.Header      = L("menu_help",      "_Help");

        // File menu
        MenuOpenRom.Header    = L("menu_open_rom", "Open ROM");
        MenuRecentRoms.Header = L("menu_recent",   "Recent ROMs");
        MenuExit.Header       = L("menu_exit",     "Exit");

        // Emulation menu
        MenuSoftReset.Header = L("menu_soft_reset", "Soft Reset");
        MenuHardReset.Header = L("menu_hard_reset", "Hard Reset");
        MenuRegion.Header    = L("menu_region",     "Region");

        // Tools menu
        MenuScreenshot.Header     = L("menu_screenshot",      "Screenshot");
        MenuRomInfo.Header        = L("menu_rom_info",        "ROM Info");
        MenuRecord.Header         = L("menu_record",          "Record");
        MenuRecordVideo.Header    = L("menu_record_video",    "Record Video");
        MenuRecordAudio.Header    = L("menu_record_audio",    "Record Audio");
        MenuRecordSettings.Header = L("menu_record_settings", "Record Settings");
        MenuConfiguration.Header  = L("menu_configuration",   "Configuration");

        // View menu
        MenuFullscreen.Header = L("menu_fullscreen", "Fullscreen");

        // Help menu
        MenuShortcuts.Header = L("menu_shortcuts", "Keyboard Shortcuts");
        MenuAbout.Header     = L("menu_about",     "About");

        // Context menu
        CtxOpenRom.Header    = L("menu_open_rom",      "Open ROM");
        CtxSoftReset.Header  = L("menu_soft_reset",    "Soft Reset");
        CtxHardReset.Header  = L("menu_hard_reset",    "Hard Reset");
        CtxConfig.Header     = L("menu_configuration", "Configuration");
        CtxRomInfo.Header    = L("menu_rom_info",      "ROM Info");
        CtxFullscreen.Header = L("menu_fullscreen",    "Fullscreen");
        CtxExit.Header       = L("menu_exit",          "Exit");

        // Status bar
        if (!_emu.IsRomLoaded)
            StatusRomName.Text = L("status_no_rom", "No ROM loaded");
        StatusFpsLabel.Text = L("status_fps", "FPS:") + " ";
    }

    private void UpdateMenuStates()
    {
        string limitText = L("menu_limit_fps", "Limit FPS");
        string optaText  = L("menu_accuracy_opta", "AccuracyOptA");
        MenuLimitFPS.Header     = _limitFPS     ? "✓ " + limitText : "  " + limitText;
        MenuAccuracyOptA.Header = _accuracyOptA ? "✓ " + optaText  : "  " + optaText;

        string soundText = _soundEnabled ? L("menu_sound_on", "Sound: ON") : L("menu_sound_off", "Sound: OFF");
        MenuSound.Header = soundText;
        CtxSound.Header  = soundText;

        string analogText = _ultraAnalogEnabled
            ? L("menu_ultra_analog_on", "Ultra Analog: ON")
            : L("menu_ultra_analog_off", "Ultra Analog: OFF");
        MenuUltraAnalog.Header = analogText;
        CtxUltraAnalog.Header  = analogText;

        // Region radio — mark current
        MenuRegionNTSC.Header  = _currentRegion == "NTSC"  ? "● NTSC"  : "  NTSC";
        MenuRegionPAL.Header   = _currentRegion == "PAL"   ? "● PAL"   : "  PAL";
        MenuRegionDendy.Header = _currentRegion == "Dendy" ? "● Dendy" : "  Dendy";

        StatusRegion.Text = _currentRegion;
    }

    // ═══ Error logging ═════════════════════════════════════════════════════

    private static readonly string LogFile = Path.Combine(AppContext.BaseDirectory, "AprNes.log");

    private static void LogError(string msg)
    {
        try
        {
            string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + msg + Environment.NewLine;
            File.AppendAllText(LogFile, line);
        }
        catch { }
    }

    // ═══ Frame rendering ════════════════════════════════════════════════════

    private unsafe void OnFrameReady()
    {
        var src = _emu.FrameBuffer;
        int size = src.Length;
        fixed (byte* p = src)
        using (var fb = _writeableBitmap.Lock())
            Buffer.MemoryCopy(p, fb.Address.ToPointer(), size, size);
        GameCanvas.InvalidateVisual();
    }

    // ═══ Keyboard ═══════════════════════════════════════════════════════════

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        // Shift+P = screenshot
        if (e.KeyModifiers == KeyModifiers.Shift && e.Key == Key.P)
        {
            CaptureScreen();
            e.Handled = true;
            return;
        }
        // F11 = fullscreen
        if (e.Key == Key.F11)
        {
            ToggleFullscreen();
            e.Handled = true;
            return;
        }
        // Escape = exit fullscreen
        if (e.Key == Key.Escape && _isFullscreen)
        {
            ToggleFullscreen();
            e.Handled = true;
            return;
        }
        _emu.KeyDown(e.Key);
    }

    private void OnKeyUp(object? sender, KeyEventArgs e) => _emu.KeyUp(e.Key);

    // ═══ Drag & Drop ════════════════════════════════════════════════════════

    private void OnDrop(object? sender, DragEventArgs e)
    {
        var files = e.Data.GetFiles();
        if (files == null) return;

        foreach (var item in files)
        {
            var path = item.TryGetLocalPath();
            if (string.IsNullOrEmpty(path)) continue;
            if (!path.EndsWith(".nes", StringComparison.OrdinalIgnoreCase) &&
                !path.EndsWith(".fds", StringComparison.OrdinalIgnoreCase) &&
                !path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) continue;

            LoadAndStartRom(path);
            break;
        }
    }

    // ═══ ROM loading helper ═════════════════════════════════════════════════

    private void LoadAndStartRom(string path)
    {
        _emu.Stop();
        if (!_emu.LoadRom(path)) return;

        StatusRomName.Text = Path.GetFileName(path);
        _emu.Start();
        AddRecentROM(path);
    }

    // ═══ Fullscreen ═════════════════════════════════════════════════════════

    private void ToggleFullscreen()
    {
        _isFullscreen = !_isFullscreen;
        if (_isFullscreen)
        {
            WindowState = WindowState.FullScreen;
            // Scale canvas to fill
            GameCanvas.Width  = double.NaN;
            GameCanvas.Height = double.NaN;
            GameCanvas.Stretch = Avalonia.Media.Stretch.Uniform;
            GameBorder.Margin = new Thickness(0);
        }
        else
        {
            WindowState = WindowState.Normal;
            GameCanvas.Width  = 256;
            GameCanvas.Height = 240;
            GameCanvas.Stretch = Avalonia.Media.Stretch.Fill;
            GameBorder.Margin = new Thickness(0);
        }
    }

    // ═══ Screenshot ═════════════════════════════════════════════════════════

    private volatile bool _capturing;

    private async void CaptureScreen()
    {
        if (!_emu.IsRunning || _capturing) return;
        _capturing = true;
        _emu.Pause();

        string message;
        try
        {
            string dir = _ini.Get("CaptureScreenPath",
                         Path.Combine(AppContext.BaseDirectory, "Screenshot"));
            Directory.CreateDirectory(dir);

            string stamp    = DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss");
            string filePath = Path.Combine(dir, $"Screen-{stamp}.png");

            var src = _emu.FrameBuffer;
            unsafe
            {
                fixed (byte* p = src)
                {
                    var bmp = new Bitmap(
                        PixelFormats.Bgra8888, AlphaFormat.Unpremul,
                        (nint)p,
                        new PixelSize(256, 240),
                        new Vector(96, 96),
                        256 * 4);
                    using var fs = File.Create(filePath);
                    bmp.Save(fs);
                    bmp.Dispose();
                }
            }
            message = filePath + " " + L("dlg_screenshot_saved", "save!");
        }
        catch (Exception ex) { message = L("dlg_screenshot_failed", "Screenshot failed:") + " " + ex.Message; }

        _emu.Resume();
        _capturing = false;
        await ShowMessageBox(message);
    }

    private async System.Threading.Tasks.Task ShowMessageBox(string message)
    {
        var win = new Window
        {
            Title = "AprNes",
            Width = 420, SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(16), Spacing = 12,
                Children =
                {
                    new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                    new Button    { Content = "OK",
                                   HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                                   Padding = new Thickness(20, 4) }
                }
            }
        };
        ((win.Content as StackPanel)!.Children[1] as Button)!.Click += (_, _) => win.Close();
        await win.ShowDialog(this);
    }

    // ═══ Menu: File ═════════════════════════════════════════════════════════

    private async void MenuOpenRom_Click(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = L("dlg_open_rom", "Open ROM"),
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType(L("dlg_nes_rom", "NES ROM")) { Patterns = ["*.nes", "*.fds", "*.zip"] },
                              new FilePickerFileType(L("dlg_all_files", "All Files")) { Patterns = ["*"] }]
        });
        if (files.Count == 0) return;

        string path = files[0].TryGetLocalPath() ?? string.Empty;
        if (string.IsNullOrEmpty(path)) return;

        LoadAndStartRom(path);
    }

    private void MenuExit_Click(object? sender, RoutedEventArgs e) => Close();

    // ═══ Menu: Emulation ════════════════════════════════════════════════════

    private void MenuSoftReset_Click(object? sender, RoutedEventArgs e)
    {
        if (!_emu.IsRomLoaded) return;
        AprNes.NesCore.SoftReset();
        if (!_emu.IsRunning) _emu.Start();
    }

    private void MenuHardReset_Click(object? sender, RoutedEventArgs e)
    {
        if (!_emu.IsRomLoaded) return;
        StopRecordingIfActive();
        if (!_emu.HardReset()) return;
        _emu.Start();
    }

    private void MenuRegion_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem mi || mi.Tag is not string region) return;
        if (_currentRegion == region) return;

        _currentRegion = region;
        StopRecordingIfActive(true);
        ApplyRegionToNesCore(region);
        _ini.Set("Region", region);
        _ini.Save();
        UpdateMenuStates();

        // Region change requires hard reset (like AprNes WinForms)
        if (_emu.IsRunning)
        {
            _emu.HardReset();
            _emu.Start();
        }
    }

    private static void ApplyRegionToNesCore(string region)
    {
        AprNes.NesCore.Region = region switch
        {
            "PAL"   => AprNes.NesCore.RegionType.PAL,
            "Dendy" => AprNes.NesCore.RegionType.Dendy,
            _       => AprNes.NesCore.RegionType.NTSC,
        };
    }

    private void MenuLimitFPS_Click(object? sender, RoutedEventArgs e)
    {
        _limitFPS = !_limitFPS;
        AprNes.NesCore.LimitFPS = _limitFPS;
        _ini.Set("LimitFPS", _limitFPS ? "1" : "0");
        _ini.Save();
        UpdateMenuStates();
    }

    private void MenuAccuracyOptA_Click(object? sender, RoutedEventArgs e)
    {
        _accuracyOptA = !_accuracyOptA;
        AprNes.NesCore.AccuracyOptA = _accuracyOptA;
        _ini.Set("AccuracyOptA", _accuracyOptA ? "1" : "0");
        _ini.Save();
        UpdateMenuStates();
    }

    // ═══ Menu: View ═════════════════════════════════════════════════════════

    private void MenuFullscreen_Click(object? sender, RoutedEventArgs e) => ToggleFullscreen();

    private void MenuSound_Click(object? sender, RoutedEventArgs e)
    {
        _soundEnabled = !_soundEnabled;
        _emu.ApplyAudioSettings(_soundEnabled, _ini.GetInt("Volume", 80));
        _ini.Set("Sound", _soundEnabled ? "1" : "0");
        _ini.Save();
        UpdateMenuStates();
    }

    private void MenuUltraAnalog_Click(object? sender, RoutedEventArgs e)
    {
        StopRecordingIfActive(true);
        _ultraAnalogEnabled = !_ultraAnalogEnabled;
        AprNes.NesCore.UltraAnalog = _ultraAnalogEnabled;
        _ini.Set("UltraAnalog", _ultraAnalogEnabled ? "1" : "0");
        _ini.Save();
        UpdateMenuStates();

        // Sync rendering pipeline if running in Analog mode
        if (_emu.IsRunning && AprNes.NesCore.AnalogEnabled)
        {
            _emu.Pause();
            AprNes.NesCore.SyncAnalogConfig();
            AprNes.NesCore.Ntsc_Init();
            AprNes.NesCore.Crt_Init();
            _emu.Resume();
        }
    }

    // ═══ Menu: Tools ════════════════════════════════════════════════════════

    private void MenuScreenshot_Click(object? sender, RoutedEventArgs e) => CaptureScreen();

    private async void MenuRomInfo_Click(object? sender, RoutedEventArgs e)
    {
        var dlg = new RomInfoWindow();
        dlg.SetInfo(_emu.GetRomInfo());
        await dlg.ShowDialog(this);
    }

    // ── Recording ──────────────────────────────────────────────────────────
    private static string? _ffmpegPath;

    private static string GetFfmpegPath()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg", "ffmpeg.exe");
        _ffmpegPath = File.Exists(path) ? path : "";
        return _ffmpegPath;
    }

    private void UpdateRecordMenuVisibility()
    {
        bool hasFfmpeg = !string.IsNullOrEmpty(GetFfmpegPath());
        MenuRecord.IsVisible = hasFfmpeg;
        if (hasFfmpeg)
        {
            bool videoRec = AprNes.VideoRecorder.IsRecording;
            bool audioRec = AprNes.AudioRecorder.IsRecording;

            MenuRecordVideo.Header = videoRec ? "■ Stop Recording" : "● Record Video";
            MenuRecordVideo.IsEnabled = videoRec || (_emu.IsRunning && !audioRec);

            MenuRecordAudio.Header = audioRec ? "■ Stop Audio Recording" : "● Record Audio";
            MenuRecordAudio.IsEnabled = audioRec || (_emu.IsRunning && !videoRec);
        }
    }

    private async void MenuRecordVideo_Click(object? sender, RoutedEventArgs e)
    {
        if (AprNes.VideoRecorder.IsRecording)
        {
            AprNes.VideoRecorder.Stop();
            UpdateRecordMenuVisibility();
            Title = "AprNes";
            return;
        }

        if (!_emu.IsRunning) return;

        string capturesDir = _ini.Get("CaptureVideoPath",
            Path.Combine(AppContext.BaseDirectory, "Captures", "Video"));
        int recW = AprNes.NesCore.AnalogEnabled ? AprNes.NesCore.Crt_DstW : AprNes.NesCore.RenderOutputW;
        int recH = AprNes.NesCore.AnalogEnabled ? AprNes.NesCore.Crt_DstH : AprNes.NesCore.RenderOutputH;
        bool ok = AprNes.VideoRecorder.Start(GetFfmpegPath(), capturesDir, recW, recH);
        if (ok)
        {
            UpdateRecordMenuVisibility();
            Title = "AprNes [REC]";
        }
        else
        {
            string err = AprNes.VideoRecorder.LastError ?? "Unknown error";
            await ShowMessageBox("Failed to start recording.\n\n" + err);
        }
    }

    private async void MenuRecordAudio_Click(object? sender, RoutedEventArgs e)
    {
        if (AprNes.AudioRecorder.IsRecording)
        {
            AprNes.AudioRecorder.Stop();
            UpdateRecordMenuVisibility();
            if (!AprNes.VideoRecorder.IsRecording)
                Title = "AprNes";
            return;
        }

        if (!_emu.IsRunning) return;

        string capturesDir = _ini.Get("CaptureAudioPath",
            Path.Combine(AppContext.BaseDirectory, "Captures", "Audio"));
        bool ok = AprNes.AudioRecorder.Start(GetFfmpegPath(), capturesDir);
        if (ok)
        {
            UpdateRecordMenuVisibility();
            if (!AprNes.VideoRecorder.IsRecording)
                Title = "AprNes [REC Audio]";
        }
        else
        {
            string err = AprNes.AudioRecorder.LastError ?? "Unknown error";
            await ShowMessageBox("Failed to start audio recording.\n\n" + err);
        }
    }

    private async void StopRecordingIfActive(bool notify = false)
    {
        bool wasStopped = false;
        if (AprNes.VideoRecorder.IsRecording)
        {
            AprNes.VideoRecorder.Stop();
            wasStopped = true;
        }
        if (AprNes.AudioRecorder.IsRecording)
        {
            AprNes.AudioRecorder.Stop();
            wasStopped = true;
        }
        if (wasStopped)
        {
            UpdateRecordMenuVisibility();
            try { Title = "AprNes"; } catch { }
            if (notify)
            {
                await ShowMessageBox(L("rec_stopped_msg",
                    "Recording has been stopped due to settings change."));
            }
        }
    }

    public void StopRecordingOnSettingsChange() => StopRecordingIfActive(true);

    private void MenuRecordSettings_Click(object? sender, RoutedEventArgs e)
    {
        // TODO: open record settings dialog (future)
    }

    private async void MenuConfiguration_Click(object? sender, RoutedEventArgs e)
    {
        bool wasRunning = _emu.IsRunning;
        if (wasRunning) _emu.Pause();

        StopRecordingIfActive(true);

        var dlg = new ConfigWindow(_ini, _emu.Gamepad);
        await dlg.ShowDialog(this);

        ApplyIniSettings();
        UpdateRecordMenuVisibility();
        if (wasRunning) _emu.Resume();
    }

    // ═══ Menu: Help ═════════════════════════════════════════════════════════

    private async void MenuShortcuts_Click(object? sender, RoutedEventArgs e)
    {
        string shortcuts = L("shortcuts_text",
            "Ctrl+O          Open ROM\nCtrl+R          Soft Reset\nCtrl+Shift+P    Screenshot\nF11             Fullscreen\nEscape          Exit Fullscreen\nCtrl+W          Exit")
            .Replace("\\n", "\n");
        await ShowMessageBox(shortcuts);
    }

    private async void MenuAbout_Click(object? sender, RoutedEventArgs e)
    {
        await new AboutWindow().ShowDialog(this);
    }

    // ═══ Recent ROMs ═════════════════════════════════════════════════════════

    private void InitRecentROMs()
    {
        string raw = _ini.Get("RecentROMs", "");
        if (!string.IsNullOrEmpty(raw))
        {
            foreach (string p in raw.Split('|'))
                if (!string.IsNullOrEmpty(p))
                    _recentROMs.Add(p);
        }
        BuildRecentROMsMenu();
    }

    private void BuildRecentROMsMenu()
    {
        MenuRecentRoms.Items.Clear();

        if (_recentROMs.Count == 0)
        {
            var empty = new MenuItem { Header = L("menu_recent_empty", "(empty)"), IsEnabled = false };
            MenuRecentRoms.Items.Add(empty);
            return;
        }

        foreach (string path in _recentROMs)
        {
            var item = new MenuItem { Header = Path.GetFileName(path), Tag = path };
            // Tooltip via attached property not needed; Tag carries full path
            item.Click += RecentROM_Click;
            MenuRecentRoms.Items.Add(item);
        }
    }

    private void AddRecentROM(string fullPath)
    {
        _recentROMs.RemoveAll(p => string.Equals(p, fullPath, StringComparison.OrdinalIgnoreCase));
        _recentROMs.Insert(0, fullPath);
        if (_recentROMs.Count > MaxRecentROMs)
            _recentROMs.RemoveRange(MaxRecentROMs, _recentROMs.Count - MaxRecentROMs);

        BuildRecentROMsMenu();
        _ini.Set("RecentROMs", string.Join("|", _recentROMs));
        _ini.Save();
    }

    private void RemoveRecentROM(string fullPath)
    {
        _recentROMs.RemoveAll(p => string.Equals(p, fullPath, StringComparison.OrdinalIgnoreCase));
        BuildRecentROMsMenu();
        _ini.Set("RecentROMs", string.Join("|", _recentROMs));
        _ini.Save();
    }

    private async void RecentROM_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem mi || mi.Tag is not string path) return;
        if (!File.Exists(path))
        {
            await ShowMessageBox(L("file_not_found", "File not found, removed from list."));
            RemoveRecentROM(path);
            return;
        }
        LoadAndStartRom(path);
    }
}
