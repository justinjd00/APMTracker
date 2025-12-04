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
                if (string.IsNullOrEmpty(downloadUrl))
                {
                    System.Diagnostics.Debug.WriteLine("UpdateManager: Download URL is empty");
                    return false;
                }

                var tempPath = Path.Combine(Path.GetTempPath(), $"ApmTracker_{version}.exe");
                
                System.Diagnostics.Debug.WriteLine($"UpdateManager: Downloading from {downloadUrl} to {tempPath}");
                
                using (var response = await HttpClient.GetAsync(downloadUrl))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        System.Diagnostics.Debug.WriteLine($"UpdateManager: Download failed with status {response.StatusCode}");
                        return false;
                    }
                    
                    using (var fileStream = new FileStream(tempPath, FileMode.Create))
                    {
                        await response.Content.CopyToAsync(fileStream);
                    }
                }

                if (!File.Exists(tempPath))
                {
                    System.Diagnostics.Debug.WriteLine("UpdateManager: Downloaded file does not exist");
                    return false;
                }

                var currentExe = Process.GetCurrentProcess().MainModule?.FileName ?? 
                                System.Reflection.Assembly.GetExecutingAssembly().Location;
                
                if (string.IsNullOrEmpty(currentExe) || !File.Exists(currentExe))
                {
                    System.Diagnostics.Debug.WriteLine($"UpdateManager: Current EXE path is invalid: {currentExe}");
                    return false;
                }

                System.Diagnostics.Debug.WriteLine($"UpdateManager: Current EXE: {currentExe}");
                System.Diagnostics.Debug.WriteLine($"UpdateManager: New EXE: {tempPath}");

                var updateScript = Path.Combine(Path.GetTempPath(), "ApmTracker_Update.bat");
                
                var scriptContent = $@"@echo off
chcp 65001 >nul
timeout /t 2 /nobreak >nul
taskkill /F /IM ApmTracker.exe 2>nul
timeout /t 2 /nobreak >nul
if exist ""{tempPath}"" (
    if exist ""{currentExe}"" (
        copy /Y ""{tempPath}"" ""{currentExe}"" >nul 2>&1
        if %ERRORLEVEL% EQU 0 (
            start """" ""{currentExe}""
            timeout /t 1 /nobreak >nul
            del ""{tempPath}"" >nul 2>&1
        )
    )
)
del ""%~f0"" >nul 2>&1
";
                File.WriteAllText(updateScript, scriptContent, System.Text.Encoding.UTF8);

                System.Diagnostics.Debug.WriteLine($"UpdateManager: Starting update script: {updateScript}");

                var processStartInfo = new ProcessStartInfo
                {
                    FileName = updateScript,
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    CreateNoWindow = true
                };
                
                Process.Start(processStartInfo);

                System.Diagnostics.Debug.WriteLine("UpdateManager: Update script started, shutting down application");
                
                await Task.Delay(500);
                Application.Current.Shutdown();
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"UpdateManager: Exception during update: {ex.Message}\n{ex.StackTrace}");
                return false;
            }
        }
    }
}

