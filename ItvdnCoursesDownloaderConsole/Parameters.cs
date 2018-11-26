using System;
using System.Collections.Generic;
using System.Reflection;

namespace ItvdnCoursesDownloaderConsole
{
    internal class Parameters
    {
        public string Email { get; set; }
        public string Password { get; set; }
        public string SavePath { get; set; }

        public Parameters(string[] args)
        {
            Dictionary<string, string> argDictionary = ParseArgs(args ?? throw new ArgumentNullException(nameof(args)));

            foreach (PropertyInfo propertyInfo in GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                string setting = GetSetting(propertyInfo.Name, argDictionary);
                propertyInfo.SetValue(this, setting);
            }
        }

        private Dictionary<string, string> ParseArgs(string[] args)
        {
            if (args.Length == 0)
            {
                return null;
            }

            var argDictionary = new Dictionary<string, string>();

            const string argementStart = "-";
            const char separator = ':';

            foreach (string arg in args)
            {
                if (arg.StartsWith(argementStart))
                {
                    string parameterWithoutHyphen = arg.Substring(1);
                    int separatorIndex = parameterWithoutHyphen.IndexOf(separator);
                    if (separatorIndex == -1)
                    {
                        throw new FormatException();
                    }
                    string optionName = parameterWithoutHyphen.Substring(0, separatorIndex);
                    string optionValue = parameterWithoutHyphen.Substring(separatorIndex + 1);

                    argDictionary.Add(optionName, string.IsNullOrWhiteSpace(optionValue) ? null : optionValue);
                }
                else
                {
                    throw new FormatException();
                }
            }

            return argDictionary;
        }

        private string GetSetting(string name, Dictionary<string, string> argDictionary)
        {
            string setting = null;
            if (argDictionary != null)
            {
                argDictionary.TryGetValue(name, out setting);
            }

            if (string.IsNullOrWhiteSpace(setting))
            {
                QuerySetting(name, out setting);
            }

            return setting;
        }

        private string QuerySetting(string name, out string setting)
        {
            Console.Write($"{name}: ");
            setting = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(setting))
            {
                Console.CursorTop -= 1;
                return QuerySetting(name, out setting);
            }

            return setting;
        }
    }
}
