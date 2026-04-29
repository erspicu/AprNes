# A2 6502 完整 256 Opcode 實作參考

## 這篇要解決什麼問題

寫 NES 模擬器到 CPU 階段時，你需要一份「給定 opcode hex，知道要做什麼」的對照表。本篇是給實作 6502 / Ricoh 2A03 直譯器或 cycle-accurate 模擬器用的完整參考：

- 全部 256 個 opcode（151 個官方 + 105 個非官方/illegal）
- 每個 opcode 的：定址模式、bytes、cycles、影響的旗標、語意
- NES 跟原版 6502 的差異（沒有 BCD 模式）
- 實作上的共通規則（RMW、page-crossing、branch penalty、interrupt sequence）

讀本篇前建議先看 [05 6502 CPU 核心](05_6502_cpu_core.md) 了解 register 跟 addressing mode 的基本概念。

---

## 0. 先說清楚：實作共通規則

在看每條指令前，先把「所有指令都會碰到的共同行為」釐清一次，下面表格就不用每條重複說明。

### 0.1 旗標（P 暫存器的 7 bits）

| bit | 名稱 | 意義 |
|---|---|---|
| 7 | **N** (Negative) | 結果的 bit 7 |
| 6 | **V** (Overflow) | 算術溢位（簽名超出範圍） |
| 5 | — | 物理上不存在；push 到 stack 永遠是 1 |
| 4 | **B** (Break) | 物理上不存在；BRK/PHP push 時為 1，IRQ/NMI push 時為 0 |
| 3 | **D** (Decimal) | BCD 模式 —— **NES 上忽略，6502 ADC/SBC 不走 BCD 路徑** |
| 2 | **I** (Interrupt Disable) | 1=遮罩 IRQ |
| 1 | **Z** (Zero) | 結果是否為 0 |
| 0 | **C** (Carry) | 進位/借位 |

**對 NES 模擬器很重要的一點**：D（decimal）旗標可以讀寫但**不影響運算結果**。Ricoh 2A03 把 6502 的 BCD 邏輯閘移除了。模擬器寫 ADC/SBC 時不需要實作 BCD 模式。

### 0.2 定址模式縮寫表

| 縮寫 | 名稱 | 範例 | 取運算元方式 |
|---|---|---|---|
| **Imp** | Implied | `CLC` | 無運算元 |
| **Acc** | Accumulator | `ASL A` | 對 A 操作 |
| **Imm** | Immediate | `LDA #$42` | 下一個 byte 直接是值 |
| **ZP** | Zero Page | `LDA $42` | `mem[$0042]` |
| **ZP,X** | Zero Page,X | `LDA $42,X` | `mem[($42 + X) & $FF]`（保持在 zero page） |
| **ZP,Y** | Zero Page,Y | `LDX $42,Y` | `mem[($42 + Y) & $FF]` |
| **Abs** | Absolute | `LDA $1234` | `mem[$1234]` |
| **Abs,X** | Absolute,X | `LDA $1234,X` | `mem[$1234 + X]`（page crossing 加 1 cycle） |
| **Abs,Y** | Absolute,Y | `LDA $1234,Y` | `mem[$1234 + Y]`（page crossing 加 1 cycle） |
| **(Ind)** | Indirect | `JMP ($1234)` | `mem[$1234]` 為 low，`mem[$1235]` 為 high（**有 JMP page-boundary bug**） |
| **(Ind,X)** | Indexed Indirect | `LDA ($42,X)` | 在 zero page 找 `mem[$42+X]` 跟 `mem[$42+X+1]`，組成 16-bit 位址後讀那個位址 |
| **(Ind),Y** | Indirect Indexed | `LDA ($42),Y` | 在 zero page 找 `mem[$42]` 跟 `mem[$43]` 組成 base，再加 Y |
| **Rel** | Relative | `BNE $42` | PC 加上 signed byte（branch 用）|

### 0.3 Page Crossing penalty

