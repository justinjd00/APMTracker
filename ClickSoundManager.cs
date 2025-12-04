using System;
using System.IO;
using System.Linq;
using System.Media;
using System.Collections.Generic;
using System.Windows.Media;

namespace ApmTracker
{
    // TODO: Improve click sound quality to match Clicket exactly - investigate audio processing/format conversion
    // TODO: Add support for more audio formats (OGG, FLAC)
    // TODO: Implement sound preview functionality
    // TODO: Add per-key sound customization
    
    public enum ClickSoundType
    {
        Off,
        WindowsDefault,
        WindowsNavigate, 
        WindowsNotify,
        Custom1, Custom2, Custom3, Custom4, Custom5,
        Custom6, Custom7, Custom8, Custom9, Custom10
    }

    public class ClickSoundManager : IDisposable
    {
        private float _volume = 0.5f;
        private ClickSoundType _soundType = ClickSoundType.WindowsDefault;
        private bool _isEnabled = false;
        private readonly string _soundsFolder;
        private readonly Dictionary<ClickSoundType, string> _customSoundPaths = new();
        private MediaPlayer? _mediaPlayer;

        public float Volume
        {
            get => _volume;
            set => _volume = Math.Clamp(value, 0f, 1f);
        }

        public ClickSoundType SoundType
        {
            get => _soundType;
            set => _soundType = value;
        }

        public bool IsEnabled
        {
            get => _isEnabled;
            set => _isEnabled = value;
        }

        public ClickSoundManager()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            _soundsFolder = Path.Combine(baseDir, "Sounds");
            
            if (!Directory.Exists(_soundsFolder))
            {
                Directory.CreateDirectory(_soundsFolder);
                CreateInfoFile();
            }

            LoadCustomSounds();
        }

        private void CreateInfoFile()
        {
            var infoPath = Path.Combine(_soundsFolder, "README.txt");
            File.WriteAllText(infoPath, @"=== APM Tracker - Custom Sounds ===

Lege hier WAV oder MP3 Dateien ab!

Empfohlene Quellen für Klick-Sounds:
- https://mechvibes.com/sound-packs/ (Mechvibes Sound-Packs)
- https://pixabay.com/sound-effects/search/click/
- https://freesound.org/search/?q=mouse+click

Tipps:
- Kurze Sounds (unter 100ms) funktionieren am besten
- WAV-Format wird empfohlen
- Benenne die Dateien wie du willst (z.B. 'Gaming_Mouse.wav')

Die Sounds erscheinen automatisch in der App nach Neustart!
");
        }

        private void LoadCustomSounds()
        {
            _customSoundPaths.Clear();
            
            if (!Directory.Exists(_soundsFolder)) return;

            var soundFiles = Directory.GetFiles(_soundsFolder, "*.*")
                .Where(f => f.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) || 
                           f.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) ||
                           f.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => Path.GetFileName(f))
                .Take(10)
                .ToArray();

            for (int i = 0; i < soundFiles.Length && i < 10; i++)
            {
                var soundType = (ClickSoundType)(i + 4);
                _customSoundPaths[soundType] = soundFiles[i];
            }
        }

        public List<(ClickSoundType Type, string Name)> GetAvailableSounds()
        {
            var sounds = new List<(ClickSoundType, string)>
            {
                (ClickSoundType.WindowsDefault, "🔊 Windows Click"),
                (ClickSoundType.WindowsNavigate, "📂 Windows Navigate"),
                (ClickSoundType.WindowsNotify, "🔔 Windows Notify")
            };

            foreach (var kvp in _customSoundPaths.OrderBy(x => x.Key))
            {
                var fileName = Path.GetFileNameWithoutExtension(kvp.Value);
                sounds.Add((kvp.Key, $"🎵 {fileName}"));
            }

            return sounds;
        }

        public void PlayClick()
        {
            if (!_isEnabled || _soundType == ClickSoundType.Off) return;

            try
            {
                var soundPath = GetSoundPath(_soundType);
                if (string.IsNullOrEmpty(soundPath) || !File.Exists(soundPath)) return;

                PlaySoundFile(soundPath);
            }
            catch
            {
            }
        }

        private void PlaySoundFile(string filePath)
        {
            try
            {
                var extension = Path.GetExtension(filePath).ToLower();
                
                if (extension == ".wav")
                {
                    using var player = new SoundPlayer(filePath);
                    player.Play();
                    return;
                }
                else if (extension == ".mp3")
                {
                    if (_mediaPlayer == null)
                    {
                        _mediaPlayer = new MediaPlayer();
                        _mediaPlayer.MediaEnded += (s, e) => { };
                    }
                    
                    _mediaPlayer.Volume = _volume;
                    _mediaPlayer.Open(new Uri(filePath, UriKind.Absolute));
                    _mediaPlayer.Play();
                }
            }
            catch (Exception)
            {
                try
                {
                    if (filePath.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
                    {
                        using var player = new SoundPlayer(filePath);
                        player.Play();
                    }
                }
                catch { }
            }
        }

        private string? GetSoundPath(ClickSoundType type)
        {
            string windowsMediaPath = @"C:\Windows\Media\";
            
            return type switch
            {
                ClickSoundType.WindowsDefault => Path.Combine(windowsMediaPath, "Windows Navigation Start.wav"),
                ClickSoundType.WindowsNavigate => Path.Combine(windowsMediaPath, "Windows Menu Command.wav"),
                ClickSoundType.WindowsNotify => Path.Combine(windowsMediaPath, "Windows Notify System Generic.wav"),
                _ when _customSoundPaths.ContainsKey(type) => _customSoundPaths[type],
                _ => null
            };
        }

        public void RefreshCustomSounds()
        {
            LoadCustomSounds();
        }

        public string GetSoundsFolder() => _soundsFolder;

        public bool HasCustomSounds() => _customSoundPaths.Count > 0;

        public void Dispose()
        {
            _mediaPlayer?.Close();
            _mediaPlayer = null;
        }
    }
}
