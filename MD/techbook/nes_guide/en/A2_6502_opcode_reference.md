# A2 6502 Complete 256-Opcode Implementation Reference

## What This Chapter Solves

When writing an NES emulator's CPU stage, you need a "given an opcode hex, what should it do?" lookup. This is the complete reference for implementing a 6502 / Ricoh 2A03 interpreter or cycle-accurate emulator:

- All 256 opcodes (151 official + 105 unofficial / illegal).
- For each opcode: addressing mode, bytes, cycles, affected flags, semantics.
- NES vs. stock 6502 differences (no BCD mode).
- Implementation rules common to all instructions (RMW, page-crossing, branch penalty, interrupt sequence).

Recommended reading first: [05 6502 CPU Core](05_6502_cpu_core.md) for register and addressing-mode basics.

---

## 0. First: Common Implementation Rules

Before each instruction, lay out the common behaviours so each entry doesn't need to repeat them.

### 0.1 Flags (the 7 bits of P register)

| bit | Name | Meaning |
|---|---|---|
| 7 | **N** (Negative) | bit 7 of result |
| 6 | **V** (Overflow) | signed-arithmetic overflow |
| 5 | — | Doesn't physically exist; pushed-to-stack copy is always 1 |
| 4 | **B** (Break) | Doesn't physically exist; 1 when pushed by BRK/PHP, 0 when pushed by IRQ/NMI |
| 3 | **D** (Decimal) | BCD mode — **ignored on NES; 6502 ADC/SBC don't take the BCD path** |
| 2 | **I** (Interrupt Disable) | 1 = mask IRQ |
| 1 | **Z** (Zero) | Whether the result is zero |
| 0 | **C** (Carry) | Carry / borrow |

**Crucial point for NES emulators**: the D (decimal) flag is readable / writable but **does not affect arithmetic results**. Ricoh 2A03 removed the BCD logic gates. ADC/SBC implementations don't need a BCD branch.

### 0.2 Addressing-Mode Abbreviations

| Abbr | Name | Example | Operand fetch |
|---|---|---|---|
| **Imp** | Implied | `CLC` | no operand |
| **Acc** | Accumulator | `ASL A` | operates on A |
| **Imm** | Immediate | `LDA #$42` | next byte is the value |
| **ZP** | Zero Page | `LDA $42` | `mem[$0042]` |
| **ZP,X** | Zero Page,X | `LDA $42,X` | `mem[($42 + X) & $FF]` (stays in zero page) |
| **ZP,Y** | Zero Page,Y | `LDX $42,Y` | `mem[($42 + Y) & $FF]` |
| **Abs** | Absolute | `LDA $1234` | `mem[$1234]` |
| **Abs,X** | Absolute,X | `LDA $1234,X` | `mem[$1234 + X]` (page crossing +1 cycle) |
| **Abs,Y** | Absolute,Y | `LDA $1234,Y` | `mem[$1234 + Y]` (page crossing +1 cycle) |
| **(Ind)** | Indirect | `JMP ($1234)` | `mem[$1234]` low, `mem[$1235]` high (**JMP page-boundary bug**) |
| **(Ind,X)** | Indexed Indirect | `LDA ($42,X)` | look up `mem[$42+X]` and `mem[$42+X+1]` in zero page, form 16-bit address, read |
| **(Ind),Y** | Indirect Indexed | `LDA ($42),Y` | `mem[$42]` and `mem[$43]` in zero page form base; add Y |
| **Rel** | Relative | `BNE $42` | PC adds a signed byte (used by branches) |

### 0.3 Page-Crossing Penalty

In `Abs,X` / `Abs,Y` / `(Ind),Y`, if base and base+index cross a 256-byte boundary (different high byte), **add 1 cycle**. E.g. `LDA $10F0,X` with X=$20 → `$10F0 + $20 = $1110` (crossed page), +1 cycle.

**Exception**: write instructions (STA/STX/STY etc.) and RMW instructions **always pay the page-cross cost** (whether or not actually crossed), because they perform a dummy read first.

### 0.4 Branch Penalty

Branch instructions (BCC/BCS/BEQ/BNE etc.):
- **not taken**: 2 cycles.
- **taken**: 3 cycles.
- **taken with page cross**: 4 cycles.

### 0.5 Read-Modify-Write (RMW)

ASL/LSR/ROL/ROR/INC/DEC (memory variants) execute as:

1. Read memory.
2. **Write the original value back** (dummy write — hardware writes the original value back in the same cycle).
3. Compute the result.
4. Write the new value.

Emulator impact: RMW on certain hardware registers (e.g., `$2007`) triggers **two side effects**; writing only once will misbehave for some PPU operations.

### 0.6 Stack Mechanics

