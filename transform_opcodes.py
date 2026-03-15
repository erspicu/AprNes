"""
Transform CPU.cs Op_XX handlers to use shared dispatch helpers.
Groups converted:
  Read ops: ORA, AND, EOR, ADC, SBC, CMP/CPX/CPY, LDA, LDX, LDY  (~78 handlers)
  Write ops: STA, STX, STY, SAX                                    (~17 handlers)
  RMW ops:   ASL, LSR, ROL, ROR, INC, DEC + SLO, RLA, SRE, RRA, DCP, ISC (~66 handlers)
"""
import re, sys

with open('AprNes/NesCore/CPU.cs', 'r', encoding='utf-8') as f:
    src = f.read()

HELPERS = r"""
        // === Shared addressing mode dispatch helpers (read operations) ===
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void ExecReadImm(delegate*<byte, void> op) {
            GetImmediate(); op(dl); CompleteOperation();
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void ExecReadZP(delegate*<byte, void> op) {
            if (operationCycle == 1) GetAddressZeroPage();
            else { op(CpuRead(addressBus)); CompleteOperation(); }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void ExecReadZPX(delegate*<byte, void> op) {
            if (operationCycle < 3) GetAddressZPOffX();
            else { op(CpuRead(addressBus)); CompleteOperation(); }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void ExecReadZPY(delegate*<byte, void> op) {
            if (operationCycle < 3) GetAddressZPOffY();
            else { op(CpuRead(addressBus)); CompleteOperation(); }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void ExecReadAbs(delegate*<byte, void> op) {
            if (operationCycle < 3) GetAddressAbsolute();
            else { op(CpuRead(addressBus)); CompleteOperation(); }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void ExecReadAbsX(delegate*<byte, void> op) {
            if (operationCycle < 4) GetAddressAbsOffX(true);
            else { op(CpuRead(addressBus)); CompleteOperation(); }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void ExecReadAbsY(delegate*<byte, void> op) {
            if (operationCycle < 4) GetAddressAbsOffY(true);
            else { op(CpuRead(addressBus)); CompleteOperation(); }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void ExecReadIndX(delegate*<byte, void> op) {
            if (operationCycle < 5) GetAddressIndOffX();
            else { op(CpuRead(addressBus)); CompleteOperation(); }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void ExecReadIndY(delegate*<byte, void> op) {
            if (operationCycle < 5) GetAddressIndOffY(true);
            else { op(CpuRead(addressBus)); CompleteOperation(); }
        }
        // === Shared addressing mode dispatch helpers (write operations) ===
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void ExecWriteZP(byte val) {
            if (operationCycle == 1) GetAddressZeroPage();
            else { CpuWrite(addressBus, val); CompleteOperation(); }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void ExecWriteZPX(byte val) {
            if (operationCycle < 3) GetAddressZPOffX();
            else { CpuWrite(addressBus, val); CompleteOperation(); }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void ExecWriteZPY(byte val) {
            if (operationCycle < 3) GetAddressZPOffY();
            else { CpuWrite(addressBus, val); CompleteOperation(); }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void ExecWriteAbs(byte val) {
            if (operationCycle < 3) GetAddressAbsolute();
            else { CpuWrite(addressBus, val); CompleteOperation(); }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void ExecWriteAbsX(byte val) {
            if (operationCycle < 4) GetAddressAbsOffX(false);
            else { CpuWrite(addressBus, val); CompleteOperation(); }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void ExecWriteAbsY(byte val) {
            if (operationCycle < 4) GetAddressAbsOffY(false);
            else { CpuWrite(addressBus, val); CompleteOperation(); }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void ExecWriteIndX(byte val) {
            if (operationCycle < 5) GetAddressIndOffX();
            else { CpuWrite(addressBus, val); CompleteOperation(); }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void ExecWriteIndY(byte val) {
            if (operationCycle < 5) GetAddressIndOffY(false);
            else { CpuWrite(addressBus, val); CompleteOperation(); }
        }
        // === Shared addressing mode dispatch helpers (RMW operations) ===
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void ExecRMW_ZP(delegate*<ushort, void> op) {
            if (operationCycle < 2) GetAddressZeroPage();
            else if (operationCycle == 2) { dl = CpuRead(addressBus); }
            else if (operationCycle == 3) { CpuWrite(addressBus, dl); }
            else { op(addressBus); CompleteOperation(); }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void ExecRMW_ZPX(delegate*<ushort, void> op) {
            if (operationCycle < 3) GetAddressZPOffX();
            else if (operationCycle == 3) { dl = CpuRead(addressBus); }
            else if (operationCycle == 4) { CpuWrite(addressBus, dl); }
            else { op(addressBus); CompleteOperation(); }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void ExecRMW_Abs(delegate*<ushort, void> op) {
            if (operationCycle < 3) GetAddressAbsolute();
            else if (operationCycle == 3) { dl = CpuRead(addressBus); }
            else if (operationCycle == 4) { CpuWrite(addressBus, dl); }
            else { op(addressBus); CompleteOperation(); }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void ExecRMW_AbsX(delegate*<ushort, void> op) {
            if (operationCycle < 5) GetAddressAbsOffX(false);
            else if (operationCycle == 5) { CpuWrite(addressBus, dl); }
            else { op(addressBus); CompleteOperation(); }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void ExecRMW_AbsY(delegate*<ushort, void> op) {
            if (operationCycle < 5) GetAddressAbsOffY(false);
            else if (operationCycle == 5) { CpuWrite(addressBus, dl); }
            else { op(addressBus); CompleteOperation(); }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void ExecRMW_IndX(delegate*<ushort, void> op) {
            if (operationCycle < 5) GetAddressIndOffX();
            else if (operationCycle == 5) { dl = CpuRead(addressBus); }
            else if (operationCycle == 6) { CpuWrite(addressBus, dl); }
            else { op(addressBus); CompleteOperation(); }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void ExecRMW_IndY(delegate*<ushort, void> op) {
            if (operationCycle < 5) GetAddressIndOffY(false);
            else if (operationCycle == 5) { dl = CpuRead(addressBus); }
            else if (operationCycle == 6) { CpuWrite(addressBus, dl); }
            else { op(addressBus); CompleteOperation(); }
        }
        // === CMP / Load register wrapper stubs ===
        static void Op_CMPA(byte v) => Op_CMP(v, r_A);
        static void Op_CMPX(byte v) => Op_CMP(v, r_X);
        static void Op_CMPY(byte v) => Op_CMP(v, r_Y);
        static void Op_LDA_r(byte v) { r_A = v; SetNZ(r_A); }
        static void Op_LDX_r(byte v) { r_X = v; SetNZ(r_X); }
        static void Op_LDY_r(byte v) { r_Y = v; SetNZ(r_Y); }
        static void Op_LAX_r(byte v) { r_A = v; r_X = v; SetNZ(r_X); }

"""

