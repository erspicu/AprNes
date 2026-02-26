"""
AprNes Gamepad Environment Checker
====================================
檢查系統是否具備 WinMM 和 XInput 的 DLL 與功能，
以確認 AprNes 手把支援是否可正常運作。

使用方式: python check_gamepad_env.py
"""

import ctypes
import ctypes.wintypes
import os
import sys

# ── ANSI 顏色 ──────────────────────────────────────────────
GREEN  = "\033[92m"
RED    = "\033[91m"
YELLOW = "\033[93m"
CYAN   = "\033[96m"
RESET  = "\033[0m"
BOLD   = "\033[1m"

def ok(msg):   print(f"  {GREEN}[OK]{RESET}  {msg}")
def fail(msg): print(f"  {RED}[NG]{RESET}  {msg}")
def warn(msg): print(f"  {YELLOW}[--]{RESET}  {msg}")
def info(msg): print(f"  {CYAN}[..]{RESET}  {msg}")
def section(title):
    print(f"\n{BOLD}{CYAN}{'='*50}{RESET}")
    print(f"{BOLD}{CYAN}  {title}{RESET}")
    print(f"{BOLD}{CYAN}{'='*50}{RESET}")


# ═══════════════════════════════════════════════════════════
# 1. DLL 存在檢查
# ═══════════════════════════════════════════════════════════
section("1. DLL 存在檢查")

SYSTEM32 = os.path.join(os.environ.get("SystemRoot", "C:\\Windows"), "System32")
SYSWOW64 = os.path.join(os.environ.get("SystemRoot", "C:\\Windows"), "SysWOW64")

dll_targets = {
    "winmm.dll":          "WinMM（手把 / 音效）",
    "xinput1_4.dll":      "XInput 1.4（Windows 8+，Xbox 手把）",
    "xinput1_3.dll":      "XInput 1.3（Windows 7，舊版後備）",
    "xinput9_1_0.dll":    "XInput 9.1.0（Vista 舊版）",
}

dll_available = {}
for dll, desc in dll_targets.items():
    found_paths = []
    for folder in [SYSTEM32, SYSWOW64]:
        p = os.path.join(folder, dll)
        if os.path.exists(p):
            found_paths.append(p)
    if found_paths:
        ok(f"{dll}  ({desc})")
        for p in found_paths:
            info(f"    路徑: {p}  [{os.path.getsize(p):,} bytes]")
        dll_available[dll] = True
    else:
        if dll == "winmm.dll":
            fail(f"{dll}  ({desc})  ← 必要！")
        elif dll == "xinput1_4.dll":
            warn(f"{dll}  ({desc})  ← 未找到，Xbox 手把將無法使用")
        else:
            warn(f"{dll}  ({desc})  ← 未找到（可選）")
        dll_available[dll] = False


# ═══════════════════════════════════════════════════════════
# 2. WinMM 功能測試
# ═══════════════════════════════════════════════════════════
section("2. WinMM 功能測試")

# struct JOYCAPS
class JOYCAPS(ctypes.Structure):
    _fields_ = [
        ("wMid",         ctypes.c_ushort),
        ("wPid",         ctypes.c_ushort),
        ("szPname",      ctypes.c_char * 32),
        ("wXmin",        ctypes.c_int),
        ("wXmax",        ctypes.c_int),
        ("wYmin",        ctypes.c_int),
        ("wYmax",        ctypes.c_int),
        ("wZmin",        ctypes.c_int),
        ("wZmax",        ctypes.c_int),
        ("wNumButtons",  ctypes.c_int),
        ("wPeriodMin",   ctypes.c_int),
        ("wPeriodMax",   ctypes.c_int),
        ("wRmin",        ctypes.c_int),
        ("wRmax",        ctypes.c_int),
        ("wUmin",        ctypes.c_int),
        ("wUmax",        ctypes.c_int),
        ("wVmin",        ctypes.c_int),
        ("wVmax",        ctypes.c_int),
        ("wCaps",        ctypes.c_int),
        ("wMaxAxes",     ctypes.c_int),
        ("wNumAxes",     ctypes.c_int),
        ("wMaxButtons",  ctypes.c_int),
        ("szRegKey",     ctypes.c_char * 32),
        ("szOEMVxD",     ctypes.c_char * 260),
    ]

class JOYINFO(ctypes.Structure):
    _fields_ = [
        ("wXpos",    ctypes.c_int),
        ("wYpos",    ctypes.c_int),
        ("wZpos",    ctypes.c_int),
        ("wButtons", ctypes.c_int),
    ]

winmm_ok = False
winmm_devices = []

