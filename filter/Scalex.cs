using System.Threading.Tasks;

namespace ScalexFilter
{
    // ============================================================
    // Scale2x / Scale3x 濾鏡 - .NET 4.8.1 究極無分支 + 64-bit 打包版
    // ============================================================
    public unsafe class ScalexTool
    {
        public static void toScale2x_dx(uint* src_fast, int org_width, int org_height, uint* buffer_2x)
        {
            if (org_width <= 1 || org_height <= 1) return;

            Parallel.For(0, org_height, y =>
            {
                uint* srcRow = src_fast + y * org_width;
                uint* dstRow0 = buffer_2x + (y * 2) * org_width * 2;
                uint* dstRow1 = dstRow0 + org_width * 2;
                int limit = org_width - 1;

                if (y == 0 || y == org_height - 1)
                {
                    // 邊界慢速路徑
                    for (int x = 0; x < org_width; x++)
                    {
                        uint E = srcRow[x];
                        uint D = (x == 0) ? E : srcRow[x - 1];
                        uint F = (x == limit) ? E : srcRow[x + 1];
                        uint B = (y == 0) ? E : src_fast[(y - 1) * org_width + x];
                        uint H = (y == org_height - 1) ? E : src_fast[(y + 1) * org_width + x];

                        // ★ 終極無分支布林求值 (Branchless CMOV)
                        bool diff = (B != H) & (D != F);
                        uint E0 = (diff & (D == B)) ? D : E;
                        uint E1 = (diff & (B == F)) ? F : E;
                        uint E2 = (diff & (D == H)) ? D : E;
                        uint E3 = (diff & (H == F)) ? F : E;

                        int dstX = x << 1;
                        *(ulong*)(dstRow0 + dstX) = E0 | ((ulong)E1 << 32);
                        *(ulong*)(dstRow1 + dstX) = E2 | ((ulong)E3 << 32);
                    }
                }
                else
                {
                    // 內部極速核心區
                    uint* pUP = srcRow - org_width;
                    uint* pDN = srcRow + org_width;

                    // (1) 空間剝離：左邊界 (x = 0)
                    {
                        uint E = srcRow[0]; uint D = E; uint F = srcRow[1]; uint B = pUP[0]; uint H = pDN[0];

                        bool diff = (B != H) & (D != F);
                        uint E0 = (diff & (D == B)) ? D : E;
                        uint E1 = (diff & (B == F)) ? F : E;
                        uint E2 = (diff & (D == H)) ? D : E;
                        uint E3 = (diff & (H == F)) ? F : E;

                        *(ulong*)(dstRow0) = E0 | ((ulong)E1 << 32);
                        *(ulong*)(dstRow1) = E2 | ((ulong)E3 << 32);
                    }

                    // (2) 核心 X 軸 (極限狂飆區)
                    for (int x = 1; x < limit; x++)
                    {
                        uint E = srcRow[x];
                        uint D = srcRow[x - 1];
                        uint F = srcRow[x + 1];
                        uint B = pUP[x];
                        uint H = pDN[x];

                        // ★ 終極無分支布林求值 (完全沒有 if)
                        bool diff = (B != H) & (D != F);
                        uint E0 = (diff & (D == B)) ? D : E;
                        uint E1 = (diff & (B == F)) ? F : E;
                        uint E2 = (diff & (D == H)) ? D : E;
                        uint E3 = (diff & (H == F)) ? F : E;

                        // ★ 64-bit 記憶體打包寫入
                        int dstX = x << 1;
                        *(ulong*)(dstRow0 + dstX) = E0 | ((ulong)E1 << 32);
                        *(ulong*)(dstRow1 + dstX) = E2 | ((ulong)E3 << 32);
                    }

                    // (3) 空間剝離：右邊界 (x = W-1)
                    {
                        int x = limit;
                        uint E = srcRow[x]; uint D = srcRow[x - 1]; uint F = E; uint B = pUP[x]; uint H = pDN[x];

                        bool diff = (B != H) & (D != F);
                        uint E0 = (diff & (D == B)) ? D : E;
                        uint E1 = (diff & (B == F)) ? F : E;
                        uint E2 = (diff & (D == H)) ? D : E;
                        uint E3 = (diff & (H == F)) ? F : E;

                        int dstX = x << 1;
                        *(ulong*)(dstRow0 + dstX) = E0 | ((ulong)E1 << 32);
                        *(ulong*)(dstRow1 + dstX) = E2 | ((ulong)E3 << 32);
                    }
                }
            });
        }

