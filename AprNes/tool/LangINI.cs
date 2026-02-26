using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;

namespace LangTool
{
    public class LangINI
    {
        static string LangFile = System.IO.Path.Combine(AppContext.BaseDirectory, "AprNesLang.ini");
        static List<string> lines;
        static List<string> langs = new List<string>();
        public static Dictionary<string, string> lang_map = new Dictionary<string, string>();
        public static Dictionary<string, Dictionary<string, string>> lang_table = new Dictionary<string, Dictionary<string, string>>();
        public static bool LangLoadOK = false;
        public static bool LangFileMissing = false;

        /// <summary>
        /// 安全取值：找不到語系、語系 key 不存在時回傳 fallback（預設空字串）
        /// </summary>
        public static string Get(string lang, string key, string fallback = "")
        {
            if (!LangLoadOK) return fallback;
            if (!lang_table.ContainsKey(lang)) return fallback;
            string val;
            return lang_table[lang].TryGetValue(key, out val) ? val : fallback;
        }

        public static void init()
        {
            if (LangLoadOK) return;
            if (!File.Exists(LangFile)) { LangFileMissing = true; return; }

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