`Abs,X` / `Abs,Y` / `(Ind),Y` 模式下，base 跟 base+index 如果跨越 256 byte 邊界（high byte 不一樣），**多花 1 cycle**。例如 `LDA $10F0,X` 當 X=$20 時，`$10F0 + $20 = $1110`（跨頁），多 1 cycle。

**例外**：寫入指令（STA/STX/STY 等）跟 RMW 指令**永遠付 page-cross cost**（即使沒跨），因為要先做 dummy read 才寫。

### 0.4 Branch penalty

分支指令（BCC/BCS/BEQ/BNE 等）：
- **沒採用** branch：2 cycles
- **採用了** branch：3 cycles
- **採用且跨頁**：4 cycles

### 0.5 Read-Modify-Write（RMW）

ASL/LSR/ROL/ROR/INC/DEC（記憶體版本）這類指令的執行序：

1. 讀記憶體
2. **再寫一次原值**（dummy write，硬體用同一個 cycle 把原值寫回）
3. 計算結果
4. 寫入新值

模擬器的影響：RMW 在某些 hardware register（例如 `$2007`）會觸發**兩次副作用**，如果只寫一次會讓某些 PPU 行為錯亂。

### 0.6 Stack 機制

- SP 是 8-bit 暫存器，stack 物理上在 `$0100`–`$01FF`
- 實際位址 = `$0100 | SP`
- **Push** 步驟：先寫 `mem[$0100|SP]`，**再** SP--
- **Pull** 步驟：先 SP++，**再**讀 `mem[$0100|SP]`
- SP 溢位是合法行為（會 wrap 到另一端）

### 0.7 Interrupt Sequence

NMI / IRQ / BRK 觸發時 CPU 要做：

```
1. (BRK 才有) PC++
2. push PC high
3. push PC low
4. push P (B flag: BRK/PHP=1, IRQ/NMI=0)
5. 設定 I flag = 1
6. PC = mem[vector_low] | (mem[vector_high] << 8)
```

| Vector | 位址 |
|---|---|
| NMI | `$FFFA-$FFFB` |
| Reset | `$FFFC-$FFFD` |
| IRQ / BRK | `$FFFE-$FFFF` |

**Interrupt hijacking**：BRK 跟同時發生的 NMI 會「合體」—— BRK 開始 push 後 NMI 觸發，最後跳到 NMI vector 而非 IRQ vector。要過 cpu_interrupts_v2 測試需要實作這個。

整個 interrupt 流程吃 7 個 cycle。

### 0.8 NES 跟 6502 的差異

| 項目 | 標準 6502 | Ricoh 2A03（NES） |
|---|---|---|
| BCD 模式 | 有 | **無**（D flag 可讀寫但不影響 ADC/SBC） |
| 整合 APU | 無 | **有**（地址 `$4000`–`$4017`） |
| Decimal 旗標的副作用 | 影響運算 | 完全不影響 |

---

## 1. Load / Store 系列

### LDA — Load Accumulator
```
A = M; N=A.bit7, Z=(A==0)
```

| Op | Mode | B | C |
|---|---|---|---|
| `A9` | Imm | 2 | 2 |
| `A5` | ZP | 2 | 3 |
| `B5` | ZP,X | 2 | 4 |
| `AD` | Abs | 3 | 4 |
| `BD` | Abs,X | 3 | 4 (+1 page) |
| `B9` | Abs,Y | 3 | 4 (+1 page) |
| `A1` | (Ind,X) | 2 | 6 |
| `B1` | (Ind),Y | 2 | 5 (+1 page) |

### LDX — Load X
```
X = M; N=X.bit7, Z=(X==0)
```

| Op | Mode | B | C |
|---|---|---|---|
| `A2` | Imm | 2 | 2 |
| `A6` | ZP | 2 | 3 |
| `B6` | ZP,Y | 2 | 4 |
| `AE` | Abs | 3 | 4 |
| `BE` | Abs,Y | 3 | 4 (+1 page) |

### LDY — Load Y
```
Y = M; N=Y.bit7, Z=(Y==0)
```

