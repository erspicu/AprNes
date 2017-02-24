using System;
using System.Drawing;
using WINAPIGDI;
using XBRz_speed;
using ScalexFilter;
using ScanLineBuilder;
using System.Runtime.InteropServices;
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

    unsafe public class Render_xbrz_1x : InterfaceGraphic
    {
        uint* _input;
        public void freeMem()
        {
        }

        public Bitmap GetOutput()
        {
            return new Bitmap(256 * 1, 240 * 1, 256 * 1 * 4, PixelFormat.Format32bppRgb, (IntPtr)_input);
        }

        public void init(uint* input, Graphics _device)
        {
            _input = input;
            NativeGDI.initHighSpeed(_device, 256 * 1, 240 * 1, _input, 0, 0);
            HS_XBRz.initTable(256, 240);
        }

        public void Render()
        {
            NativeGDI.DrawImageHighSpeedtoDevice();
        }
    }

    unsafe public class Render_xbrz_2x : InterfaceGraphic
    {
        uint* _input;
        uint* _output;
        public void freeMem()
        {
            Marshal.FreeHGlobal((IntPtr)_output);
        }

        public Bitmap GetOutput()
        {
            return new Bitmap(256 * 2, 240 * 2, 256 * 2 * 4, PixelFormat.Format32bppRgb, (IntPtr)_output);
        }

        public void init(uint* input, Graphics _device)
        {
            _input = input;
            _output = (uint*)Marshal.AllocHGlobal(sizeof(uint) * 245760);
            NativeGDI.initHighSpeed(_device, 256 * 2, 240 * 2, _output, 0, 0);
            HS_XBRz.initTable(256, 240);
        }

        public void Render()
        {
            HS_XBRz.ScaleImage2X(_input, _output);
            NativeGDI.DrawImageHighSpeedtoDevice();
        }
    }

    unsafe public class Render_xbrz_3x : InterfaceGraphic
    {

        uint* _input;
        uint* _output;
        public void freeMem()
        {
            Marshal.FreeHGlobal((IntPtr)_output);
        }

        public Bitmap GetOutput()
        {
            return new Bitmap(256 * 3, 240 * 3, 256 * 3 * 4, PixelFormat.Format32bppRgb, (IntPtr)_output);
        }

        public void init(uint* input, Graphics _device)
        {
            _input = input;
            _output = (uint*)Marshal.AllocHGlobal(sizeof(uint) * 256 * 3 * 240 * 3);
            NativeGDI.initHighSpeed(_device, 256 * 3, 240 * 3, _output, 0, 0);
            HS_XBRz.initTable(256, 240);
        }

        public void Render()
        {
            HS_XBRz.ScaleImage3X(_input, _output);
            NativeGDI.DrawImageHighSpeedtoDevice();
        }
    }

    unsafe public class Render_xbrz_4x : InterfaceGraphic
    {
        uint* _input;
        uint* _output;
        public void freeMem()
        {
            Marshal.FreeHGlobal((IntPtr)_output);
        }

        public Bitmap GetOutput()
        {
            return new Bitmap(256 * 4, 240 * 4, 256 * 4 * 4, PixelFormat.Format32bppRgb, (IntPtr)_output);
        }

        public void init(uint* input, Graphics _device)
        {
            _input = input;
            _output = (uint*)Marshal.AllocHGlobal(sizeof(uint) * 256 * 4 * 240 * 4);
            NativeGDI.initHighSpeed(_device, 256 * 4, 240 * 4, _output, 0, 0);
            HS_XBRz.initTable(256, 240);
        }

        public void Render()
        {
            HS_XBRz.ScaleImage4X(_input, _output);
            NativeGDI.DrawImageHighSpeedtoDevice();
        }
    }

    unsafe public class Render_xbrz_5x : InterfaceGraphic
    {
        uint* _input;
        uint* _output;
        public void freeMem()
        {
            Marshal.FreeHGlobal((IntPtr)_output);
        }

        public Bitmap GetOutput()
        {
            return new Bitmap(256 * 5, 240 * 5, 256 * 5 * 4, PixelFormat.Format32bppRgb, (IntPtr)_output);
        }

        public void init(uint* input, Graphics _device)
        {
            _input = input;
            _output = (uint*)Marshal.AllocHGlobal(sizeof(uint) * 256 * 5 * 240 * 5);
            NativeGDI.initHighSpeed(_device, 256 * 5, 240 * 5, _output, 0, 0);
            HS_XBRz.initTable(256, 240);
        }

        public void Render()
        {
            HS_XBRz.ScaleImage5X(_input, _output);
            NativeGDI.DrawImageHighSpeedtoDevice();
        }
    }

    unsafe public class Render_xbrz_6x : InterfaceGraphic
    {
        uint* _input;
        uint* _output;
        public void freeMem()
        {
            Marshal.FreeHGlobal((IntPtr)_output);
        }

        public Bitmap GetOutput()
        {
            return new Bitmap(256 * 6, 240 * 6, 256 * 6 * 4, PixelFormat.Format32bppRgb, (IntPtr)_output);
        }

        public void init(uint* input, Graphics _device)
        {
            _input = input;
            _output = (uint*)Marshal.AllocHGlobal(sizeof(uint) * 256 * 6 * 240 * 6);
            NativeGDI.initHighSpeed(_device, 256 * 6, 240 * 6, _output, 0, 0);
            HS_XBRz.initTable(256, 240);
        }

        public void Render()
        {
            HS_XBRz.ScaleImage6X(_input, _output);
            NativeGDI.DrawImageHighSpeedtoDevice();
        }
    }

    unsafe public class Render_xbrz_8x : InterfaceGraphic
    {
        uint* _input;
        uint* _output;
        uint* _output_tmp;
        public void freeMem()
        {
            Marshal.FreeHGlobal((IntPtr)_output);
            Marshal.FreeHGlobal((IntPtr)_output_tmp);
        }

        public Bitmap GetOutput()
        {
            return new Bitmap(256 * 8, 240 * 8, 256 * 8 * 4, PixelFormat.Format32bppRgb, (IntPtr)_output);
        }

        public void init(uint* input, Graphics _device)
        {
            _input = input;
            _output_tmp = (uint*)Marshal.AllocHGlobal(sizeof(uint) * 256 * 4 * 240 * 4);
            _output = (uint*)Marshal.AllocHGlobal(sizeof(uint) * 256 * 8 * 240 * 8);
            NativeGDI.initHighSpeed(_device, 256 * 8, 240 * 8, _output, 0, 0);
            HS_XBRz.initTable(256, 240);
        }

        public void Render()
        {
            HS_XBRz.ScaleImage4X(_input, _output_tmp);
            ScalexTool.toScale2x_dx(_output_tmp, 1024, 960, _output);
            NativeGDI.DrawImageHighSpeedtoDevice();
        }
    }

    unsafe public class Render_xbrz_9x : InterfaceGraphic
    {
        uint* _input;
        uint* _output;
        uint* _output_tmp;
        public void freeMem()
        {
            Marshal.FreeHGlobal((IntPtr)_output);
            Marshal.FreeHGlobal((IntPtr)_output_tmp);
        }

        public Bitmap GetOutput()
        {
            return new Bitmap(256 * 9, 240 * 9, 256 * 9 * 4, PixelFormat.Format32bppRgb, (IntPtr)_output);
        }

        public void init(uint* input, Graphics _device)
        {
            _input = input;
            _output_tmp = (uint*)Marshal.AllocHGlobal(sizeof(uint) * 256 * 3 * 240 * 3);
            _output = (uint*)Marshal.AllocHGlobal(sizeof(uint) * 256 * 9 * 240 * 9);
            NativeGDI.initHighSpeed(_device, 256 * 9, 240 * 9, _output, 0, 0);
            HS_XBRz.initTable(256, 240);
        }

        public void Render()
        {
            HS_XBRz.ScaleImage3X(_input, _output_tmp);
            ScalexTool.toScale3x_dx(_output_tmp, 768, 720, _output);
            NativeGDI.DrawImageHighSpeedtoDevice();
        }
    }

    unsafe public class Render_scanline_2x : InterfaceGraphic
    {
        uint* _input;
        uint* _output;
        public void freeMem()
        {
            Marshal.FreeHGlobal((IntPtr)_output);
        }

        public Bitmap GetOutput()
        {
            return new Bitmap(600, 480, 600 * 4, PixelFormat.Format32bppRgb, (IntPtr)_output);
        }

        public void init(uint* input, Graphics _device)
        {
            _input = input;
            _output = (uint*)Marshal.AllocHGlobal(sizeof(uint) * 600 * 480);
            NativeGDI.initHighSpeed(_device, 600, 480, _output, 0, 0);
            LibScanline.init(_input, _output);
        }

        public void Render()
        {
            LibScanline.ScanlineFor1x();
            NativeGDI.DrawImageHighSpeedtoDevice();
        }
    }

    unsafe public class Render_scanline_4x : InterfaceGraphic
    {
        uint* _input;
        uint* _output;
        uint* _output_tmp;
        public void freeMem()
        {
            Marshal.FreeHGlobal((IntPtr)_output);
            Marshal.FreeHGlobal((IntPtr)_output_tmp);
        }

        public Bitmap GetOutput()
        {
            return new Bitmap(1196, 960, 1196 * 4, PixelFormat.Format32bppRgb, (IntPtr)_output);
        }

        public void init(uint* input, Graphics _device)
        {
            _input = input;
            _output_tmp = (uint*)Marshal.AllocHGlobal(sizeof(uint) * 256 * 2 * 240 * 2);
            _output = (uint*)Marshal.AllocHGlobal(sizeof(uint) * 1196 * 960);
            NativeGDI.initHighSpeed(_device, 1196, 960, _output, 0, 0);
            HS_XBRz.initTable(256, 240);
            LibScanline.init(_output_tmp, _output);
        }

        public void Render()
        {
            HS_XBRz.ScaleImage2X(_input, _output_tmp);
            LibScanline.ScanlineFor2x();
            NativeGDI.DrawImageHighSpeedtoDevice();
        }
    }

    unsafe public class Render_scanline_6x : InterfaceGraphic
    {
        uint* _input;
        uint* _output;
        uint* _output_tmp;
        public void freeMem()
        {
            Marshal.FreeHGlobal((IntPtr)_output);
            Marshal.FreeHGlobal((IntPtr)_output_tmp);
        }

        public Bitmap GetOutput()
        {
            return new Bitmap(1792, 1440, 1792 * 4, PixelFormat.Format32bppRgb, (IntPtr)_output);
        }

        public void init(uint* input, Graphics _device)
        {
            _input = input;
            _output_tmp = (uint*)Marshal.AllocHGlobal(sizeof(uint) * 256 * 3 * 240 * 3);
            _output = (uint*)Marshal.AllocHGlobal(sizeof(uint) * 1792 * 1440);
            NativeGDI.initHighSpeed(_device, 1792, 1440, _output, 0, 0);
            HS_XBRz.initTable(256, 240);
            LibScanline.init(_output_tmp, _output);
        }

        public void Render()
        {
            HS_XBRz.ScaleImage3X(_input, _output_tmp);
            LibScanline.ScanlineFor3x();
            NativeGDI.DrawImageHighSpeedtoDevice();
        }
    }
}
