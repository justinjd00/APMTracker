using System;
using System.IO;
using System.Text.Json;

namespace ApmTracker
{
    public class AppSettings
    {
        public string StreamerFont { get; set; } = "pack://application:,,,/Fonts/#Orbitron";
        public bool ClickSoundEnabled { get; set; } = false;
        public string ClickSoundType { get; set; } = "WindowsDefault";
        public int Volume { get; set; } = 50;
        public double NormalModeLeft { get; set; } = double.NaN;
        public double NormalModeTop { get; set; } = double.NaN;
        public double StreamerModeLeft { get; set; } = double.NaN;
        public double StreamerModeTop { get; set; } = double.NaN;
        
        public string ApmColorNormal { get; set; } = "FFFFFF";
        public string ApmColorCommon { get; set; } = "00C8FF";
        public string ApmColorUncommon { get; set; } = "B400FF";
        public string ApmColorRare { get; set; } = "FF3232";
        public string ApmColorEpic { get; set; } = "FFD700";
        public string ApmColorLegendary { get; set; } = "FF0080";
    }

    public static class SettingsManager
    {
        private static readonly string SettingsPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, 
            "settings.json");

        public static AppSettings LoadSettings()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var json = File.ReadAllText(SettingsPath);
                    var settings = JsonSerializer.Deserialize<AppSettings>(json);
                    return settings ?? new AppSettings();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading settings: {ex.Message}");
            }

            return new AppSettings();
        }

        public static void SaveSettings(AppSettings settings)
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(settings, options);
                File.WriteAllText(SettingsPath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving settings: {ex.Message}");
            }
        }
    }
}