| Op | Mode | B | C |
|---|---|---|---|
| `A0` | Imm | 2 | 2 |
| `A4` | ZP | 2 | 3 |
| `B4` | ZP,X | 2 | 4 |
| `AC` | Abs | 3 | 4 |
| `BC` | Abs,X | 3 | 4 (+1 page) |

### STA — Store Accumulator
```
M = A; (no flags)
```

| Op | Mode | B | C |
|---|---|---|---|
| `85` | ZP | 2 | 3 |
| `95` | ZP,X | 2 | 4 |
| `8D` | Abs | 3 | 4 |
| `9D` | Abs,X | 3 | **5** |
| `99` | Abs,Y | 3 | **5** |
| `81` | (Ind,X) | 2 | 6 |
| `91` | (Ind),Y | 2 | **6** |

注意：寫入的 Abs,X / Abs,Y / (Ind),Y 永遠付 page-cross cost（不管實際有沒有跨）。

### STX — Store X
```
M = X
```

| Op | Mode | B | C |
|---|---|---|---|
| `86` | ZP | 2 | 3 |
| `96` | ZP,Y | 2 | 4 |
| `8E` | Abs | 3 | 4 |

### STY — Store Y
```
M = Y
```

| Op | Mode | B | C |
|---|---|---|---|
| `84` | ZP | 2 | 3 |
| `94` | ZP,X | 2 | 4 |
| `8C` | Abs | 3 | 4 |

---

## 2. Transfer 系列

```
TAX  AA  Imp  1B  2C   X = A; flags(N,Z) on X
TAY  A8  Imp  1B  2C   Y = A; flags(N,Z) on Y
TXA  8A  Imp  1B  2C   A = X; flags(N,Z) on A
TYA  98  Imp  1B  2C   A = Y; flags(N,Z) on A
TSX  BA  Imp  1B  2C   X = SP; flags(N,Z) on X
TXS  9A  Imp  1B  2C   SP = X; (no flags)
```

`TXS` 是唯一**不更新旗標**的 transfer。

---

## 3. Stack 系列

```
PHA  48  Imp  1B  3C   push A
PHP  08  Imp  1B  3C   push P (with B=1, bit 5=1)
PLA  68  Imp  1B  4C   pull A; flags(N,Z) on A
PLP  28  Imp  1B  4C   pull P (B 跟 bit 5 不影響真實的 P)
```

---

## 4. 算術 (ADC / SBC)

### ADC — Add with Carry
```
A = A + M + C
N = result.bit7
Z = (result == 0)
C = (result > 255)
V = ((A^result) & (M^result) & 0x80) != 0
```

| Op | Mode | B | C |
|---|---|---|---|
| `69` | Imm | 2 | 2 |
| `65` | ZP | 2 | 3 |
| `75` | ZP,X | 2 | 4 |
| `6D` | Abs | 3 | 4 |
| `7D` | Abs,X | 3 | 4 (+1 page) |
| `79` | Abs,Y | 3 | 4 (+1 page) |
| `61` | (Ind,X) | 2 | 6 |
| `71` | (Ind),Y | 2 | 5 (+1 page) |

### SBC — Subtract with Carry
```
A = A - M - (1 - C)   等價於  A = A + (~M) + C
旗標規則同 ADC（用 ~M 帶入 V 公式）
```

| Op | Mode | B | C |
|---|---|---|---|
| `E9` | Imm | 2 | 2 |
| `E5` | ZP | 2 | 3 |
| `F5` | ZP,X | 2 | 4 |
| `ED` | Abs | 3 | 4 |
| `FD` | Abs,X | 3 | 4 (+1 page) |
| `F9` | Abs,Y | 3 | 4 (+1 page) |
| `E1` | (Ind,X) | 2 | 6 |
| `F1` | (Ind),Y | 2 | 5 (+1 page) |
| `EB` | Imm | 2 | 2 | **非官方但等同 SBC #imm** |

---

## 5. 邏輯 (AND / ORA / EOR / BIT)

格式跟 LDA 完全一樣（同樣 8 個 mode），結果存在 A，更新 N、Z。

```
AND  29 25 35 2D 3D 39 21 31    A = A & M
ORA  09 05 15 0D 1D 19 01 11    A = A | M
EOR  49 45 55 4D 5D 59 41 51    A = A ^ M
```

