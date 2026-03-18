namespace AprNes
{
    static class MapperRegistry
    {
        public static bool IsSupported(int id)
        {
            switch (id)
            {
                case  0: case  1: case  2: case  3: case  4: case  5:
                case  7: case  9: case 11: case 66: case 71:
                    return true;
                default:
                    return false;
            }
        }

        public static string GetName(int id)
        {
            switch (id)
            {
                case  0: return "NROM";
                case  1: return "MMC1";
                case  2: return "UNROM";
                case  3: return "CNROM";
                case  4: return "MMC3";
                case  5: return "MMC5";
                case  7: return "AxROM";
                case  9: return "MMC2";
                case 11: return "Color Dreams";
                case 66: return "GxROM";
                case 71: return "Camerica";
                default: return "Unknown";
            }
        }

        // romCrc: CRC32 of the full ROM bytes; used for MMC3 sub-variant detection.
        // Pass 0 for non-mapper-4 ROMs (ignored).
        public static IMapper Create(int id, uint romCrc = 0)
        {
            if (id == 4)
            {
                if (romCrc == 0x1D814D25 || romCrc == 0x59322B74)
                {
                    System.Console.WriteLine("Sub-variant: MMC3 Rev A");
                    return new Mapper004RevA();
                }
                if (romCrc == 0x9F1A68ED)
                {
                    System.Console.WriteLine("Sub-variant: MMC6");
                    return new Mapper004MMC6();
                }
                return new Mapper004();
            }
            switch (id)
            {
                case  0: return new Mapper000();
                case  1: return new Mapper001();
                case  2: return new Mapper002();
                case  3: return new Mapper003();
                case  5: return new Mapper005();
                case  7: return new Mapper007();
                case  9: return new Mapper009();
                case 11: return new Mapper011();
                case 66: return new Mapper066();
                case 71: return new Mapper071();
                default: throw new System.NotSupportedException("Mapper " + id + " not supported");
            }
        }
    }
}
