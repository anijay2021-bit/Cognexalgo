using System;
using System.IO;
using System.Text.Json;

namespace Cognexalgo.UI.Services
{
    public class UiSettings
    {
        public double WindowWidth { get; set; } = 1400;
        public double WindowHeight { get; set; } = 850;
        public double LogPanelHeight { get; set; } = 200;
        public double MaxLoss { get; set; } = -50000;
        public double MaxProfit { get; set; } = 50000;
    }

    public class UiSettingsService
    {
        private readonly string _filePath;

        public UiSettingsService()
        {
            _filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ui_settings.json");
        }

        public UiSettings Load()
        {
            try
            {
                if (File.Exists(_filePath))
                {
                    string json = File.ReadAllText(_filePath);
                    return JsonSerializer.Deserialize<UiSettings>(json) ?? new UiSettings();
                }
            }
            catch { /* Ignore load errors, return default */ }
            return new UiSettings();
        }

        public void Save(UiSettings settings)
        {
            try
            {
                string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_filePath, json);
            }
            catch { /* Ignore save errors */ }
        }
    }
}