cycles 跟 LDA 對應位置完全一致。

### BIT — Test Bits
```
result = A & M
Z = (result == 0)
N = M.bit7
V = M.bit6
A 不變
```

| Op | Mode | B | C |
|---|---|---|---|
| `24` | ZP | 2 | 3 |
| `2C` | Abs | 3 | 4 |

---

## 6. 比較 (CMP / CPX / CPY)

```
result = REG - M
Z = (REG == M)
C = (REG >= M)
N = result.bit7
REG 跟 M 不變
```

### CMP（compare A）

| Op | Mode | B | C |
|---|---|---|---|
| `C9` | Imm | 2 | 2 |
| `C5` | ZP | 2 | 3 |
| `D5` | ZP,X | 2 | 4 |
| `CD` | Abs | 3 | 4 |
| `DD` | Abs,X | 3 | 4 (+1 page) |
| `D9` | Abs,Y | 3 | 4 (+1 page) |
| `C1` | (Ind,X) | 2 | 6 |
| `D1` | (Ind),Y | 2 | 5 (+1 page) |

### CPX（compare X）/ CPY（compare Y）

```
CPX  E0 Imm 2B 2C   E4 ZP 2B 3C   EC Abs 3B 4C
CPY  C0 Imm 2B 2C   C4 ZP 2B 3C   CC Abs 3B 4C
```

---

## 7. Increment / Decrement

### INC / DEC（記憶體 RMW）

```
INC  E6 ZP 2B 5C    F6 ZP,X 2B 6C    EE Abs 3B 6C    FE Abs,X 3B 7C
DEC  C6 ZP 2B 5C    D6 ZP,X 2B 6C    CE Abs 3B 6C    DE Abs,X 3B 7C
```

旗標 N、Z 看新值。

### INX / DEX / INY / DEY（暫存器版本）

```
INX  E8  Imp  1B  2C   X++; flags(N,Z) on X
DEX  CA  Imp  1B  2C   X--; flags(N,Z) on X
INY  C8  Imp  1B  2C   Y++; flags(N,Z) on Y
DEY  88  Imp  1B  2C   Y--; flags(N,Z) on Y
```

---

## 8. Shift / Rotate（RMW）

### ASL — Arithmetic Shift Left
```
C = old.bit7
new = old << 1
N = new.bit7, Z = (new == 0)
```

| Op | Mode | B | C |
|---|---|---|---|
| `0A` | Acc | 1 | 2 |
| `06` | ZP | 2 | 5 |
| `16` | ZP,X | 2 | 6 |
| `0E` | Abs | 3 | 6 |
| `1E` | Abs,X | 3 | 7 |

### LSR — Logical Shift Right
```
C = old.bit0
new = old >> 1
N = 0（永遠）, Z = (new == 0)
```

| Op | Mode | B | C |
|---|---|---|---|
| `4A` | Acc | 1 | 2 |
| `46` | ZP | 2 | 5 |
| `56` | ZP,X | 2 | 6 |
| `4E` | Abs | 3 | 6 |
| `5E` | Abs,X | 3 | 7 |

### ROL — Rotate Left（透過 Carry）
```
new = (old << 1) | C
C = old.bit7
flags 同 ASL（用 new）
```

| Op | Mode | B | C |
|---|---|---|---|
| `2A` | Acc | 1 | 2 |
| `26` | ZP | 2 | 5 |
| `36` | ZP,X | 2 | 6 |
| `2E` | Abs | 3 | 6 |
| `3E` | Abs,X | 3 | 7 |

### ROR — Rotate Right
```
new = (old >> 1) | (C << 7)
C = old.bit0
N = C 的 new 位置（即 new.bit7 = 原 C）
Z = (new == 0)
```

| Op | Mode | B | C |
|---|---|---|---|
| `6A` | Acc | 1 | 2 |
| `66` | ZP | 2 | 5 |
| `76` | ZP,X | 2 | 6 |
| `6E` | Abs | 3 | 6 |
| `7E` | Abs,X | 3 | 7 |

