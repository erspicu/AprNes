using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Windows.Forms;

namespace LangTool
{
    public class LangINI
    {
        static string LangFile = Application.StartupPath + "/AprNesLang.ini";
        static List<string> lines;
        static List<string> langs = new List<string>();
        public static Dictionary<string, string> lang_map = new Dictionary<string, string>();
        public static Dictionary<string, Dictionary<string, string>> lang_table = new Dictionary<string, Dictionary<string, string>>();
        public static bool LangLoadOK = false;

        public static void init()
        {
            if (LangLoadOK) return;
            if (!File.Exists(LangFile)) return;

            try
            {
                lines = File.ReadAllLines(LangFile).ToList();
                Dictionary<string, string> items = new Dictionary<string, string>();
                string lang = "";

                bool start = false;
                foreach (string i in lines)
                {
                    string l = i.Replace("\r", "").Replace("\n", "");


                    if (start == true)
                    {
                        List<string> keyvalue = i.Split(new char[] { '=' }).ToList();
                        if (keyvalue.Count == 2)
                        {
                            lang_table[lang][keyvalue[0]] = keyvalue[1];
                            if (keyvalue[0] == "lang") lang_map.Add(lang, keyvalue[1]);
                        }
                        if (l.StartsWith("[") && l.EndsWith("]")) start = false;
                    }
                    if ((l.StartsWith("[") && l.EndsWith("]")) && start != true)
                    {
                        start = true;
                        lang = l.Replace("[", "").Replace("]", "");
                        lang_table.Add(lang, new Dictionary<string, string>());
                        langs.Add(lang);

                    }
                }
            }
            catch (Exception e) { Console.WriteLine(e.Message); }
            LangLoadOK = true;
        }
    }
}