# Insert helpers before Op_Default
ANCHOR = '        // === Named static op methods (for delegate* function pointer table) ===\n\n        static void Op_Default()'
src = src.replace(ANCHOR, '        // === Named static op methods (for delegate* function pointer table) ===\n' + HELPERS + '        static void Op_Default()')

# -----------------------------------------------------------------------
# READ OPERATION REPLACEMENTS
# Each tuple: (opcode_hex, helper_name, op_fn)
# -----------------------------------------------------------------------
READ_OPS = [
    # ORA
    ('09', 'ExecReadImm',  'Op_ORA'),
    ('05', 'ExecReadZP',   'Op_ORA'),
    ('15', 'ExecReadZPX',  'Op_ORA'),
    ('0D', 'ExecReadAbs',  'Op_ORA'),
    ('1D', 'ExecReadAbsX', 'Op_ORA'),
    ('19', 'ExecReadAbsY', 'Op_ORA'),
    ('01', 'ExecReadIndX', 'Op_ORA'),
    ('11', 'ExecReadIndY', 'Op_ORA'),
    # AND
    ('29', 'ExecReadImm',  'Op_AND'),
    ('25', 'ExecReadZP',   'Op_AND'),
    ('35', 'ExecReadZPX',  'Op_AND'),
    ('2D', 'ExecReadAbs',  'Op_AND'),
    ('3D', 'ExecReadAbsX', 'Op_AND'),
    ('39', 'ExecReadAbsY', 'Op_AND'),
    ('21', 'ExecReadIndX', 'Op_AND'),
    ('31', 'ExecReadIndY', 'Op_AND'),
    # EOR
    ('49', 'ExecReadImm',  'Op_EOR'),
    ('45', 'ExecReadZP',   'Op_EOR'),
    ('55', 'ExecReadZPX',  'Op_EOR'),
    ('4D', 'ExecReadAbs',  'Op_EOR'),
    ('5D', 'ExecReadAbsX', 'Op_EOR'),
    ('59', 'ExecReadAbsY', 'Op_EOR'),
    ('41', 'ExecReadIndX', 'Op_EOR'),
    ('51', 'ExecReadIndY', 'Op_EOR'),
    # ADC
    ('69', 'ExecReadImm',  'Op_ADC'),
    ('65', 'ExecReadZP',   'Op_ADC'),
    ('75', 'ExecReadZPX',  'Op_ADC'),
    ('6D', 'ExecReadAbs',  'Op_ADC'),
    ('7D', 'ExecReadAbsX', 'Op_ADC'),
    ('79', 'ExecReadAbsY', 'Op_ADC'),
    ('61', 'ExecReadIndX', 'Op_ADC'),
    ('71', 'ExecReadIndY', 'Op_ADC'),
    # SBC
    ('E9_SBC_Imm', 'ExecReadImm', 'Op_SBC'),  # special name
    ('E5', 'ExecReadZP',   'Op_SBC'),
    ('F5', 'ExecReadZPX',  'Op_SBC'),
    ('ED', 'ExecReadAbs',  'Op_SBC'),
    ('FD', 'ExecReadAbsX', 'Op_SBC'),
    ('F9', 'ExecReadAbsY', 'Op_SBC'),
    ('E1', 'ExecReadIndX', 'Op_SBC'),
    ('F1', 'ExecReadIndY', 'Op_SBC'),
    # CMP (A)
    ('C9', 'ExecReadImm',  'Op_CMPA'),
    ('C5', 'ExecReadZP',   'Op_CMPA'),
    ('D5', 'ExecReadZPX',  'Op_CMPA'),
    ('CD', 'ExecReadAbs',  'Op_CMPA'),
    ('DD', 'ExecReadAbsX', 'Op_CMPA'),
    ('D9', 'ExecReadAbsY', 'Op_CMPA'),
    ('C1', 'ExecReadIndX', 'Op_CMPA'),
    ('D1', 'ExecReadIndY', 'Op_CMPA'),
    # CPX
    ('E0', 'ExecReadImm', 'Op_CMPX'),
    ('E4', 'ExecReadZP',  'Op_CMPX'),
    ('EC', 'ExecReadAbs', 'Op_CMPX'),
    # CPY
    ('C0', 'ExecReadImm', 'Op_CMPY'),
    ('C4', 'ExecReadZP',  'Op_CMPY'),
    ('CC', 'ExecReadAbs', 'Op_CMPY'),
    # LDA
    ('A9', 'ExecReadImm',  'Op_LDA_r'),
    ('A5', 'ExecReadZP',   'Op_LDA_r'),
    ('B5', 'ExecReadZPX',  'Op_LDA_r'),
    ('AD', 'ExecReadAbs',  'Op_LDA_r'),
    ('BD', 'ExecReadAbsX', 'Op_LDA_r'),
    ('B9', 'ExecReadAbsY', 'Op_LDA_r'),
    ('A1', 'ExecReadIndX', 'Op_LDA_r'),
    ('B1', 'ExecReadIndY', 'Op_LDA_r'),
    # LDX
    ('A2', 'ExecReadImm',  'Op_LDX_r'),
    ('A6', 'ExecReadZP',   'Op_LDX_r'),
    ('B6', 'ExecReadZPY',  'Op_LDX_r'),
    ('AE', 'ExecReadAbs',  'Op_LDX_r'),
    ('BE', 'ExecReadAbsY', 'Op_LDX_r'),
    # LDY
    ('A0', 'ExecReadImm',  'Op_LDY_r'),
    ('A4', 'ExecReadZP',   'Op_LDY_r'),
    ('B4', 'ExecReadZPX',  'Op_LDY_r'),
    ('AC', 'ExecReadAbs',  'Op_LDY_r'),
    ('BC', 'ExecReadAbsX', 'Op_LDY_r'),
    # LAX (undocumented: load A and X)
    ('A7', 'ExecReadZP',   'Op_LAX_r'),
    ('B7', 'ExecReadZPY',  'Op_LAX_r'),
    ('AF', 'ExecReadAbs',  'Op_LAX_r'),
    ('BF', 'ExecReadAbsY', 'Op_LAX_r'),
    ('A3', 'ExecReadIndX', 'Op_LAX_r'),
    ('B3', 'ExecReadIndY', 'Op_LAX_r'),
]