---

## 9. Branch / Jump / Subroutine

### Branch（條件分支）

採用 → 加 1 cycle；跨頁再加 1 cycle。

| Op | Mn | 條件 | B | C(no) | C(yes) |
|---|---|---|---|---|---|
| `10` | BPL | N == 0 | 2 | 2 | 3 (+1 page) |
| `30` | BMI | N == 1 | 2 | 2 | 3 (+1 page) |
| `50` | BVC | V == 0 | 2 | 2 | 3 (+1 page) |
| `70` | BVS | V == 1 | 2 | 2 | 3 (+1 page) |
| `90` | BCC | C == 0 | 2 | 2 | 3 (+1 page) |
| `B0` | BCS | C == 1 | 2 | 2 | 3 (+1 page) |
| `D0` | BNE | Z == 0 | 2 | 2 | 3 (+1 page) |
| `F0` | BEQ | Z == 1 | 2 | 2 | 3 (+1 page) |

### JMP

| Op | Mode | B | C | 注意 |
|---|---|---|---|---|
| `4C` | Abs | 3 | 3 | 直接跳 |
| `6C` | (Ind) | 3 | 5 | **JMP page-boundary bug**：當 indirect address low byte 是 `$FF`，high byte 不會跨頁讀。例如 `JMP ($10FF)` 讀 `mem[$10FF]` 跟 `mem[$1000]`（不是 `$1100`）。模擬器**必須**還原這個 bug。 |

### JSR / RTS / RTI / BRK

```
JSR  20 Abs  3B 6C   push (PC-1) high, push (PC-1) low, PC = target
RTS  60 Imp  1B 6C   pull PC low, pull PC high, PC = (popped + 1)
RTI  40 Imp  1B 6C   pull P, pull PC low, pull PC high, **PC 不+1**
BRK  00 Imp  2B 7C   PC++, push PC high, push PC low, push P|0x10,
                     I=1, PC = mem[$FFFE/F]
```

注意 BRK 是 **2 byte 指令**（`00 XX`，padding byte 任意），但執行時 PC 已經前進兩次。

---

## 10. 狀態旗標控制

```
CLC  18  Imp  1B  2C   C = 0
SEC  38  Imp  1B  2C   C = 1
CLI  58  Imp  1B  2C   I = 0
SEI  78  Imp  1B  2C   I = 1
CLV  B8  Imp  1B  2C   V = 0
CLD  D8  Imp  1B  2C   D = 0    (NES 上 D 不影響運算，但仍可改)
SED  F8  Imp  1B  2C   D = 1    (同上)
```

---

## 11. NOP

```
NOP  EA  Imp  1B  2C
```

---

## 12. 非官方 / Illegal Opcodes

NES 老遊戲使用了部分這些非官方 opcode（最常見：LAX、SAX、DCP、ISB）。要 100% 通過 nestest 等測試 ROM 必須實作。

下表標 ⭐ 的是 NES 商業遊戲常用的。

### 12.1 組合 RMW（一個 opcode 做兩件事）⭐

| Op | Mn | 等價 | 描述 |
|---|---|---|---|
| `C7,D7,CF,DF,DB,C3,D3` | **DCP** ⭐ | DEC + CMP | M--; CMP |
| `E7,F7,EF,FF,FB,E3,F3` | **ISB / ISC** ⭐ | INC + SBC | M++; A = A - M |
| `07,17,0F,1F,1B,03,13` | **SLO / ASO** | ASL + ORA | M = M<<1; A |= M |
| `27,37,2F,3F,3B,23,33` | **RLA** ⭐ | ROL + AND | M = ROL(M); A &= M |
| `47,57,4F,5F,5B,43,53` | **SRE / LSE** | LSR + EOR | M = M>>1; A ^= M |
| `67,77,6F,7F,7B,63,73` | **RRA** | ROR + ADC | M = ROR(M); A = A+M+C |

### 12.2 LAX / SAX ⭐

```
LAX  A7,B7,AF,BF,A3,B3        A = X = M; flags(N,Z)  ⭐(常用)
SAX  87,97,8F,83              M = A & X; (no flags)  ⭐(常用)
```

