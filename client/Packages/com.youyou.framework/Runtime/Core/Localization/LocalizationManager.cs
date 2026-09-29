using System;
using System.Collections.Generic;
using System.Globalization;

namespace YouYou.Framework
{
    public sealed class LocalizationManager
    {
        private Dictionary<string, string> strings = new Dictionary<string, string>();
        public string Language { get; private set; } = "";
        public event Action LanguageChanged;
        public void SetLanguage(string language, IDictionary<string, string> translations)
        {
            if (language == null) throw new ArgumentNullException(nameof(language));
            if (translations == null) throw new ArgumentNullException(nameof(translations));
            strings = new Dictionary<string, string>(translations);
            Language = language;
            LanguageChanged?.Invoke();
        }
        public string GetString(string key, params object[] args)
        {
            var value = strings.TryGetValue(key, out var text) ? text : key;
            return args == null || args.Length == 0 ? value : string.Format(CultureInfo.CurrentCulture, value, args);
        }
        public void Clear() { strings.Clear(); Language = ""; LanguageChanged = null; }
    }
}