# -----------------------------------------------------------------------
# WRITE OPERATION REPLACEMENTS
# -----------------------------------------------------------------------
# STA
WRITE_OPS = [
    ('85', 'ExecWriteZP',   'r_A'),
    ('95', 'ExecWriteZPX',  'r_A'),
    ('8D', 'ExecWriteAbs',  'r_A'),
    ('9D', 'ExecWriteAbsX', 'r_A'),
    ('99', 'ExecWriteAbsY', 'r_A'),
    ('81', 'ExecWriteIndX', 'r_A'),
    ('91', 'ExecWriteIndY', 'r_A'),
    # STX
    ('86', 'ExecWriteZP',   'r_X'),
    ('96', 'ExecWriteZPY',  'r_X'),
    ('8E', 'ExecWriteAbs',  'r_X'),
    # STY
    ('84', 'ExecWriteZP',   'r_Y'),
    ('94', 'ExecWriteZPX',  'r_Y'),
    ('8C', 'ExecWriteAbs',  'r_Y'),
    # SAX (write A & X)
    ('87', 'ExecWriteZP',   '(byte)(r_A & r_X)'),
    ('97', 'ExecWriteZPY',  '(byte)(r_A & r_X)'),
    ('8F', 'ExecWriteAbs',  '(byte)(r_A & r_X)'),
    ('83', 'ExecWriteIndX', '(byte)(r_A & r_X)'),
]

