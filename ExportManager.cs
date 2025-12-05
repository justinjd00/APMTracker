using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace ApmTracker
{
    public static class ExportManager
    {
        public class ExportData
        {
            public DateTime SessionStart { get; set; }
            public DateTime ExportTime { get; set; }
            public TimeSpan SessionDuration { get; set; }
            public int TotalActions { get; set; }
            public int KeyboardActions { get; set; }
            public int MouseActions { get; set; }
            public int PeakApm { get; set; }
            public int MinApm { get; set; }
            public double AverageApm { get; set; }
            public double Apm1Min { get; set; }
            public double Apm5Min { get; set; }
            public List<ApmHistoryEntry> History { get; set; } = new();
        }

        public class ApmHistoryEntry
        {
            public DateTime Timestamp { get; set; }
            public double Apm { get; set; }
        }

        public static bool ExportToCsv(ApmCalculator calculator, string filePath)
        {
            try
            {
                var data = PrepareExportData(calculator);
                
                var csv = new StringBuilder();
                
                // Header
                csv.AppendLine("APM Tracker - Export");
                csv.AppendLine($"Exportiert am: {data.ExportTime:yyyy-MM-dd HH:mm:ss}");
                csv.AppendLine();
                
                // Session-Informationen
                csv.AppendLine("SESSION INFORMATIONEN");
                csv.AppendLine($"Startzeit: {data.SessionStart:yyyy-MM-dd HH:mm:ss}");
                csv.AppendLine($"Dauer: {data.SessionDuration:hh\\:mm\\:ss}");
                csv.AppendLine($"Gesamt-Aktionen: {data.TotalActions}");
                csv.AppendLine($"Tastatur-Aktionen: {data.KeyboardActions}");
                csv.AppendLine($"Maus-Aktionen: {data.MouseActions}");
                csv.AppendLine();
                
                // Statistiken
                csv.AppendLine("STATISTIKEN");
                csv.AppendLine($"Peak APM: {data.PeakApm}");
                csv.AppendLine($"Min APM: {data.MinApm}");
                csv.AppendLine($"Durchschnitt APM: {data.AverageApm:F2}");
                csv.AppendLine($"APM (1 Min): {data.Apm1Min:F2}");
                csv.AppendLine($"APM (5 Min): {data.Apm5Min:F2}");
                csv.AppendLine();
                
                // APM-Historie
                csv.AppendLine("APM HISTORIE");
                csv.AppendLine("Zeitstempel,APM");
                foreach (var entry in data.History)
                {
                    csv.AppendLine($"{entry.Timestamp:yyyy-MM-dd HH:mm:ss},{entry.Apm.ToString("F2", CultureInfo.InvariantCulture)}");
                }
                
                File.WriteAllText(filePath, csv.ToString(), Encoding.UTF8);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CSV Export Fehler: {ex.Message}");
                return false;
            }
        }

        public static bool ExportToJson(ApmCalculator calculator, string filePath)
        {
            try
            {
                var data = PrepareExportData(calculator);
                
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };
                
                var json = JsonSerializer.Serialize(data, options);
                File.WriteAllText(filePath, json, Encoding.UTF8);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"JSON Export Fehler: {ex.Message}");
                return false;
            }
        }

        private static ExportData PrepareExportData(ApmCalculator calculator)
        {
            var history = calculator.GetApmHistory();
            
            return new ExportData
            {
                SessionStart = calculator.SessionStart ?? DateTime.Now,
                ExportTime = DateTime.Now,
                SessionDuration = calculator.GetSessionDuration(),
                TotalActions = calculator.TotalActions,
                KeyboardActions = calculator.KeyboardActions,
                MouseActions = calculator.MouseActions,
                PeakApm = calculator.PeakApm,
                MinApm = calculator.MinApm,
                AverageApm = calculator.CalculateAverageApm(),
                Apm1Min = calculator.CalculateApm1Min(),
                Apm5Min = calculator.CalculateApm5Min(),
                History = history.Select(h => new ApmHistoryEntry
                {
                    Timestamp = h.timestamp,
                    Apm = h.apm
                }).ToList()
            };
        }
    }
}