- SP is an 8-bit register; the stack lives physically at `$0100`–`$01FF`.
- Real address = `$0100 | SP`.
- **Push** order: write `mem[$0100|SP]` first, **then** SP--.
- **Pull** order: SP++ first, **then** read `mem[$0100|SP]`.
- SP overflow is legal (wraps to the other end).

### 0.7 Interrupt Sequence

When NMI / IRQ / BRK fires, the CPU does:

```
1. (BRK only) PC++
2. push PC high
3. push PC low
4. push P (B flag: BRK/PHP=1, IRQ/NMI=0)
5. set I flag = 1
6. PC = mem[vector_low] | (mem[vector_high] << 8)
```

| Vector | Address |
|---|---|
| NMI | `$FFFA-$FFFB` |
| Reset | `$FFFC-$FFFD` |
| IRQ / BRK | `$FFFE-$FFFF` |

**Interrupt hijacking**: if BRK and NMI fire simultaneously, they "merge" — after BRK starts pushing, NMI fires; the final jump goes to the NMI vector instead of IRQ. Required to pass cpu_interrupts_v2.

The full interrupt sequence takes 7 cycles.

### 0.8 NES vs 6502 Differences

| Item | Stock 6502 | Ricoh 2A03 (NES) |
|---|---|---|
| BCD mode | Yes | **No** (D flag readable/writable but ignored by ADC/SBC) |
| Integrated APU | No | **Yes** (addresses `$4000`–`$4017`) |
| Effect of decimal flag | Affects arithmetic | No effect at all |

---

## 1. Load / Store

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

Note: write-form Abs,X / Abs,Y / (Ind),Y always pays the page-cross cost (even when not actually crossed).

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

## 2. Transfer

```
TAX  AA  Imp  1B  2C   X = A; flags(N,Z) on X
TAY  A8  Imp  1B  2C   Y = A; flags(N,Z) on Y
TXA  8A  Imp  1B  2C   A = X; flags(N,Z) on A
TYA  98  Imp  1B  2C   A = Y; flags(N,Z) on A
TSX  BA  Imp  1B  2C   X = SP; flags(N,Z) on X
TXS  9A  Imp  1B  2C   SP = X; (no flags)
```

`TXS` is the only transfer that **does not update flags**.

---

## 3. Stack

```
PHA  48  Imp  1B  3C   push A
PHP  08  Imp  1B  3C   push P (with B=1, bit 5=1)
PLA  68  Imp  1B  4C   pull A; flags(N,Z) on A
PLP  28  Imp  1B  4C   pull P (B and bit 5 don't affect the real P)
```

---

## 4. Arithmetic (ADC / SBC)

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
A = A - M - (1 - C)   equivalent to  A = A + (~M) + C
Flag rules same as ADC (substitute ~M for M in V formula)
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
| `EB` | Imm | 2 | 2 | **unofficial; equivalent to SBC #imm** |

---

## 5. Logical (AND / ORA / EOR / BIT)

Same shape as LDA (same 8 modes); result stored in A; updates N, Z.

```
AND  29 25 35 2D 3D 39 21 31    A = A & M
ORA  09 05 15 0D 1D 19 01 11    A = A | M
EOR  49 45 55 4D 5D 59 41 51    A = A ^ M
```

Cycles match the LDA positions exactly.

### BIT — Test Bits
```
result = A & M
Z = (result == 0)
N = M.bit7
V = M.bit6
A unchanged
```

| Op | Mode | B | C |
|---|---|---|---|
| `24` | ZP | 2 | 3 |
| `2C` | Abs | 3 | 4 |

---

## 6. Compare (CMP / CPX / CPY)

```
result = REG - M
Z = (REG == M)
C = (REG >= M)
N = result.bit7
REG and M unchanged
```

### CMP (compare A)

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

### CPX (compare X) / CPY (compare Y)

```
CPX  E0 Imm 2B 2C   E4 ZP 2B 3C   EC Abs 3B 4C
CPY  C0 Imm 2B 2C   C4 ZP 2B 3C   CC Abs 3B 4C
```

---

## 7. Increment / Decrement

### INC / DEC (memory RMW)

```
INC  E6 ZP 2B 5C    F6 ZP,X 2B 6C    EE Abs 3B 6C    FE Abs,X 3B 7C
DEC  C6 ZP 2B 5C    D6 ZP,X 2B 6C    CE Abs 3B 6C    DE Abs,X 3B 7C
```

Flags N, Z reflect the new value.

### INX / DEX / INY / DEY (register variants)

```
INX  E8  Imp  1B  2C   X++; flags(N,Z) on X
DEX  CA  Imp  1B  2C   X--; flags(N,Z) on X
INY  C8  Imp  1B  2C   Y++; flags(N,Z) on Y
DEY  88  Imp  1B  2C   Y--; flags(N,Z) on Y
```

