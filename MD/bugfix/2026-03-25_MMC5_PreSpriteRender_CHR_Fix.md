# 2026-03-25 MMC5 Pre-Sprite Render CHR Bank Fix

## 問題

Castlevania III (US, MMC5) 標題畫面「OPENING」旁邊的十字架游標顯示為亂碼圖形。
此問題在 MMC5 `$2F` nametable 修復（chrABAutoSwitch=false 改用 NotifyVramRead 驅動 CHR A/B 切換）後出現。

## 根因

MMC5 在 8x16 sprite 模式下，CHR bank 分為 A set（sprites）和 B set（BG）。

舊程式碼（`chrABAutoSwitch=true`）：PPU 在 cx=257 自動將 chrBankPtrs 切換到 A set，然後呼叫 `RenderSpritesLine()`，sprite 使用正確的 A set CHR banks。

新程式碼（`chrABAutoSwitch=false`）：改由 `NotifyVramRead()` 追蹤 `splitTileNumber` 來決定 CHR A/B 切換。但 `NotifyVramRead()` 的第一次 sprite phase 呼叫在 cx=258（garbage NT fetch），比 cx=257 的 `RenderSpritesLine()` **晚一個 dot**。因此 `RenderSpritesLine()` 執行時 chrBankPtrs 仍指向 B set（BG banks），sprite 使用了錯誤的 tile data。

## 修復

### Mapper005.cs — 新增 `PreSpriteRender()` 方法

在 cx=257、`RenderSpritesLine()` 之前，由 PPU 呼叫此方法強制切換到 A set：

```csharp
public void PreSpriteRender()
{
    if (NesCore.Spritesize8x16 && chrRomSize > 0)
    {
        prevChrA = true;
        FillCHRBankPtrs(NesCore.chrBankPtrs, true);
    }
}
```

### PPU.cs — cx=257 呼叫 PreSpriteRender

```csharp
if (cx == 257)
{
    if (mmc5Ref != null) mmc5Ref.PreSpriteRender();
    RenderSpritesLine();
}
```

## 驗證

- Castlevania III 標題畫面：十字架游標正常顯示 ✅
- mmc5test split screen：仍正確（上方捲動區域 + 下方條紋數字）✅
- blargg: 174/174 PASS ✅
- AccuracyCoin: 136/136 PASS ✅
