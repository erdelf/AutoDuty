namespace AutoDuty.Managers;
using System;
using System.Collections.Generic;
using System.IO;
using Configurations;
using ECommons.Throttlers;
using Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

internal static class LocalizationManager
{
    private record Translation
    {
        public string Language { get; init; }

        private readonly Dictionary<string, string> keyed = [];

        public bool ready;

        public Translation(string language)
        {
            this.Language = language;
            this.LoadLanguage(language);
        }

        public string? GetTranslation(string key) =>
            this.keyed.GetValueOrDefault(key);

        private void LoadLanguage(string language)
        {
            string dirPath = Path.Combine(LOCALIZATION_PATH, language);

            if (!Directory.Exists(dirPath))
            {
                ADLogging.Warning($"Localization directory not found: {dirPath}");
                return;
            }

            string[] jsonFiles = Directory.GetFiles(dirPath, "*.json");

            if (jsonFiles.Length == 0)
            {
                ADLogging.Warning($"No JSON files found in: {dirPath}");
                return;
            }

            foreach (string filePath in jsonFiles)
            {
                try
                {
                    string                      json = File.ReadAllText(filePath);
                    Dictionary<string, object>? data = JsonConvert.DeserializeObject<Dictionary<string, object>>(json);

                    if (data != null)
                        FlattenJson(data, "", this.keyed);
                }
                catch (Exception ex)
                {
                    ADLogging.Error($"Failed to load {filePath}: {ex.Message}");
                }
            }

            ADLogging.Info($"Loaded {this.keyed.Count} translations for {language} from {jsonFiles.Length} files");

            this.ready = this.keyed.Count > 0;
            return;

            static void FlattenJson(Dictionary<string, object> data, string prefix, Dictionary<string, string> target)
            {
                foreach (KeyValuePair<string, object> kvp in data)
                {
                    string key = string.IsNullOrEmpty(prefix) ? kvp.Key : $"{prefix}.{kvp.Key}";

                    switch (kvp.Value)
                    {
                        case string str:
                            target[key] = str;
                            break;
                        case JObject obj:
                            FlattenJson(obj.ToObject<Dictionary<string, object>>()!, key, target);
                            break;
                    }
                }
            }
        }
    }


    private static readonly Dictionary<string, Translation> translations = new();

    private static Translation ActiveTranslation => translations[ConfigurationMain.Instance.Language];
    private static Translation BaseTranslation => translations[BASE_LANGUAGE];

    public const string BASE_LANGUAGE = "en-US";

    private static readonly string LOCALIZATION_PATH;

    internal static string[] availableLanguages = ["en-US"];

    static LocalizationManager() => 
        LOCALIZATION_PATH = Path.Combine(PluginInterface.AssemblyLocation.Directory!.FullName, "Localization");

    internal static void Initialize()
    {
        List<string> languages = [];
        foreach (string directory in Directory.EnumerateDirectories(LOCALIZATION_PATH, "*", SearchOption.TopDirectoryOnly))
        {
            DirectoryInfo info = new(directory);

            Translation translation = new(info.Name);
            if(translation.ready)
            {
                translations[info.Name] = translation;
                languages.Add(info.Name);
            }
        }
        availableLanguages = languages.ToArray();

        if (!translations.ContainsKey(BASE_LANGUAGE))
            ADLogging.Error("Languages broken");

        SetLanguage(ConfigurationMain.Instance.Language, true);
    }

    internal static void SetLanguage(string language, bool force = false)
    {
        if (!force && ConfigurationMain.Instance.Language == language)
            return;

        //translations.Clear();

        ConfigurationMain.Instance.Language = language;
        ADLogging.Info($"Language changed to: {language}");
    }

    internal static string Get(string key)
    {
        string? translation = ActiveTranslation.GetTranslation(key);

        if (translation != null)
            return translation;

        if(EzThrottler.Throttle($"MissingTranslationKey-{key}~{ConfigurationMain.Instance.Language}", 30_000))
            ADLogging.Warning($"Missing translation key in {ConfigurationMain.Instance.Language}: {key}");

        translation = BaseTranslation.GetTranslation(key);

        if (translation != null)
            return translation;

        if (EzThrottler.Throttle($"MissingTranslationKeyBaseLang-{key}~{BASE_LANGUAGE}", 30_000))
            ADLogging.Error($"Missing translation key in base language {BASE_LANGUAGE}: {key}");
        return key;
    }

    internal static string Get(string key, params object[] args)
    {
        string template = Get(key);
        try
        {
            return string.Format(template, args);
        }
        catch (FormatException ex)
        {
            ADLogging.Error($"Format error for key '{key}': {ex.Message}");
            return template;
        }
    }
}

// Alias for shorter syntax
internal static class Loc
{
    internal static string Get(string key) =>
        LocalizationManager.Get(key);

    internal static string Get(string key, params object[] args) =>
        LocalizationManager.Get(key, args);
}
