using System;
using System.Drawing;
using WINAPIGDI;
using System.Drawing.Imaging;

namespace AprNes
{
    unsafe interface InterfaceGraphic
    {
        void init(uint* input, Graphics _device);
        void Render();
        void freeMem();
        Bitmap GetOutput();
    }

    // 類比訊號模擬輸出渲染器
    // 直接從 NesCore.AnalogScreenBuf（768×630）讀取，無縮放
    // CrtScreen Stage 2 在 PPU RenderScreen 時已完成寫入
    unsafe public class Render_Analog : InterfaceGraphic
    {
        public void freeMem() { }  // 緩衝區屬於 NesCore，不在此釋放

        public Bitmap GetOutput()
        {
            int dw = NesCore.Crt_DstW, dh = NesCore.Crt_DstH;
            return new Bitmap(dw, dh, dw * 4, PixelFormat.Format32bppRgb, (IntPtr)NesCore.AnalogScreenBuf);
        }

        public void init(uint* input, Graphics _device)
        {
            // input (ScreenBuf1x) 不使用；直接指向 AnalogScreenBuf
            NativeGDI.initHighSpeed(_device, NesCore.Crt_DstW, NesCore.Crt_DstH, NesCore.AnalogScreenBuf, 0, 0);
            NesCore.RenderOutputPtr = NesCore.AnalogScreenBuf;
            NesCore.RenderOutputW = NesCore.Crt_DstW;
            NesCore.RenderOutputH = NesCore.Crt_DstH;
        }

        public void Render()
        {
            NativeGDI.DrawImageHighSpeedtoDevice();
        }
    }
}