# -----------------------------------------------------------------------
# RMW OPERATION REPLACEMENTS
# -----------------------------------------------------------------------
RMW_OPS = [
    # ASL
    ('06', 'ExecRMW_ZP',   'Op_ASL_mem'),
    ('16', 'ExecRMW_ZPX',  'Op_ASL_mem'),
    ('0E', 'ExecRMW_Abs',  'Op_ASL_mem'),
    ('1E', 'ExecRMW_AbsX', 'Op_ASL_mem'),
    # LSR
    ('46', 'ExecRMW_ZP',   'Op_LSR_mem'),
    ('56', 'ExecRMW_ZPX',  'Op_LSR_mem'),
    ('4E', 'ExecRMW_Abs',  'Op_LSR_mem'),
    ('5E', 'ExecRMW_AbsX', 'Op_LSR_mem'),
    # ROL
    ('26', 'ExecRMW_ZP',   'Op_ROL_mem'),
    ('36', 'ExecRMW_ZPX',  'Op_ROL_mem'),
    ('2E', 'ExecRMW_Abs',  'Op_ROL_mem'),
    ('3E', 'ExecRMW_AbsX', 'Op_ROL_mem'),
    # ROR
    ('66', 'ExecRMW_ZP',   'Op_ROR_mem'),
    ('76', 'ExecRMW_ZPX',  'Op_ROR_mem'),
    ('6E', 'ExecRMW_Abs',  'Op_ROR_mem'),
    ('7E', 'ExecRMW_AbsX', 'Op_ROR_mem'),
    # INC
    ('E6', 'ExecRMW_ZP',   'Op_INC_mem'),
    ('F6', 'ExecRMW_ZPX',  'Op_INC_mem'),
    ('EE', 'ExecRMW_Abs',  'Op_INC_mem'),
    ('FE', 'ExecRMW_AbsX', 'Op_INC_mem'),
    # DEC
    ('C6', 'ExecRMW_ZP',   'Op_DEC_mem'),
    ('D6', 'ExecRMW_ZPX',  'Op_DEC_mem'),
    ('CE', 'ExecRMW_Abs',  'Op_DEC_mem'),
    ('DE', 'ExecRMW_AbsX', 'Op_DEC_mem'),
    # SLO (undocumented)
    ('07', 'ExecRMW_ZP',   'Op_SLO'),
    ('17', 'ExecRMW_ZPX',  'Op_SLO'),
    ('0F', 'ExecRMW_Abs',  'Op_SLO'),
    ('1F', 'ExecRMW_AbsX', 'Op_SLO'),
    ('1B', 'ExecRMW_AbsY', 'Op_SLO'),
    ('03', 'ExecRMW_IndX', 'Op_SLO'),
    ('13', 'ExecRMW_IndY', 'Op_SLO'),
    # RLA (undocumented)
    ('27', 'ExecRMW_ZP',   'Op_RLA'),
    ('37', 'ExecRMW_ZPX',  'Op_RLA'),
    ('2F', 'ExecRMW_Abs',  'Op_RLA'),
    ('3F', 'ExecRMW_AbsX', 'Op_RLA'),
    ('3B', 'ExecRMW_AbsY', 'Op_RLA'),
    ('23', 'ExecRMW_IndX', 'Op_RLA'),
    ('33', 'ExecRMW_IndY', 'Op_RLA'),
    # SRE (undocumented)
    ('47', 'ExecRMW_ZP',   'Op_SRE'),
    ('57', 'ExecRMW_ZPX',  'Op_SRE'),
    ('4F', 'ExecRMW_Abs',  'Op_SRE'),
    ('5F', 'ExecRMW_AbsX', 'Op_SRE'),
    ('5B', 'ExecRMW_AbsY', 'Op_SRE'),
    ('43', 'ExecRMW_IndX', 'Op_SRE'),
    ('53', 'ExecRMW_IndY', 'Op_SRE'),
    # RRA (undocumented)
    ('67', 'ExecRMW_ZP',   'Op_RRA'),
    ('77', 'ExecRMW_ZPX',  'Op_RRA'),
    ('6F', 'ExecRMW_Abs',  'Op_RRA'),
    ('7F', 'ExecRMW_AbsX', 'Op_RRA'),
    ('7B', 'ExecRMW_AbsY', 'Op_RRA'),
    ('63', 'ExecRMW_IndX', 'Op_RRA'),
    ('73', 'ExecRMW_IndY', 'Op_RRA'),
    # DCP (undocumented)
    ('C7', 'ExecRMW_ZP',   'Op_DCP'),
    ('D7', 'ExecRMW_ZPX',  'Op_DCP'),
    ('CF', 'ExecRMW_Abs',  'Op_DCP'),
    ('DF', 'ExecRMW_AbsX', 'Op_DCP'),
    ('DB', 'ExecRMW_AbsY', 'Op_DCP'),
    ('C3', 'ExecRMW_IndX', 'Op_DCP'),
    ('D3', 'ExecRMW_IndY', 'Op_DCP'),
    # ISC (undocumented)
    ('E7', 'ExecRMW_ZP',   'Op_ISC'),
    ('F7', 'ExecRMW_ZPX',  'Op_ISC'),
    ('EF', 'ExecRMW_Abs',  'Op_ISC'),
    ('FF', 'ExecRMW_AbsX', 'Op_ISC'),
    ('FB', 'ExecRMW_AbsY', 'Op_ISC'),
    ('E3', 'ExecRMW_IndX', 'Op_ISC'),
    ('F3', 'ExecRMW_IndY', 'Op_ISC'),
]

