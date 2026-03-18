namespace AprNes
{
    static class MapperRegistry
    {
        public static bool IsSupported(int id)
        {
            switch (id)
            {
                case  0: case  1: case  2: case  3: case  4: case  5:
                case  7: case  9: case 10: case 11: case 34: case 66:
                case 69: case 71: case 78: case 206:
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
                case 10: return "MMC4";
                case 11: return "Color Dreams";
                case 34: return "Nina-1";
                case 66: return "GxROM";
                case 69: return "FME-7";
                case 71: return "Camerica";
                case 78: return "Irem 74HC161/32";
                case 206: return "Namco 108";
                default: return "Unknown";
            }
        }

        public static IMapper Create(int id, RomDbEntry db)
        {
            if (id == 4)
            {
                if (db.Submapper == 1)
                {
                    System.Console.WriteLine("Sub-variant: MMC3 Rev A");
                    return new Mapper004RevA();
                }
                if (db.Submapper == 2)
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
                case 10: return new Mapper010();
                case 11: return new Mapper011();
                case 34: return new Mapper034();
                case 66: return new Mapper066();
                case 69: return new Mapper069();
                case 71: return new Mapper071();
                case 78: {
                    var m = new Mapper078();
                    if (db.Submapper == 3)
                    {
                        System.Console.WriteLine("Sub-variant: Mapper078 Holy Diver");
                        m.isHolyDiver = true;
                    }
                    return m;
                }
                case 206: return new Mapper206();
                default: throw new System.NotSupportedException("Mapper " + id + " not supported");
            }
        }
    }
}
