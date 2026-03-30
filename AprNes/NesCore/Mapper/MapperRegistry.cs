namespace AprNes
{
    static class MapperRegistry
    {
        public static bool IsSupported(int id)
        {
            switch (id)
            {
                case  0: case  1: case  2: case  3: case  4: case  5:
                case  7: case  9: case 10: case 11: case 13: case 16: case 18: case 19: case 21: case 22: case 23: case 24: case 26: case 29:
                case 159: case 153: case 85:
                case 25: case 67: case 72: case 76: case 77: case 79: case 80: case 82: case 87: case 89: case 93: case 95: case 97: case 184: case 185:
                case 32: case 33: case 34: case 64: case 65: case 66: case 68: case 69: case 71: case 78: case 206:
                case 70: case 75: case 88: case 90: case 118: case 119: case 140: case 152: case 180: case 209: case 210: case 211: case 228: case 232:
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
                case 16:  return "Bandai FCG";
                case 19:  return "Namco 163";
                case 24:  return "VRC6a";
                case 26:  return "VRC6b";
                case 29:  return "Sealie Computing";
                case 85:  return "VRC7";
                case 153: return "Bandai LZ93D50+WRAM";
                case 159: return "Bandai LZ93D50+24C01";
                case 18: return "Jaleco SS8806";
                case 21: return "VRC4";
                case 22: return "VRC2a";
                case 23: return "VRC2b";
                case 32: return "Irem G-101";
                case 33: return "Taito TC0190";
                case 34: return "Nina-1";
                case 64: return "Tengen RAMBO-1";
                case 65: return "Irem H-3001";
                case 66: return "GxROM";
                case 68: return "Sunsoft #4";
                case 69: return "FME-7";
                case 71: return "Camerica";
                case 78: return "Irem 74HC161/32";
                case  13: return "CPROM";
                case  67: return "Sunsoft-3";
                case  76: return "Namco 109";
                case  77: return "Napoleon/IremLrog017";
                case  80: return "Taito X1-005";
                case  82: return "Taito X1-017";
                case  95: return "Namco 118 DxROM";
                case  97: return "Irem TAM-S1";
                case 119: return "TQROM";
                case 180: return "Crazy Climber";
                case 210: return "Namco 175/340";
                case 228: return "Action 52";
                case 206: return "Namco 108";
                case  25: return "VRC4b/d";
                case  72: return "Jaleco JF-17";
                case  79: return "NINA-03/06";
                case  87: return "Jaleco JF-09/10/18";
                case  89: return "Sunsoft-2 (Ikki)";
                case  90: return "JY Company";
                case 209: return "JY Company (209)";
                case 211: return "JY Company (211)";
                case  93: return "Sunsoft-2 (Fantasy Zone II)";
                case 184: return "Sunsoft-1";
                case 185: return "CNROM+protection";
                case  70: return "Bandai 74161/32";
                case  75: return "Konami VRC1";
                case  88: return "Namco 118";
                case 118: return "TxSROM";
                case 140: return "Jaleco JF-11/14";
                case 152: return "Bandai 74161/32+mirror";
                case 232: return "Camerica Quattro";
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
                case 16: {
                    var m = new Mapper016();
                    m.Submapper = db.Submapper;
                    if (db.Submapper == 4) System.Console.WriteLine("Sub-variant: Mapper016 FCG-1/2 ($6000)");
                    else if (db.Submapper == 5) System.Console.WriteLine("Sub-variant: Mapper016 LZ93D50 ($8000)");
                    else System.Console.WriteLine("Sub-variant: Mapper016 heuristic");
                    return m;
                }
                case 19: {
                    System.Console.WriteLine("Mapper019: Namco 163");
                    return new Mapper019();
                }
                case 24: {
                    var m = new Mapper024();
                    m.IsVRC6b = false;
                    System.Console.WriteLine("Mapper024: VRC6a");
                    return m;
                }
                case 26: {
                    var m = new Mapper024();
                    m.IsVRC6b = true;
                    System.Console.WriteLine("Mapper026: VRC6b");
                    return m;
                }
                case 29: return new Mapper029();
                case 85: {
                    System.Console.WriteLine("Mapper085: VRC7 (OPLL FM synthesis)");
                    return new Mapper085();
                }
                case 153: {
                    System.Console.WriteLine("Mapper153: Bandai LZ93D50+WRAM");
                    return new Mapper153();
                }
                case 159: {
                    // LZ93D50 + 24C01 (128-byte EEPROM) — same logic as mapper 16 sub 5
                    var m = new Mapper016();
                    m.Submapper = 5;
                    System.Console.WriteLine("Mapper159: LZ93D50+24C01 (sub5 behaviour)");
                    return m;
                }
                case 18: return new Mapper018();
                case 21: {
                    var m = new Mapper021();
                    m.Submapper = db.Submapper;
                    if (db.Submapper == 1) System.Console.WriteLine("Sub-variant: VRC4a");
                    else if (db.Submapper == 2) System.Console.WriteLine("Sub-variant: VRC4c");
                    else System.Console.WriteLine("Sub-variant: VRC4 heuristic");
                    return m;
                }
                case 22: return new Mapper022();
                case 23: return new Mapper023();
                case 32: {
                    var m = new Mapper032();
                    if (db.Submapper == 1) { System.Console.WriteLine("Sub-variant: Mapper032 Major League"); m.majorLeague = true; }
                    return m;
                }
                case 33: return new Mapper033();
                case 34: return new Mapper034();
                case 64: return new Mapper064();
                case 65: return new Mapper065();
                case 66: return new Mapper066();
                case 68: return new Mapper068();
                case 69: return new Mapper069();
                case 71: {
                    var m = new Mapper071();
                    m.Submapper = db.Submapper;
                    return m;
                }
                case 78: {
                    var m = new Mapper078();
                    if (db.Submapper == 3)
                    {
                        System.Console.WriteLine("Sub-variant: Mapper078 Holy Diver");
                        m.isHolyDiver = true;
                    }
                    return m;
                }
                case  13: {
                    System.Console.WriteLine("Mapper013: CPROM");
                    return new Mapper013();
                }
                case  67: {
                    System.Console.WriteLine("Mapper067: Sunsoft-3");
                    return new Mapper067();
                }
                case  76: {
                    System.Console.WriteLine("Mapper076: Namco 109");
                    return new Mapper076();
                }
                case  77: {
                    System.Console.WriteLine("Mapper077: Napoleon/IremLrog017");
                    return new Mapper077();
                }
                case  80: {
                    System.Console.WriteLine("Mapper080: Taito X1-005");
                    return new Mapper080();
                }
                case  82: {
                    System.Console.WriteLine("Mapper082: Taito X1-017");
                    return new Mapper082();
                }
                case  95: {
                    System.Console.WriteLine("Mapper095: Namco 118 DxROM");
                    return new Mapper095();
                }
                case  97: {
                    System.Console.WriteLine("Mapper097: Irem TAM-S1");
                    return new Mapper097();
                }
                case 119: {
                    System.Console.WriteLine("Mapper119: TQROM");
                    return new Mapper119();
                }
                case 180: {
                    System.Console.WriteLine("Mapper180: Crazy Climber");
                    return new Mapper180();
                }
                case 210: {
                    var m = new Mapper210();
                    m.Submapper = db.Submapper;
                    if (db.Submapper == 1) System.Console.WriteLine("Mapper210: Namco175");
                    else if (db.Submapper == 2) System.Console.WriteLine("Mapper210: Namco340");
                    else System.Console.WriteLine("Mapper210: Namco175/340 (heuristic)");
                    return m;
                }
                case 228: {
                    System.Console.WriteLine("Mapper228: Action 52");
                    return new Mapper228();
                }
                case 206: return new Mapper206();
                case 25: {
                    var m = new Mapper025();
                    m.Submapper = db.Submapper;
                    if (db.Submapper == 1) System.Console.WriteLine("Sub-variant: VRC4b");
                    else if (db.Submapper == 2) System.Console.WriteLine("Sub-variant: VRC4d");
                    else System.Console.WriteLine("Sub-variant: VRC4b/d heuristic");
                    return m;
                }
                case 72: return new Mapper072();
                case 79: return new Mapper079();
                case 87: return new Mapper087();
                case 89: return new Mapper089();
                case 90: {
                    System.Console.WriteLine("Mapper090: JY Company");
                    return new Mapper090();
                }
                case 209: {
                    var m = new Mapper090();
                    m.MapperVariant = 209;
                    System.Console.WriteLine("Mapper209: JY Company (CHR latch)");
                    return m;
                }
                case 211: {
                    var m = new Mapper090();
                    m.MapperVariant = 211;
                    System.Console.WriteLine("Mapper211: JY Company (extended NT)");
                    return m;
                }
                case 93: return new Mapper093();
                case 184: return new Mapper184();
                case 185: return new Mapper185();
                case 70: {
                    System.Console.WriteLine("Mapper070: Bandai 74161/32 (fixed mirroring)");
                    return new Mapper070();
                }
                case 75: {
                    System.Console.WriteLine("Mapper075: Konami VRC1");
                    return new Mapper075();
                }
                case 88: {
                    System.Console.WriteLine("Mapper088: Namco 118");
                    return new Mapper088();
                }
                case 118: {
                    System.Console.WriteLine("Mapper118: TxSROM (MMC3+NT control)");
                    return new Mapper118();
                }
                case 140: {
                    System.Console.WriteLine("Mapper140: Jaleco JF-11/14");
                    return new Mapper140();
                }
                case 152: {
                    System.Console.WriteLine("Mapper152: Bandai 74161/32 (mirroring control)");
                    return new Mapper152();
                }
                case 232: {
                    var m = new Mapper232();
                    if (db.Submapper == 1)
                    {
                        System.Console.WriteLine("Mapper232: Camerica Quattro (Aladdin variant)");
                        m.IsAladdinVariant = true;
                    }
                    else System.Console.WriteLine("Mapper232: Camerica Quattro");
                    return m;
                }
                default: throw new System.NotSupportedException("Mapper " + id + " not supported");
            }
        }
    }
}