def replace_read_op(src, opcode_name, helper, op_fn):
    method_name = f'Op_{opcode_name}'
    # Match the entire method body (may be 1-line or multi-line)
    # Pattern: static void Op_XX() { ... } (possibly multi-line up to closing brace at same indent)
    # We need to replace the entire method definition

    new_body = f'        static void {method_name}() {{ {helper}(&{op_fn}); }}'

    # Try single-line first: static void Op_XX() { ... }
    pattern_single = rf'        static void {re.escape(method_name)}\(\) \{{[^\n]+\}}'
    if re.search(pattern_single, src):
        src = re.sub(pattern_single, new_body, src)
        return src, True

    # Try multi-line: static void Op_XX() {\n    ...\n    ...\n        }
    pattern_multi = rf'        static void {re.escape(method_name)}\(\) \{{\n(?:.*\n)*?        \}}'
    m = re.search(pattern_multi, src)
    if m:
        src = src[:m.start()] + new_body + src[m.end():]
        return src, True

    print(f'  WARNING: Could not find {method_name}', file=sys.stderr)
    return src, False

def replace_write_op(src, opcode_name, helper, val_expr):
    method_name = f'Op_{opcode_name}'
    new_body = f'        static void {method_name}() {{ {helper}({val_expr}); }}'

    pattern_single = rf'        static void {re.escape(method_name)}\(\) \{{[^\n]+\}}'
    if re.search(pattern_single, src):
        src = re.sub(pattern_single, new_body, src)
        return src, True

    pattern_multi = rf'        static void {re.escape(method_name)}\(\) \{{\n(?:.*\n)*?        \}}'
    m = re.search(pattern_multi, src)
    if m:
        src = src[:m.start()] + new_body + src[m.end():]
        return src, True

    print(f'  WARNING: Could not find {method_name}', file=sys.stderr)
    return src, False

