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

        public static async Task<bool> DownloadAndInstallUpdateAsync(string downloadUrl, string version, IProgress<string> progress = null)
        {
            var logPath = Path.Combine(Path.GetTempPath(), "ApmTracker_Update.log");
            
            void WriteLog(string message)
            {
                try
                {
                    File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\n");
                    System.Diagnostics.Debug.WriteLine($"UpdateManager: {message}");
                }
                catch { }
            }

            try
            {
                WriteLog("=== Update Process Started ===");
                
                if (string.IsNullOrEmpty(downloadUrl))
                {
                    WriteLog("ERROR: Download URL is empty");
                    progress?.Report("Error: Download URL is empty");
                    return false;
                }

                progress?.Report("Downloading update...");
                WriteLog("Starting download...");
                
                var currentExe = Process.GetCurrentProcess().MainModule?.FileName ?? 
                                System.Reflection.Assembly.GetExecutingAssembly().Location;
                
                WriteLog($"Current EXE: {currentExe}");
                
                if (string.IsNullOrEmpty(currentExe) || !File.Exists(currentExe))
                {
                    WriteLog($"ERROR: Invalid current EXE path: {currentExe}");
                    progress?.Report("Error: Invalid current EXE path");
                    return false;
                }

                var currentDir = Path.GetDirectoryName(currentExe);
                var tempPath = Path.Combine(currentDir, $"ApmTracker_{version}_new.exe");
                
                WriteLog($"Download URL: {downloadUrl}");
                WriteLog($"Target path: {tempPath}");
                
                using (var response = await HttpClient.GetAsync(downloadUrl))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        WriteLog($"ERROR: Download failed with status {response.StatusCode}");
                        progress?.Report($"Error: Download failed ({response.StatusCode})");
                        return false;
                    }
                    
                    progress?.Report("Installing update...");
                    WriteLog("Download successful, saving file...");
                    
                    using (var fileStream = new FileStream(tempPath, FileMode.Create))
                    {
                        await response.Content.CopyToAsync(fileStream);
                    }
                }

                if (!File.Exists(tempPath))
                {
                    WriteLog("ERROR: Downloaded file does not exist");
                    progress?.Report("Error: Downloaded file not found");
                    return false;
                }

                var fileSize = new FileInfo(tempPath).Length;
                WriteLog($"Downloaded file size: {fileSize} bytes");

                progress?.Report("Restarting application...");
                WriteLog("Creating update script...");

                var updateScript = Path.Combine(Path.GetTempPath(), "ApmTracker_Update.bat");
                
                var scriptContent = $@"@echo off
echo [%date% %time%] Update script started >> ""{logPath}""
timeout /t 1 /nobreak >nul
echo [%date% %time%] Killing ApmTracker.exe >> ""{logPath}""
taskkill /F /IM ApmTracker.exe >> ""{logPath}"" 2>&1
timeout /t 3 /nobreak >nul
echo [%date% %time%] Checking files >> ""{logPath}""
if exist ""{tempPath}"" (
    echo [%date% %time%] New file exists: {tempPath} >> ""{logPath}""
    if exist ""{currentExe}"" (
        echo [%date% %time%] Current file exists: {currentExe} >> ""{logPath}""
        echo [%date% %time%] Copying new file to current location >> ""{logPath}""
        copy /Y ""{tempPath}"" ""{currentExe}"" >> ""{logPath}"" 2>&1
        if %ERRORLEVEL% EQU 0 (
            echo [%date% %time%] Copy successful, starting application >> ""{logPath}""
            start """" ""{currentExe}""
            timeout /t 2 /nobreak >nul
            echo [%date% %time%] Deleting temp file >> ""{logPath}""
            del ""{tempPath}"" >> ""{logPath}"" 2>&1
            echo [%date% %time%] Update completed successfully >> ""{logPath}""
        ) else (
            echo [%date% %time%] ERROR: Copy failed with error level %ERRORLEVEL% >> ""{logPath}""
        )
    ) else (
        echo [%date% %time%] ERROR: Current EXE does not exist: {currentExe} >> ""{logPath}""
    )
) else (
    echo [%date% %time%] ERROR: New file does not exist: {tempPath} >> ""{logPath}""
)
echo [%date% %time%] Deleting update script >> ""{logPath}""
del ""%~f0"" >> ""{logPath}"" 2>&1
";
                File.WriteAllText(updateScript, scriptContent, System.Text.Encoding.UTF8);
                WriteLog($"Update script created: {updateScript}");

                var processStartInfo = new ProcessStartInfo
                {
                    FileName = updateScript,
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetTempPath()
                };
                
                WriteLog("Starting update script...");
                var scriptProcess = Process.Start(processStartInfo);
                WriteLog($"Script process started with PID: {scriptProcess?.Id}");

                WriteLog("Shutting down application...");
                await Task.Delay(1500);
                Application.Current.Shutdown();
                return true;
            }
            catch (Exception ex)
            {
                WriteLog($"EXCEPTION: {ex.Message}\n{ex.StackTrace}");
                System.Diagnostics.Debug.WriteLine($"UpdateManager: Exception during update: {ex.Message}\n{ex.StackTrace}");
                progress?.Report($"Error: {ex.Message}");
                return false;
            }
        }
    }
}