LAX 沒有 immediate 模式（只有上面那六個 mode）。`AB` (LXA, immediate) 是不穩定的 illegal opcode，不建議實作。

### 12.3 ANC / ALR / ARR / AXS / SBC dup

```
ANC  0B, 2B  Imm  2B 2C    A = A & M; C = N (= bit7 of result)
ALR  4B      Imm  2B 2C    A = A & M; A = A >> 1; flags
ARR  6B      Imm  2B 2C    A = A & M; A = ROR(A); flags 計算特殊
                            (V = A.bit5 ^ A.bit6, C = A.bit6)
AXS  CB      Imm  2B 2C    X = (A & X) - M; flags
SBC* EB      Imm  2B 2C    等同 SBC #imm（已在 SBC 段列出）
```

### 12.4 NOP 變體（多 byte / 多 cycle）

實際上一堆「不做事但消耗 cycle 跟 byte」的指令。商業遊戲偶爾用來做 timing padding。

| Op | Mode | B | C |
|---|---|---|---|
| `1A,3A,5A,7A,DA,FA` | Imp | 1 | 2 |
| `80,82,89,C2,E2` | Imm | 2 | 2 |
| `04,44,64` | ZP | 2 | 3 |
| `14,34,54,74,D4,F4` | ZP,X | 2 | 4 |
| `0C` | Abs | 3 | 4 |
| `1C,3C,5C,7C,DC,FC` | Abs,X | 3 | 4 (+1 page) |

### 12.5 不穩定 / 危險 opcode

下面這些在不同 batch 的 6502/2A03 行為不一致，**建議模擬器實作為「讀記憶體但不影響暫存器」或標為 NOP**。商業遊戲幾乎不用：

```
SHA / AHX  93, 9F           store A & X & (high+1)
SHX / SXA  9E               store X & (high+1)
SHY / SYA  9C               store Y & (high+1)
TAS / SHS  9B               SP = A & X; store SP & (high+1)
LAS / LAR  BB               A = X = SP = M & SP
ANE / XAA  8B  Imm          A = (A | const) & X & M  (極不穩定)
LXA / OAL  AB  Imm          A = X = (A | const) & M  (極不穩定)
```

### 12.6 KIL / JAM / HLT — CPU 鎖死

```
$02, $12, $22, $32, $42, $52, $62, $72, $92, $B2, $D2, $F2
```

執行到這幾個 opcode CPU 永久當機（PC 不前進、不接 interrupt）。模擬器可以選擇：
- 真的鎖住 PC（最忠於硬體）
- 印 warning + 視為 NOP（方便 debug）

---

## 13. 快速查表（依 hex 排序，hi nibble × lo nibble）

縮寫：**Off** = 官方; **Un** = 非官方; **(*)** = 跨頁可加 cycle; **(b)** = branch 採用加 cycle

