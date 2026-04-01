namespace AprNes
{
    public enum ResizeFilter
    {
        None,       // 1x pass-through
        NN,         // Nearest-Neighbor integer scaling
        XBRz,       // xBRZ pixel-art scaling (2x-6x)
        ScaleX      // Scale2x / Scale3x
    }
}