try:
    winmm = ctypes.windll.winmm
    ok("winmm.dll 載入成功")
    winmm_ok = True

    # joyGetDevCaps 實際 export 為 joyGetDevCapsA (ANSI)
    try:
        _joyGetDevCaps = winmm.joyGetDevCapsA
    except AttributeError:
        _joyGetDevCaps = winmm.joyGetDevCaps

    joycap   = JOYCAPS()
    joyinfo  = JOYINFO()
    caps_sz  = ctypes.sizeof(JOYCAPS)

    found_count = 0
    for i in range(16):  # 掃描前 16 個 ID
        ret = _joyGetDevCaps(i, ctypes.byref(joycap), caps_sz)
        if ret == 0:
            ret2 = winmm.joyGetPos(i, ctypes.byref(joyinfo))
            status = "可讀取" if ret2 == 0 else "無法讀取狀態"
            name = joycap.szPname.decode("mbcs", errors="replace")
            ok(f"WinMM 裝置 [{i}]: {name}  ({joycap.wNumButtons} 顆按鈕, {status})")
            winmm_devices.append(i)
            found_count += 1

    if found_count == 0:
        warn("WinMM: 未偵測到任何遊戲手把裝置")
    else:
        ok(f"WinMM: 共偵測到 {found_count} 個裝置")

except Exception as e:
    fail(f"winmm.dll 功能測試失敗: {e}")


# ═══════════════════════════════════════════════════════════
# 3. XInput 功能測試
# ═══════════════════════════════════════════════════════════
section("3. XInput 功能測試")

# struct XINPUT_GAMEPAD
class XINPUT_GAMEPAD(ctypes.Structure):
    _fields_ = [
        ("wButtons",      ctypes.c_ushort),
        ("bLeftTrigger",  ctypes.c_ubyte),
        ("bRightTrigger", ctypes.c_ubyte),
        ("sThumbLX",      ctypes.c_short),
        ("sThumbLY",      ctypes.c_short),
        ("sThumbRX",      ctypes.c_short),
        ("sThumbRY",      ctypes.c_short),
    ]

class XINPUT_STATE(ctypes.Structure):
    _fields_ = [
        ("dwPacketNumber", ctypes.c_uint),
        ("Gamepad",        XINPUT_GAMEPAD),
    ]

ERROR_SUCCESS       = 0
ERROR_DEVICE_NOT_CONNECTED = 1167

XI_BTN_NAMES = {
    0x0001: "D-Up",   0x0002: "D-Down",  0x0004: "D-Left", 0x0008: "D-Right",
    0x0010: "Start",  0x0020: "Back",
    0x0100: "LB",     0x0200: "RB",
    0x1000: "A",      0x2000: "B",       0x4000: "X",      0x8000: "Y",
}

xi_dll   = None
xi_name  = None
xi_connected = []

for candidate in ["xinput1_4.dll", "xinput1_3.dll", "xinput9_1_0.dll"]:
    if not dll_available.get(candidate, False):
        continue
    try:
        xi_dll  = ctypes.windll.LoadLibrary(candidate)
        xi_name = candidate
        ok(f"XInput DLL 載入成功: {candidate}")
        break
    except Exception as e:
        warn(f"{candidate} 載入失敗: {e}")

if xi_dll is None:
    fail("無可用的 XInput DLL，Xbox 手把功能將不可用")
else:
    found_xi = 0
    for player in range(4):
        state = XINPUT_STATE()
        ret = xi_dll.XInputGetState(player, ctypes.byref(state))
        if ret == ERROR_SUCCESS:
            g = state.Gamepad
            pressed = [name for mask, name in XI_BTN_NAMES.items() if g.wButtons & mask]
            btn_str = ", ".join(pressed) if pressed else "無按鍵按下"
            ok(f"XInput Player {player}: 已連線")
            info(f"    wButtons=0x{g.wButtons:04X} ({btn_str})")
            info(f"    LX={g.sThumbLX:6d}  LY={g.sThumbLY:6d}  "
                 f"LT={g.bLeftTrigger:3d}  RT={g.bRightTrigger:3d}")
            xi_connected.append(player)
            found_xi += 1
        elif ret == ERROR_DEVICE_NOT_CONNECTED:
            warn(f"XInput Player {player}: 未連線")
        else:
            warn(f"XInput Player {player}: 錯誤碼 0x{ret:08X}")

    if found_xi == 0:
        warn("XInput: 未偵測到已連線的 XInput 裝置")
    else:
        ok(f"XInput: 共偵測到 {found_xi} 個 Xbox 裝置")


# ═══════════════════════════════════════════════════════════
# 4. 結論
# ═══════════════════════════════════════════════════════════
section("4. 結論 / AprNes 相容性")

winmm_support  = winmm_ok
xinput_support = xi_dll is not None

print()
if winmm_support:
    ok("WinMM 支援：✔  一般 USB 手把可用")
else:
    fail("WinMM 支援：✘  AprNes 無法使用任何手把")

if xinput_support:
    ok(f"XInput 支援：✔  Xbox 手把可用  ({xi_name})")
else:
    warn("XInput 支援：✘  Xbox / Xbox One / Xbox Series 手把不可用")
    warn("             建議安裝 DirectX End-User Runtime 以取得 xinput1_4.dll")

print()
if winmm_support and xinput_support:
    ok("環境完整，AprNes 可支援所有手把類型。")
elif winmm_support:
    warn("環境部分完整，Xbox 手把不可用，其他 USB 手把正常。")
else:
    fail("環境有問題，請確認 Windows 安裝完整。")
print()
