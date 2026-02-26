// 鍵盤多鍵同壓測試工具
// 即時顯示目前壓下的按鍵，用來驗證鍵盤 ghosting 情況
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

class KeyTest
{
    [DllImport("user32.dll")]
    static extern short GetAsyncKeyState(int vKey);

    // 要監控的按鍵：VK code → 顯示名稱
    static readonly Dictionary<int, string> WatchKeys = new Dictionary<int, string>
    {
        { 90, "Z(A)" },   // Z = NES A
        { 88, "X(B)" },   // X = NES B
        { 37, "←" },      // Left
        { 39, "→" },      // Right
        { 38, "↑" },      // Up
        { 40, "↓" },      // Down
        { 75, "K" },      // K 候選A
        { 74, "J" },      // J 候選A
        { 76, "L" },      // L 候選B
        { 72, "H" },      // H 候選B
    };

    static void Main()
    {
        Console.Clear();
        Console.WriteLine("=== 鍵盤多鍵同壓測試 ===");
        Console.WriteLine("同時按下各種組合，觀察哪些鍵無法同時偵測到");
        Console.WriteLine("按 ESC 離開\n");
        Console.WriteLine("監控按鍵：Z(A)  X(B)  ←  →  ↑  ↓  K  J  L  H\n");

        string lastLine = "";
        while (true)
        {
            if ((GetAsyncKeyState(27) & 0x8000) != 0) break; // ESC 離開

            var pressed = new List<string>();
            foreach (var kv in WatchKeys)
            {
                if ((GetAsyncKeyState(kv.Key) & 0x8000) != 0)
                    pressed.Add(kv.Value);
            }

            string line = pressed.Count == 0
                ? "（沒有按鍵）"
                : string.Join(" + ", pressed) + $"  [{pressed.Count} 鍵]";

            if (line != lastLine)
            {
                Console.SetCursorPosition(0, 5);
                Console.Write("現在按下：" + line.PadRight(50));
                Console.SetCursorPosition(0, 7);

                // 特別提示
                bool hasZ = pressed.Contains("Z(A)");
                bool hasX = pressed.Contains("X(B)");
                bool hasRight = pressed.Contains("→");
                bool hasLeft = pressed.Contains("←");
                bool hasK = pressed.Contains("K");
                bool hasJ = pressed.Contains("J");
                bool hasL = pressed.Contains("L");

                if (hasZ && hasX && hasRight)
                    Console.Write("⚠  Z+X+→ 成功！此鍵盤支援這組合".PadRight(60));
                else if (hasZ && hasX && hasLeft)
                    Console.Write("✓  Z+X+← 正常（這組通常沒問題）  ".PadRight(60));
                else if ((hasK || hasJ) && (hasL) && (hasLeft || hasRight))
                    Console.Write("✓  候選鍵組合正常！可考慮改成這組".PadRight(60));
                else
                    Console.Write("".PadRight(60));

                lastLine = line;
            }
            Thread.Sleep(16); // ~60fps
        }
    }
}