        public static void toScale3x_dx(uint* src_fast, int org_width, int org_height, uint* buffer_3x)
        {
            if (org_width <= 1 || org_height <= 1) return;

            Parallel.For(0, org_height, y =>
            {
                uint* srcRow = src_fast + y * org_width;
                uint* dstRow0 = buffer_3x + (y * 3) * org_width * 3;
                uint* dstRow1 = dstRow0 + org_width * 3;
                uint* dstRow2 = dstRow1 + org_width * 3;
                int limit = org_width - 1;

                if (y == 0 || y == org_height - 1)
                {
                    for (int x = 0; x < org_width; x++)
                    {
                        uint E = srcRow[x];
                        uint D = (x == 0) ? E : srcRow[x - 1];
                        uint F = (x == limit) ? E : srcRow[x + 1];

                        uint B = (y == 0) ? E : src_fast[(y - 1) * org_width + x];
                        uint H = (y == org_height - 1) ? E : src_fast[(y + 1) * org_width + x];

                        uint A = (y == 0 || x == 0) ? E : src_fast[(y - 1) * org_width + (x - 1)];
                        uint C = (y == 0 || x == limit) ? E : src_fast[(y - 1) * org_width + (x + 1)];
                        uint G = (y == org_height - 1 || x == 0) ? E : src_fast[(y + 1) * org_width + (x - 1)];
                        uint I = (y == org_height - 1 || x == limit) ? E : src_fast[(y + 1) * org_width + (x + 1)];

                        // ★ 終極無分支 Scale3x 邏輯
                        bool diff = (B != H) & (D != F);
                        uint E0 = (diff & (D == B)) ? D : E;
                        uint E1 = (diff & ((D == B & E != C) | (B == F & E != A))) ? B : E;
                        uint E2 = (diff & (B == F)) ? F : E;
                        uint E3 = (diff & ((D == B & E != G) | (D == H & E != A))) ? D : E;
                        uint E4 = E;
                        uint E5 = (diff & ((B == F & E != I) | (H == F & E != C))) ? F : E;
                        uint E6 = (diff & (D == H)) ? D : E;
                        uint E7 = (diff & ((D == H & E != I) | (H == F & E != G))) ? H : E;
                        uint E8 = (diff & (H == F)) ? F : E;

                        int dstX = x * 3;
                        *(ulong*)(dstRow0 + dstX) = E0 | ((ulong)E1 << 32); dstRow0[dstX + 2] = E2;
                        *(ulong*)(dstRow1 + dstX) = E3 | ((ulong)E4 << 32); dstRow1[dstX + 2] = E5;
                        *(ulong*)(dstRow2 + dstX) = E6 | ((ulong)E7 << 32); dstRow2[dstX + 2] = E8;
                    }
                }
                else
                {
                    uint* pUP = srcRow - org_width;
                    uint* pDN = srcRow + org_width;

                    // (1) 空間剝離：左邊界
                    {
                        uint E = srcRow[0]; uint D = E; uint F = srcRow[1]; uint B = pUP[0]; uint H = pDN[0];
                        uint A = E; uint C = pUP[1]; uint G = E; uint I = pDN[1];

                        bool diff = (B != H) & (D != F);
                        uint E0 = (diff & (D == B)) ? D : E;
                        uint E1 = (diff & ((D == B & E != C) | (B == F & E != A))) ? B : E;
                        uint E2 = (diff & (B == F)) ? F : E;
                        uint E3 = (diff & ((D == B & E != G) | (D == H & E != A))) ? D : E;
                        uint E4 = E;
                        uint E5 = (diff & ((B == F & E != I) | (H == F & E != C))) ? F : E;
                        uint E6 = (diff & (D == H)) ? D : E;
                        uint E7 = (diff & ((D == H & E != I) | (H == F & E != G))) ? H : E;
                        uint E8 = (diff & (H == F)) ? F : E;

                        *(ulong*)(dstRow0) = E0 | ((ulong)E1 << 32); dstRow0[2] = E2;
                        *(ulong*)(dstRow1) = E3 | ((ulong)E4 << 32); dstRow1[2] = E5;
                        *(ulong*)(dstRow2) = E6 | ((ulong)E7 << 32); dstRow2[2] = E8;
                    }

                    // (2) 核心 X 軸 (極限狂飆區)
                    for (int x = 1; x < limit; x++)
                    {
                        uint E = srcRow[x];
                        uint D = srcRow[x - 1];
                        uint F = srcRow[x + 1];
                        uint B = pUP[x];
                        uint H = pDN[x];
                        uint A = pUP[x - 1];
                        uint C = pUP[x + 1];
                        uint G = pDN[x - 1];
                        uint I = pDN[x + 1];

                        // ★ 完全拋棄 if，擁抱 CMOV
                        bool diff = (B != H) & (D != F);
                        uint E0 = (diff & (D == B)) ? D : E;
                        uint E1 = (diff & ((D == B & E != C) | (B == F & E != A))) ? B : E;
                        uint E2 = (diff & (B == F)) ? F : E;
                        uint E3 = (diff & ((D == B & E != G) | (D == H & E != A))) ? D : E;
                        uint E4 = E;
                        uint E5 = (diff & ((B == F & E != I) | (H == F & E != C))) ? F : E;
                        uint E6 = (diff & (D == H)) ? D : E;
                        uint E7 = (diff & ((D == H & E != I) | (H == F & E != G))) ? H : E;
                        uint E8 = (diff & (H == F)) ? F : E;

                        int dstX = x * 3;
                        // ★ 64-bit 打包寫入
                        *(ulong*)(dstRow0 + dstX) = E0 | ((ulong)E1 << 32); dstRow0[dstX + 2] = E2;
                        *(ulong*)(dstRow1 + dstX) = E3 | ((ulong)E4 << 32); dstRow1[dstX + 2] = E5;
                        *(ulong*)(dstRow2 + dstX) = E6 | ((ulong)E7 << 32); dstRow2[dstX + 2] = E8;
                    }

                    // (3) 空間剝離：右邊界
                    {
                        int x = limit;
                        uint E = srcRow[x]; uint D = srcRow[x - 1]; uint F = E; uint B = pUP[x]; uint H = pDN[x];
                        uint A = pUP[x - 1]; uint C = E; uint G = pDN[x - 1]; uint I = E;

                        bool diff = (B != H) & (D != F);
                        uint E0 = (diff & (D == B)) ? D : E;
                        uint E1 = (diff & ((D == B & E != C) | (B == F & E != A))) ? B : E;
                        uint E2 = (diff & (B == F)) ? F : E;
                        uint E3 = (diff & ((D == B & E != G) | (D == H & E != A))) ? D : E;
                        uint E4 = E;
                        uint E5 = (diff & ((B == F & E != I) | (H == F & E != C))) ? F : E;
                        uint E6 = (diff & (D == H)) ? D : E;
                        uint E7 = (diff & ((D == H & E != I) | (H == F & E != G))) ? H : E;
                        uint E8 = (diff & (H == F)) ? F : E;

                        int dstX = x * 3;
                        *(ulong*)(dstRow0 + dstX) = E0 | ((ulong)E1 << 32); dstRow0[dstX + 2] = E2;
                        *(ulong*)(dstRow1 + dstX) = E3 | ((ulong)E4 << 32); dstRow1[dstX + 2] = E5;
                        *(ulong*)(dstRow2 + dstX) = E6 | ((ulong)E7 << 32); dstRow2[dstX + 2] = E8;
                    }
                }
            });
        }
    }
}