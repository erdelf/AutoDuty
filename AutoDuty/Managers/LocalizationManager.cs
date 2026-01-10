namespace AutoDuty.Managers;

using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ECommons.DalamudServices;

internal static class LocalizationManager
{
    private static Dictionary<string, string> _translations = new();
    private static Dictionary<string, string> _fallbackTranslations = new();
    private static string _currentLanguage = "en-US";
    private static readonly string _localizationPath;

    static LocalizationManager()
    {
        _localizationPath = Path.Combine(AutoDuty.PluginInterface.AssemblyLocation.Directory!.FullName, "Localization");
    }

    internal static void Initialize(string language = "en-US")
    {
        LoadLanguage("en-US", ref _fallbackTranslations);
        LoadLanguage(language, ref _translations);
        _currentLanguage = language;
    }

    internal static void SetLanguage(string language)
    {
        if (_currentLanguage == language) return;

        _translations.Clear();
        LoadLanguage(language, ref _translations);
        _currentLanguage = language;
        Svc.Log.Info($"Language changed to: {language}");
    }

    private static void LoadLanguage(string language, ref Dictionary<string, string> target)
    {
        string dirPath = Path.Combine(_localizationPath, language);

        if (!Directory.Exists(dirPath))
        {
            Svc.Log.Warning($"Localization directory not found: {dirPath}");
            return;
        }

        string[] jsonFiles = Directory.GetFiles(dirPath, "*.json");

        if (jsonFiles.Length == 0)
        {
            Svc.Log.Warning($"No JSON files found in: {dirPath}");
            return;
        }

        foreach (string filePath in jsonFiles)
        {
            try
            {
                string json = File.ReadAllText(filePath);
                var data = JsonConvert.DeserializeObject<Dictionary<string, object>>(json);

                if (data != null)
                {
                    FlattenJson(data, "", target);
                }
            }
            catch (Exception ex)
            {
                Svc.Log.Error($"Failed to load {filePath}: {ex.Message}");
            }
        }

        Svc.Log.Info($"Loaded {target.Count} translations for {language} from {jsonFiles.Length} files");
    }

    private static void FlattenJson(Dictionary<string, object> data, string prefix, Dictionary<string, string> target)
    {
        foreach (var kvp in data)
        {
            string key = string.IsNullOrEmpty(prefix) ? kvp.Key : $"{prefix}.{kvp.Key}";

            if (kvp.Value is string str)
            {
                target[key] = str;
            }
            else if (kvp.Value is JObject obj)
            {
                FlattenJson(obj.ToObject<Dictionary<string, object>>()!, key, target);
            }
        }
    }

    internal static string Get(string key, string? fallback = null)
    {
        if (_translations.TryGetValue(key, out string? value))
            return value;

        if (_fallbackTranslations.TryGetValue(key, out value))
            return value;

        Svc.Log.Warning($"Missing translation key: {key}");
        return fallback ?? key;
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
            Svc.Log.Error($"Format error for key '{key}': {ex.Message}");
            return template;
        }
    }

    internal static string CurrentLanguage => _currentLanguage;

    internal static string[] AvailableLanguages => new[] { "en-US", "zh-CN", "zh-TW" };
}

// Alias for shorter syntax
internal static class Loc
{
    internal static string Get(string key, string? fallback = null) =>
        LocalizationManager.Get(key, fallback);

    internal static string Get(string key, params object[] args) =>
        LocalizationManager.Get(key, args);
}
