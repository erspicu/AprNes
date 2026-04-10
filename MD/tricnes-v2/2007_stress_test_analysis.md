# $2007 Stress Test 分析 — 最後一項 FAIL

**來源**: AccuracyCoin v2 ASM 源碼 lines 2518-3010
**測試**: P19 "$2007 Stress Test" FAIL 1

---

## 測試邏輯

1. 在 visible scanline 的**每一個 dot** 讀 $2007（共 341 dots）
2. 每次讀取後，再讀一次 $2007 取得 buffer 內容
3. 記錄 256 + 85 = 341 bytes 的 buffer 值
4. 和已知答案比對（只比對奇數 index 的 byte — 穩定讀取）

---

## 硬體行為（AC test 作者的詳細描述）

### PPU 讀取節奏（rendering 期間）

每 2 dots 完成一次完整讀取：
- **Even dot**: 設定 address bus + ALE latch（地址準備）
- **Odd dot**: 執行讀取（data on bus）

模式重複：
```
dot 1: ALE (NT addr)    dot 2: Read (NT data)
dot 3: ALE (AT addr)    dot 4: Read (AT data)
dot 5: ALE (CHR addr)   dot 6: Read (CHR low)
dot 7: ALE (CHR addr)   dot 8: Read (CHR high)
dot 9: ALE (NT addr)    ...
```

### $2007 SM 時序（D-latch 管線）

$2007 read SM 使用 5 個 D-latch 形成的管線，由 PPU_Clock 驅動：

```
idle state: Latches = 01010, SR = 1 (true)

CPU 讀 $2007 結束 (M2 goes low):
  t0.0: R=1, SR=0, Latches=01010
  t0.1: R=0, SR=0, Latches=01010  ← M2 low, read cycle ends
  t1.0:           Latches=11010    ← Latch[0] loads from SR
  t1.1:           Latches=10010    ← Latch[1] inverts from Latch[0]
  t2.0:           Latches=10110, ALE=true  ← address latch
  t2.1:           Latches=10100, ALE=true, SR reset
  t3.0:           Latches=00101
  t3.1:           Latches=01101
  t4.0:           Latches=01001, Read=true  ← buffer refill!
  t4.1:           Latches=01011, Read=true
  t5.0:           Latches=01010             ← back to idle
```

**關鍵**: buffer 在 **t4（4 PPU half-cycles = 2 PPU dots after ALE）** 更新。

### $2007 SM 和 rendering fetch 的交互

當 $2007 read 發生在 visible scanline 時：
- SM 的 ALE/Read 和 rendering fetch 的 ALE/Read 會**重疊**
- Even dot 重疊 = ALE + ALE（不穩定 — analogue feedback）
- Odd dot 重疊 = Read + Read（穩定 — 兩個 read 用同一地址）

測試只檢查穩定的 byte（odd index），跳過不穩定的（even index）。

---

## 我們需要修正什麼

### 目前的問題

我們的 Process2007StateMachine 用整數計數器（state 0-9），在特定 state 更新 buffer：
- State 1: 部分 buffer 更新（bufferLate 條件）
- State 4: buffer 更新 + v increment

但測試要求的是：**buffer 在 CPU read 結束後精確 4 PPU half-cycles（= 2 PPU dots）更新**。如果我們的 buffer 更新時機差 1 dot，穩定/不穩定 byte 的 pattern 就會錯位。

### 具體的 timing 要求

```
CPU read $2007 結束
  → 2 PPU half-cycles: ALE (address bus = v)
  → 2 PPU half-cycles: idle
  → 2 PPU half-cycles: Read (buffer = data from bus)
```

測試允許整體偏移 1 byte（alignment 差異），但如果偏移超過 1，或者穩定 byte 的值不對，就 FAIL。

### 可能的修正方向

1. **確認 SM state → buffer 更新的 timing 正確**: state 4 應該對應 CPU read 結束後的第 4 個 PPU half-cycle
2. **確認 buffer 讀取的地址正確**: 當 ALE 和 rendering fetch 的 ALE 重疊時，rendering fetch 優先（地址來自 tile fetch，不是 v）
3. **確認 buffer 值來自 rendering fetch**: 穩定的 read 應該得到和 rendering 相同的 data（NT byte, AT byte, CHR low, CHR high）

---

## 預期結果對照表

| 偏移 | 類型 | 預期值 | 來源 |
|------|------|--------|------|
| $501 | NT | 02 | nametable[$2C02] |
| $503 | AT | C0 | attribute[$2FC0] |
| $505 | PL | 46 | pattern low |
| $507 | PH | 46 | pattern high |
| $509 | NT | 03 | nametable[$2C03] |
| ... | ... | ... | ... |

穩定 byte 是 rendering fetch 正在讀的資料，因為 SM 的 Read 和 fetch 的 Read 對齊。