---

## 8. Shift / Rotate (RMW)

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
N = 0 (always), Z = (new == 0)
```

| Op | Mode | B | C |
|---|---|---|---|
| `4A` | Acc | 1 | 2 |
| `46` | ZP | 2 | 5 |
| `56` | ZP,X | 2 | 6 |
| `4E` | Abs | 3 | 6 |
| `5E` | Abs,X | 3 | 7 |

### ROL — Rotate Left (through Carry)
```
new = (old << 1) | C
C = old.bit7
flags as for ASL (using new)
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
N = old C in new's bit 7 position (i.e. new.bit7 = old C)
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

### Branch (conditional)

Taken → +1 cycle; cross page → +1 more.

| Op | Mn | Condition | B | C(no) | C(yes) |
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

| Op | Mode | B | C | Note |
|---|---|---|---|---|
| `4C` | Abs | 3 | 3 | direct jump |
| `6C` | (Ind) | 3 | 5 | **JMP page-boundary bug**: when the indirect address's low byte is `$FF`, the high byte does not cross pages. E.g. `JMP ($10FF)` reads `mem[$10FF]` and `mem[$1000]` (not `$1100`). The emulator **must** reproduce this bug. |

### JSR / RTS / RTI / BRK

```
JSR  20 Abs  3B 6C   push (PC-1) high, push (PC-1) low, PC = target
RTS  60 Imp  1B 6C   pull PC low, pull PC high, PC = (popped + 1)
RTI  40 Imp  1B 6C   pull P, pull PC low, pull PC high, **PC NOT +1**
BRK  00 Imp  2B 7C   PC++, push PC high, push PC low, push P|0x10,
                     I=1, PC = mem[$FFFE/F]
```

Note BRK is a **2-byte instruction** (`00 XX`, padding byte arbitrary), but PC has advanced twice when it executes.

---

## 10. Status-Flag Control

```
CLC  18  Imp  1B  2C   C = 0
SEC  38  Imp  1B  2C   C = 1
CLI  58  Imp  1B  2C   I = 0
SEI  78  Imp  1B  2C   I = 1
CLV  B8  Imp  1B  2C   V = 0
CLD  D8  Imp  1B  2C   D = 0    (D doesn't affect arithmetic on NES, but is still writable)
SED  F8  Imp  1B  2C   D = 1    (same)
```

---

## 11. NOP

```
NOP  EA  Imp  1B  2C
```

---

## 12. Unofficial / Illegal Opcodes

Some old NES games use these unofficial opcodes (most common: LAX, SAX, DCP, ISB). Implementing them is required to pass tests like nestest 100%.

The ⭐ marker indicates ones commonly used by commercial NES games.

### 12.1 Combined RMW (one opcode does two things) ⭐

| Op | Mn | Equivalent | Description |
|---|---|---|---|
| `C7,D7,CF,DF,DB,C3,D3` | **DCP** ⭐ | DEC + CMP | M--; CMP |
| `E7,F7,EF,FF,FB,E3,F3` | **ISB / ISC** ⭐ | INC + SBC | M++; A = A - M |
| `07,17,0F,1F,1B,03,13` | **SLO / ASO** | ASL + ORA | M = M<<1; A |= M |
| `27,37,2F,3F,3B,23,33` | **RLA** ⭐ | ROL + AND | M = ROL(M); A &= M |
| `47,57,4F,5F,5B,43,53` | **SRE / LSE** | LSR + EOR | M = M>>1; A ^= M |
| `67,77,6F,7F,7B,63,73` | **RRA** | ROR + ADC | M = ROR(M); A = A+M+C |

### 12.2 LAX / SAX ⭐

```
LAX  A7,B7,AF,BF,A3,B3        A = X = M; flags(N,Z)  ⭐ (commonly used)
SAX  87,97,8F,83              M = A & X; (no flags)  ⭐ (commonly used)
```

LAX has no immediate mode (only the 6 modes above). `AB` (LXA, immediate) is an unstable illegal opcode; not recommended to implement.

### 12.3 ANC / ALR / ARR / AXS / SBC dup

```
ANC  0B, 2B  Imm  2B 2C    A = A & M; C = N (= bit 7 of result)
ALR  4B      Imm  2B 2C    A = A & M; A = A >> 1; flags
ARR  6B      Imm  2B 2C    A = A & M; A = ROR(A); flags computed specially
                            (V = A.bit5 ^ A.bit6, C = A.bit6)
AXS  CB      Imm  2B 2C    X = (A & X) - M; flags
SBC* EB      Imm  2B 2C    same as SBC #imm (already listed in SBC section)
```

### 12.4 NOP Variants (multi-byte / multi-cycle)

A pile of "do nothing but consume bytes and cycles" instructions. Commercial games occasionally use them as timing padding.