def replace_rmw_op(src, opcode_name, helper, op_fn):
    method_name = f'Op_{opcode_name}'
    new_body = f'        static void {method_name}() {{ {helper}(&{op_fn}); }}'

    pattern_single = rf'        static void {re.escape(method_name)}\(\) \{{[^\n]+\}}'
    if re.search(pattern_single, src):
        src = re.sub(pattern_single, new_body, src)
        return src, True

    pattern_multi = rf'        static void {re.escape(method_name)}\(\) \{{\n(?:.*\n)*?        \}}'
    m = re.search(pattern_multi, src)
    if m:
        src = src[:m.start()] + new_body + src[m.end():]
        return src, True

    print(f'  WARNING: Could not find {method_name}', file=sys.stderr)
    return src, False

total_replaced = 0

print("Processing READ ops...")
for opcode, helper, op_fn in READ_OPS:
    src, ok = replace_read_op(src, opcode, helper, op_fn)
    if ok:
        total_replaced += 1
        print(f'  Op_{opcode} -> {helper}(&{op_fn})')

print("\nProcessing WRITE ops...")
for opcode, helper, val_expr in WRITE_OPS:
    src, ok = replace_write_op(src, opcode, helper, val_expr)
    if ok:
        total_replaced += 1
        print(f'  Op_{opcode} -> {helper}({val_expr})')

print("\nProcessing RMW ops...")
for opcode, helper, op_fn in RMW_OPS:
    src, ok = replace_rmw_op(src, opcode, helper, op_fn)
    if ok:
        total_replaced += 1
        print(f'  Op_{opcode} -> {helper}(&{op_fn})')

print(f"\nTotal replaced: {total_replaced}")

with open('AprNes/NesCore/CPU.cs', 'w', encoding='utf-8') as f:
    f.write(src)

print("Done. Written to AprNes/NesCore/CPU.cs")