```
       0          1          2          3          4          5          6          7
0_  BRK Imp 7   ORA inX 6   KIL  -      SLO inX 8   NOP zp  3   ORA zp  3   ASL zp  5   SLO zp  5
1_  BPL rel 2(b)ORA inY 5(*)KIL  -      SLO inY 8   NOP zpx 4   ORA zpx 4   ASL zpx 6   SLO zpx 6
2_  JSR abs 6   AND inX 6   KIL  -      RLA inX 8   BIT zp  3   AND zp  3   ROL zp  5   RLA zp  5
3_  BMI rel 2(b)AND inY 5(*)KIL  -      RLA inY 8   NOP zpx 4   AND zpx 4   ROL zpx 6   RLA zpx 6
4_  RTI Imp 6   EOR inX 6   KIL  -      SRE inX 8   NOP zp  3   EOR zp  3   LSR zp  5   SRE zp  5
5_  BVC rel 2(b)EOR inY 5(*)KIL  -      SRE inY 8   NOP zpx 4   EOR zpx 4   LSR zpx 6   SRE zpx 6
6_  RTS Imp 6   ADC inX 6   KIL  -      RRA inX 8   NOP zp  3   ADC zp  3   ROR zp  5   RRA zp  5
7_  BVS rel 2(b)ADC inY 5(*)KIL  -      RRA inY 8   NOP zpx 4   ADC zpx 4   ROR zpx 6   RRA zpx 6
8_  NOP imm 2   STA inX 6   NOP imm 2   SAX inX 6   STY zp  3   STA zp  3   STX zp  3   SAX zp  3
9_  BCC rel 2(b)STA inY 6   KIL  -      SHA inY 6   STY zpx 4   STA zpx 4   STX zpy 4   SAX zpy 4
A_  LDY imm 2   LDA inX 6   LDX imm 2   LAX inX 6   LDY zp  3   LDA zp  3   LDX zp  3   LAX zp  3
B_  BCS rel 2(b)LDA inY 5(*)KIL  -      LAX inY 5(*)LDY zpx 4   LDA zpx 4   LDX zpy 4   LAX zpy 4
C_  CPY imm 2   CMP inX 6   NOP imm 2   DCP inX 8   CPY zp  3   CMP zp  3   DEC zp  5   DCP zp  5
D_  BNE rel 2(b)CMP inY 5(*)KIL  -      DCP inY 8   NOP zpx 4   CMP zpx 4   DEC zpx 6   DCP zpx 6
E_  CPX imm 2   SBC inX 6   NOP imm 2   ISB inX 8   CPX zp  3   SBC zp  3   INC zp  5   ISB zp  5
F_  BEQ rel 2(b)SBC inY 5(*)KIL  -      ISB inY 8   NOP zpx 4   SBC zpx 4   INC zpx 6   ISB zpx 6
```
```
       8          9          A          B          C          D          E          F
0_  PHP Imp 3   ORA imm 2   ASL Acc 2   ANC imm 2   NOP abs 4   ORA abs 4   ASL abs 6   SLO abs 6
1_  CLC Imp 2   ORA aby 4(*)NOP Imp 2   SLO aby 7   NOP abx 4(*)ORA abx 4(*)ASL abx 7   SLO abx 7
2_  PLP Imp 4   AND imm 2   ROL Acc 2   ANC imm 2   BIT abs 4   AND abs 4   ROL abs 6   RLA abs 6
3_  SEC Imp 2   AND aby 4(*)NOP Imp 2   RLA aby 7   NOP abx 4(*)AND abx 4(*)ROL abx 7   RLA abx 7
4_  PHA Imp 3   EOR imm 2   LSR Acc 2   ALR imm 2   JMP abs 3   EOR abs 4   LSR abs 6   SRE abs 6
5_  CLI Imp 2   EOR aby 4(*)NOP Imp 2   SRE aby 7   NOP abx 4(*)EOR abx 4(*)LSR abx 7   SRE abx 7
6_  PLA Imp 4   ADC imm 2   ROR Acc 2   ARR imm 2   JMP ind 5   ADC abs 4   ROR abs 6   RRA abs 6
7_  SEI Imp 2   ADC aby 4(*)NOP Imp 2   RRA aby 7   NOP abx 4(*)ADC abx 4(*)ROR abx 7   RRA abx 7
8_  DEY Imp 2   NOP imm 2   TXA Imp 2   ANE imm 2   STY abs 4   STA abs 4   STX abs 4   SAX abs 4
9_  TYA Imp 2   STA aby 5   TXS Imp 2   TAS aby 5   SHY abx 5   STA abx 5   SHX aby 5   SHA aby 5
A_  TAY Imp 2   LDA imm 2   TAX Imp 2   LXA imm 2   LDY abs 4   LDA abs 4   LDX abs 4   LAX abs 4
B_  CLV Imp 2   LDA aby 4(*)TSX Imp 2   LAS aby 4(*)LDY abx 4(*)LDA abx 4(*)LDX aby 4(*)LAX aby 4(*)
C_  INY Imp 2   CMP imm 2   DEX Imp 2   AXS imm 2   CPY abs 4   CMP abs 4   DEC abs 6   DCP abs 6
D_  CLD Imp 2   CMP aby 4(*)NOP Imp 2   DCP aby 7   NOP abx 4(*)CMP abx 4(*)DEC abx 7   DCP abx 7
E_  INX Imp 2   SBC imm 2   NOP Imp 2   SBC imm 2   CPX abs 4   SBC abs 4   INC abs 6   ISB abs 6
F_  SED Imp 2   SBC aby 4(*)NOP Imp 2   ISB aby 7   NOP abx 4(*)SBC abx 4(*)INC abx 7   ISB abx 7
```