| Op | Mode | B | C |
|---|---|---|---|
| `1A,3A,5A,7A,DA,FA` | Imp | 1 | 2 |
| `80,82,89,C2,E2` | Imm | 2 | 2 |
| `04,44,64` | ZP | 2 | 3 |
| `14,34,54,74,D4,F4` | ZP,X | 2 | 4 |
| `0C` | Abs | 3 | 4 |
| `1C,3C,5C,7C,DC,FC` | Abs,X | 3 | 4 (+1 page) |

### 12.5 Unstable / Dangerous Opcodes

These behave inconsistently across 6502/2A03 batches. **Recommended: implement them as "read memory but don't affect registers" or treat as NOP**. Commercial games almost never use them:

```
SHA / AHX  93, 9F           store A & X & (high+1)
SHX / SXA  9E               store X & (high+1)
SHY / SYA  9C               store Y & (high+1)
TAS / SHS  9B               SP = A & X; store SP & (high+1)
LAS / LAR  BB               A = X = SP = M & SP
ANE / XAA  8B  Imm          A = (A | const) & X & M  (very unstable)
LXA / OAL  AB  Imm          A = X = (A | const) & M  (very unstable)
```

### 12.6 KIL / JAM / HLT — CPU Lockup

```
$02, $12, $22, $32, $42, $52, $62, $72, $92, $B2, $D2, $F2
```

Executing one of these locks the CPU permanently (PC doesn't advance, interrupts ignored). Emulators can:
- Actually freeze PC (most faithful to hardware).
- Print a warning + treat as NOP (easier debugging).

---

## 13. Quick Lookup Table (by hex, hi nibble × lo nibble)

Notation: **Off** = official; **Un** = unofficial; **(*)** = page cross may add cycle; **(b)** = branch taken adds cycle.

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

All 256 cells covered.

---

## 14. Implementation Suggestions

### 14.1 Starter: a plain switch

The first version uses a 256-case switch where each case calls the matching addressing-mode helper + operation helper:

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

`Imm()`, `ZP()`, `Abs()`, etc. handle "fetch operand" + "advance PC" + "accumulate cycles". `LDA(int m)` handles `A = m; flags(N, Z, A)`.

### 14.2 Advanced: function pointer table

Performance optimisation: turn 256 opcodes into a lookup table using `delegate*<>` or `Action[]`, eliminating switch jump-table overhead. AprNes uses the `delegate*<>` route on .NET 10.

### 14.3 Cycle accuracy: split into micro-steps

For cycle-accurate testing (cpu_timing_test, cpu_interrupts_v2, etc.), break each instruction into per-cycle micro-steps:

```
LDA $1234 (Abs):
  cycle 1: read opcode (PC++)
  cycle 2: read low byte (PC++)
  cycle 3: read high byte (PC++)
  cycle 4: read mem[1234]; A = result; flags; finish
```

Every cycle goes through bus dispatch (triggering PPU/APU tick + DMC DMA checks).

### 14.4 Required Test ROMs

Recommended CPU verification order:

1. **nestest.nes** (the most famous CPU test) — covers official + most common illegal opcodes.
2. **blargg's instr_test-v5** (official) + **instr_misc** + **instr_timing**.
3. **cpu_dummy_reads**, **cpu_dummy_writes_oam/ppumem**.
4. **cpu_interrupts_v2** (NMI hijacking, IRQ timing).
5. **cpu_exec_space** (PRG/APU range execution).

Passing these means the CPU core is essentially cycle-accurate.

---

## 15. References

- **NESdev Wiki - 6502 reference**: https://www.nesdev.org/wiki/CPU
- **6502.org opcodes**: http://www.6502.org/tutorials/6502opcodes.html
- **Visual6502**: http://visual6502.org (transistor-level simulation; observes per-cycle bus behaviour)
- **64doc.txt** (Marko Mäkelä): full 6502 documentation for the C64 family, including precise illegal-opcode semantics
- **TriCNES source code**: `ref/TriCNES-main/`, a per-master-clock cycle-precise model.

---

## Recap

1. The NES 6502 has 151 official + 105 unofficial opcodes; nestest and friends require most illegal ones to be implemented.
2. The NES D (decimal) flag is readable/writable but **does not affect arithmetic**.
3. Page-cross adds 1 cycle, but write instructions always pay the cost.
4. Branch taken adds 1; taken across pages adds 2.
5. RMW instructions dummy-write the original before the new value — affects hardware registers.
6. JMP indirect has the famous page-boundary high-byte-doesn't-cross bug.
7. BRK + simultaneous NMI hijacks to the NMI vector.
8. Unstable opcodes (SHA, SHX, TAS, ANE, LXA, etc.) are best treated as NOP — commercial games almost never use them.
