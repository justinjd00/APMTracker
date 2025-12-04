using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;

namespace ApmTracker
{
    public class UpdateInfo
    {
        public string Version { get; set; } = string.Empty;
        public string DownloadUrl { get; set; } = string.Empty;
        public string ReleaseNotes { get; set; } = string.Empty;
        public DateTime PublishedAt { get; set; }
    }

    public class UpdateManager
    {
        private const string GitHubRepo = "justinjd00/APMTracker";
        private static readonly HttpClient HttpClient = new();

        public static string GetCurrentVersion()
        {
            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            return $"{version?.Major}.{version?.Minor}.{version?.Build}";
        }

        public static async Task<UpdateInfo?> CheckForUpdatesAsync()
        {
            try
            {
                var url = $"https://api.github.com/repos/{GitHubRepo}/releases/latest";
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("User-Agent", "APMTracker");
                
                var response = await HttpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode) return null;

                var json = await response.Content.ReadAsStringAsync();
                var release = JsonSerializer.Deserialize<JsonElement>(json);

                var latestVersion = release.GetProperty("tag_name").GetString()?.TrimStart('v') ?? string.Empty;
                var downloadUrl = string.Empty;
                var releaseNotes = release.GetProperty("body").GetString() ?? string.Empty;
                var publishedAt = release.GetProperty("published_at").GetDateTime();

                if (release.TryGetProperty("assets", out var assets) && assets.GetArrayLength() > 0)
                {
                    downloadUrl = assets[0].GetProperty("browser_download_url").GetString() ?? string.Empty;
                }

                if (IsNewerVersion(latestVersion, GetCurrentVersion()))
                {
                    return new UpdateInfo
                    {
                        Version = latestVersion,
                        DownloadUrl = downloadUrl,
                        ReleaseNotes = releaseNotes,
                        PublishedAt = publishedAt
                    };
                }
            }
            catch
            {
            }

            return null;
        }

        private static bool IsNewerVersion(string version1, string version2)
        {
            try
            {
                var v1 = ParseVersion(version1);
                var v2 = ParseVersion(version2);

                if (v1.Major > v2.Major) return true;
                if (v1.Major < v2.Major) return false;
                if (v1.Minor > v2.Minor) return true;
                if (v1.Minor < v2.Minor) return false;
                return v1.Patch > v2.Patch;
            }
            catch
            {
                return false;
            }
        }

        private static (int Major, int Minor, int Patch) ParseVersion(string version)
        {
            var parts = version.Split('.');
            return (
                int.Parse(parts.Length > 0 ? parts[0] : "0"),
                int.Parse(parts.Length > 1 ? parts[1] : "0"),
                int.Parse(parts.Length > 2 ? parts[2] : "0")
            );
        }

        public static async Task<bool> DownloadAndInstallUpdateAsync(string downloadUrl, string version)
        {
            try
            {
                var tempPath = Path.Combine(Path.GetTempPath(), $"ApmTracker_{version}.exe");
                
                using (var response = await HttpClient.GetAsync(downloadUrl))
                using (var fileStream = new FileStream(tempPath, FileMode.Create))
                {
                    await response.Content.CopyToAsync(fileStream);
                }

                var currentExe = Process.GetCurrentProcess().MainModule?.FileName ?? 
                                System.Reflection.Assembly.GetExecutingAssembly().Location;
                var updateScript = Path.Combine(Path.GetTempPath(), "ApmTracker_Update.bat");
                
                var scriptContent = $@"@echo off
timeout /t 2 /nobreak >nul
taskkill /F /IM ApmTracker.exe 2>nul
timeout /t 1 /nobreak >nul
copy /Y ""{tempPath}"" ""{currentExe}"" >nul
start """" ""{currentExe}""
del ""{tempPath}""
del ""%~f0""
";
                File.WriteAllText(updateScript, scriptContent);

                Process.Start(new ProcessStartInfo
                {
                    FileName = updateScript,
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                });

                Application.Current.Shutdown();
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}