完整 256 個 cell 涵蓋。

---

## 14. 實作建議

### 14.1 起步：直接寫 switch

第一版直接 256-case switch，每個 case 內呼叫對應的 addressing mode helper + 操作 helper：

```csharp
switch (opcode)
{
    case 0xA9: LDA(Imm()); break;
    case 0xA5: LDA(ZP()); break;
    case 0xB5: LDA(ZPX()); break;
    case 0xAD: LDA(Abs()); break;
    // ... 256 entries
}
```

`Imm()`, `ZP()`, `Abs()` 等 helper 處理「取得運算元」+「推進 PC」+「累積 cycle」。`LDA(int m)` 處理 `A = m; flags(N, Z, A)`。

### 14.2 進階：函式指標表

效能優化：用 `delegate*<>` 或 `Action[]` 把 256 個 opcode 變成 lookup table，省掉 switch 的 jump table 開銷。AprNes 在 .NET 10 上用了 `delegate*<>` 路線。

### 14.3 Cycle accuracy：拆 micro-step

如果要過 cycle-accurate 測試（cpu_timing_test, cpu_interrupts_v2 等），把每條指令拆成「每個 cycle 做什麼」的 micro-step：

```
LDA $1234 (Abs):
  cycle 1: read opcode (PC++)
  cycle 2: read low byte (PC++)
  cycle 3: read high byte (PC++)
  cycle 4: read mem[1234]; A = result; flags; finish
```

每個 cycle 都要走過 bus dispatch（觸發 PPU/APU tick + DMC DMA 檢查）。

### 14.4 必過的測試 ROM

一般建議的 CPU 驗證順序：

1. **nestest.nes**（最有名的 CPU 測試）—— 涵蓋 official + 最常見 illegal opcode
2. **blargg's instr_test-v5**（official）+ **instr_misc** + **instr_timing**
3. **cpu_dummy_reads**、**cpu_dummy_writes_oam/ppumem**
4. **cpu_interrupts_v2**（含 NMI hijacking、IRQ timing）
5. **cpu_exec_space**（PRG/APU 範圍的執行）

過完這些代表 CPU 核心已經 cycle-accurate 了。

---

## 15. 參考資源

- **NESdev Wiki - 6502 reference**：https://www.nesdev.org/wiki/CPU
- **6502.org opcodes**：http://www.6502.org/tutorials/6502opcodes.html
- **Visual6502**：http://visual6502.org（電晶體級模擬，可看每個 cycle 的 bus 行為）
- **64doc.txt**（Marko Mäkelä）：64 系列電腦的 6502 完整文件，含 illegal opcode 的精確語意
- **TriCNES 原始碼**：`ref/TriCNES-main/`，per-master-clock 的精確 cycle 模型參考

---

## 重點整理

1. NES 的 6502 有 151 個官方 + 105 個非官方 opcode；nestest 等基本測試需要實作大部分 illegal。
2. NES 的 D（decimal）旗標可讀寫但**不影響運算**。
3. Page-cross 罰 1 cycle，但寫入指令永遠付這個成本。
4. Branch 採用加 1，採用且跨頁加 2。
5. RMW 指令會 dummy-write 一次原值再寫新值 —— 對 hardware register 有副作用。
6. JMP indirect 在 page boundary 有著名的 high-byte-不-跨頁 bug。
7. BRK + 同時 NMI 觸發會 hijacking 到 NMI vector。
8. 不穩定 opcode（SHA、SHX、TAS、ANE、LXA 等）建議實作為 NOP，商業遊戲幾乎不用。
