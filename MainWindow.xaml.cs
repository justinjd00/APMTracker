using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Windows.Forms;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace ApmTracker
{
    // TODO: Add hotkey support for start/pause/reset
    // TODO: Add export statistics functionality (CSV/JSON)
    // TODO: Add theme customization (light/dark mode)
    // TODO: Improve multi-monitor support for window positioning
    // TODO: Add APM history graph/chart
    // TODO: Add sound customization per action type (keyboard vs mouse)
    
    public partial class MainWindow : Window
    {
        private readonly RawInputHook _inputHook;
        private readonly ApmCalculator _apmCalculator;
        private readonly DispatcherTimer _updateTimer;
        private readonly ClickSoundManager _clickSoundManager;
        private bool _isTracking = false;
        private bool _isStreamerMode = false;
        private bool _isInitialized = false;
        private int _lastMilestone = 0;
        
        private int _lastCurrentApm = -1;
        private int _lastPeakApm = -1;
        private int _lastAvgApm = -1;
        private int _lastTotalActions = -1;
        private int _lastKeyboardActions = -1;
        private int _lastMouseActions = -1;
        private string _lastSessionTime = string.Empty;
        
        private static readonly Dictionary<int, Brush> _colorCache = new();
        private static AppSettings? _apmColorSettings = null;
        private const double NormalHeight = 1110;
        private const double NormalWidth = 650;
        
        private string _streamerFont = "pack://application:,,,/Fonts/#Orbitron";
        private const double StreamerHeight = 52;
        private const double StreamerWidth = 250;
        
        private double _normalModeLeft = double.NaN;
        private double _normalModeTop = double.NaN;
        private double _streamerModeLeft = double.NaN;
        private double _streamerModeTop = double.NaN;
        
        private bool _isSettingPosition = false;
        public MainWindow()
        {
            try
            {
                InitializeComponent();
                _inputHook = new RawInputHook();
                _apmCalculator = new ApmCalculator();
                _clickSoundManager = new ClickSoundManager();
                _inputHook.OnInput += OnInputReceived;
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"Error starting application:\n\n{ex.Message}\n\n{ex.StackTrace}",
                    "APM Tracker - Startup Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
                throw;
            }
            _updateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(200)
            };
            _updateTimer.Tick += UpdateTimer_Tick;
            MouseDoubleClick += Window_MouseDoubleClick;
            LocationChanged += Window_LocationChanged;
            Loaded += MainWindow_Loaded;
        }
        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            _isInitialized = true;
            LoadSettings();
            LoadSoundOptions();
            CheckForUpdatesAsync();
        }
        private void LoadSettings()
        {
            var settings = SettingsManager.LoadSettings();
            
            if (!string.IsNullOrEmpty(settings.StreamerFont))
            {
                _streamerFont = settings.StreamerFont;
                foreach (System.Windows.Controls.ComboBoxItem item in FontComboBox.Items)
                {
                    if (item.Tag?.ToString() == settings.StreamerFont)
                    {
                        FontComboBox.SelectedItem = item;
                        ApplyStreamerFont();
                        break;
                    }
                }
            }
            
            ClickSoundCheckbox.IsChecked = settings.ClickSoundEnabled;
            _clickSoundManager.IsEnabled = settings.ClickSoundEnabled;
            
            if (Enum.TryParse<ClickSoundType>(settings.ClickSoundType, out var soundType))
            {
                _clickSoundManager.SoundType = soundType;
                foreach (System.Windows.Controls.ComboBoxItem item in ClickSoundComboBox.Items)
                {
                    if (item.Tag?.ToString() == settings.ClickSoundType)
                    {
                        ClickSoundComboBox.SelectedItem = item;
                        break;
                    }
                }
            }
            
            VolumeSlider.Value = settings.Volume;
            _clickSoundManager.Volume = (float)(settings.Volume / 100.0);
            VolumeText.Text = $"{settings.Volume}%";
            
            CenterWindowOnPrimaryScreen();
            
            if (!double.IsNaN(settings.NormalModeLeft) && !double.IsNaN(settings.NormalModeTop))
            {
                if (IsPositionOnValidScreen(settings.NormalModeLeft, settings.NormalModeTop))
                {
                    _normalModeLeft = settings.NormalModeLeft;
                    _normalModeTop = settings.NormalModeTop;
                }
            }
            
            if (!double.IsNaN(settings.StreamerModeLeft) && !double.IsNaN(settings.StreamerModeTop))
            {
                _streamerModeLeft = settings.StreamerModeLeft;
                _streamerModeTop = settings.StreamerModeTop;
            }
            
            ApmColorNormalText.Text = settings.ApmColorNormal;
            ApmColorCommonText.Text = settings.ApmColorCommon;
            ApmColorUncommonText.Text = settings.ApmColorUncommon;
            ApmColorRareText.Text = settings.ApmColorRare;
            ApmColorEpicText.Text = settings.ApmColorEpic;
            ApmColorLegendaryText.Text = settings.ApmColorLegendary;
            
            UpdateColorPreviews();
            
            RefreshApmColors();
        }
        
        private void UpdateColorPreviews()
        {
            UpdateColorPreview(ApmColorNormalText.Text, "ApmColorNormalPreview");
            UpdateColorPreview(ApmColorCommonText.Text, "ApmColorCommonPreview");
            UpdateColorPreview(ApmColorUncommonText.Text, "ApmColorUncommonPreview");
            UpdateColorPreview(ApmColorRareText.Text, "ApmColorRarePreview");
            UpdateColorPreview(ApmColorEpicText.Text, "ApmColorEpicPreview");
            UpdateColorPreview(ApmColorLegendaryText.Text, "ApmColorLegendaryPreview");
        }
        
        private void UpdateColorPreview(string hexColor, string borderName)
        {
            try
            {
                var border = FindName(borderName) as System.Windows.Controls.Border;
                if (border != null && hexColor.Length == 6 && System.Text.RegularExpressions.Regex.IsMatch(hexColor, "^[0-9A-Fa-f]{6}$"))
                {
                    var r = Convert.ToByte(hexColor.Substring(0, 2), 16);
                    var g = Convert.ToByte(hexColor.Substring(2, 2), 16);
                    var b = Convert.ToByte(hexColor.Substring(4, 2), 16);
                    border.Background = new SolidColorBrush(Color.FromRgb(r, g, b));
                }
            }
            catch
            {
            }
        }
        
        private void ApmColorText_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (!_isInitialized) return;
            
            var textBox = sender as System.Windows.Controls.TextBox;
            if (textBox == null) return;
            
            var text = textBox.Text.Replace("#", "").ToUpper();
            if (text.Length > 6) text = text.Substring(0, 6);
            textBox.Text = text;
            
            string? previewName = textBox.Name switch
            {
                "ApmColorNormalText" => "ApmColorNormalPreview",
                "ApmColorCommonText" => "ApmColorCommonPreview",
                "ApmColorUncommonText" => "ApmColorUncommonPreview",
                "ApmColorRareText" => "ApmColorRarePreview",
                "ApmColorEpicText" => "ApmColorEpicPreview",
                "ApmColorLegendaryText" => "ApmColorLegendaryPreview",
                _ => null
            };
            
            if (previewName != null)
            {
                UpdateColorPreview(text, previewName);
            }
            
            SaveSettings();
            
            RefreshApmColors();
            
            UpdateUI();
        }
        private void SaveSettings()
        {
            if (_isStreamerMode)
            {
                _streamerModeLeft = Left;
                _streamerModeTop = Top;
            }
            else
            {
                _normalModeLeft = Left;
                _normalModeTop = Top;
            }
            
            var settings = new AppSettings
            {
                StreamerFont = _streamerFont,
                ClickSoundEnabled = _clickSoundManager.IsEnabled,
                ClickSoundType = _clickSoundManager.SoundType.ToString(),
                Volume = (int)VolumeSlider.Value,
                NormalModeLeft = _normalModeLeft,
                NormalModeTop = _normalModeTop,
                StreamerModeLeft = _streamerModeLeft,
                StreamerModeTop = _streamerModeTop,
                ApmColorNormal = ApmColorNormalText?.Text ?? "FFFFFF",
                ApmColorCommon = ApmColorCommonText?.Text ?? "00C8FF",
                ApmColorUncommon = ApmColorUncommonText?.Text ?? "B400FF",
                ApmColorRare = ApmColorRareText?.Text ?? "FF3232",
                ApmColorEpic = ApmColorEpicText?.Text ?? "FFD700",
                ApmColorLegendary = ApmColorLegendaryText?.Text ?? "FF0080"
            };
            
            SettingsManager.SaveSettings(settings);
        }
        private void LoadSoundOptions()
        {
            ClickSoundComboBox.Items.Clear();
            
            var sounds = _clickSoundManager.GetAvailableSounds();
            bool isFirst = true;
            
            foreach (var (soundType, name) in sounds)
            {
                var item = new System.Windows.Controls.ComboBoxItem
                {
                    Content = name,
                    Tag = soundType.ToString(),
                    Style = (Style)FindResource("DarkComboBoxItem")
                };
                
                if (isFirst)
                {
                    item.IsSelected = true;
                    isFirst = false;
                }
                
                ClickSoundComboBox.Items.Add(item);
            }
        }
        private void OnInputReceived(InputType type)
        {
            _apmCalculator.RecordAction(type);
            
            if (type == InputType.MouseLeft || type == InputType.MouseRight || 
                type == InputType.MouseMiddle || type == InputType.MouseExtra)
            {
                if (Dispatcher.CheckAccess())
                {
                    _clickSoundManager.PlayClick();
                }
                else
                {
                    Dispatcher.BeginInvoke(() => _clickSoundManager.PlayClick());
                }
            }
        }
        private void UpdateTimer_Tick(object? sender, EventArgs e)
        {
            UpdateUI();
        }
        private void UpdateUI()
        {
            var currentApm = _apmCalculator.CalculateCurrentApm();
            var peakApm = _apmCalculator.PeakApm;
            var avgApm = (int)Math.Round(_apmCalculator.CalculateAverageApm());
            var totalActions = _apmCalculator.TotalActions;
            var keyboardActions = _apmCalculator.KeyboardActions;
            var mouseActions = _apmCalculator.MouseActions;
            var sessionTime = FormatTimeSpan(_apmCalculator.GetSessionDuration());
            
            if (currentApm != _lastCurrentApm)
            {
                var currentApmStr = currentApm.ToString();
                CurrentApmText.Text = currentApmStr;
                _lastCurrentApm = currentApm;
                
                StreamerLiveApmText.Text = currentApmStr;
                StreamerLiveApmShadow.Text = currentApmStr;
                StreamerLiveApmShadow2.Text = currentApmStr;
                StreamerLiveApmShadow3.Text = currentApmStr;
                StreamerLiveApmShadow4.Text = currentApmStr;
                StreamerLiveApmShadow5.Text = currentApmStr;
                StreamerLiveApmOutline1.Text = currentApmStr;
                StreamerLiveApmOutline2.Text = currentApmStr;
                StreamerLiveApmOutline3.Text = currentApmStr;
                StreamerLiveApmOutline4.Text = currentApmStr;
                StreamerLiveApmOutline5.Text = currentApmStr;
                StreamerLiveApmOutline6.Text = currentApmStr;
                StreamerLiveApmOutline7.Text = currentApmStr;
                StreamerLiveApmOutline8.Text = currentApmStr;
                
                var apmColor = GetApmColor(currentApm);
                StreamerLiveApmText.Foreground = apmColor;
                CurrentApmText.Foreground = apmColor;
            }
            
            _apmCalculator.UpdatePeakApm(currentApm);
            peakApm = _apmCalculator.PeakApm; // Refresh nach Update
            
            if (peakApm != _lastPeakApm)
            {
                var peakApmStr = peakApm.ToString();
                PeakApmText.Text = peakApmStr;
                StreamerPeakApmText.Text = peakApmStr;
                _lastPeakApm = peakApm;
            }
            
            if (avgApm != _lastAvgApm)
            {
                AvgApmText.Text = avgApm.ToString();
                _lastAvgApm = avgApm;
            }
            
            if (totalActions != _lastTotalActions)
            {
                TotalActionsText.Text = FormatNumber(totalActions);
                _lastTotalActions = totalActions;
            }
            
            if (keyboardActions != _lastKeyboardActions)
            {
                KeyboardActionsText.Text = FormatNumber(keyboardActions);
                _lastKeyboardActions = keyboardActions;
            }
            
            if (mouseActions != _lastMouseActions)
            {
                MouseActionsText.Text = FormatNumber(mouseActions);
                _lastMouseActions = mouseActions;
            }
            
            if (sessionTime != _lastSessionTime)
            {
                SessionTimeText.Text = sessionTime;
                _lastSessionTime = sessionTime;
            }
            int currentMilestone = GetMilestone(currentApm);
            if (currentMilestone > _lastMilestone && _lastMilestone >= 0)
            {
                PlayPulseAnimation();
                PlayMainMenuPulseAnimation();
            }
            _lastMilestone = currentMilestone;
        }

        private static int GetMilestone(int apm)
        {
            return apm switch
            {
                >= 500 => 500,
                >= 400 => 400,
                >= 300 => 300,
                >= 200 => 200,
                >= 100 => 100,
                _ => 0
            };
        }

        private void PlayPulseAnimation()
        {
            var pulseAnimation = new DoubleAnimation
            {
                From = 1.0,
                To = 1.15,
                Duration = TimeSpan.FromMilliseconds(80),
                AutoReverse = true,
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            var scaleTransform = new ScaleTransform(1.0, 1.0);
            StreamerLiveApmText.RenderTransform = scaleTransform;
            StreamerLiveApmText.RenderTransformOrigin = new Point(0.5, 0.5);
            var shadowScale = new ScaleTransform(1.0, 1.0);
            var shadowTransform = new TransformGroup();
            shadowTransform.Children.Add(new TranslateTransform(3.3, 0.7));
            shadowTransform.Children.Add(shadowScale);
            StreamerLiveApmShadow.RenderTransform = shadowTransform;
            StreamerLiveApmShadow.RenderTransformOrigin = new Point(0.5, 0.5);
            
            var shadow2Scale = new ScaleTransform(1.0, 1.0);
            var shadow2Transform = new TransformGroup();
            shadow2Transform.Children.Add(new TranslateTransform(3.6, 1));
            shadow2Transform.Children.Add(shadow2Scale);
            StreamerLiveApmShadow2.RenderTransform = shadow2Transform;
            StreamerLiveApmShadow2.RenderTransformOrigin = new Point(0.5, 0.5);
            
            var shadow3Scale = new ScaleTransform(1.0, 1.0);
            var shadow3Transform = new TransformGroup();
            shadow3Transform.Children.Add(new TranslateTransform(3.9, 1.3));
            shadow3Transform.Children.Add(shadow3Scale);
            StreamerLiveApmShadow3.RenderTransform = shadow3Transform;
            StreamerLiveApmShadow3.RenderTransformOrigin = new Point(0.5, 0.5);
            
            var shadow4Scale = new ScaleTransform(1.0, 1.0);
            var shadow4Transform = new TransformGroup();
            shadow4Transform.Children.Add(new TranslateTransform(4.2, 1.6));
            shadow4Transform.Children.Add(shadow4Scale);
            StreamerLiveApmShadow4.RenderTransform = shadow4Transform;
            StreamerLiveApmShadow4.RenderTransformOrigin = new Point(0.5, 0.5);
            
            var shadow5Scale = new ScaleTransform(1.0, 1.0);
            var shadow5Transform = new TransformGroup();
            shadow5Transform.Children.Add(new TranslateTransform(4.6, 2));
            shadow5Transform.Children.Add(shadow5Scale);
            StreamerLiveApmShadow5.RenderTransform = shadow5Transform;
            StreamerLiveApmShadow5.RenderTransformOrigin = new Point(0.5, 0.5);
            
            scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, pulseAnimation);
            scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, pulseAnimation);
            shadowScale.BeginAnimation(ScaleTransform.ScaleXProperty, pulseAnimation);
            shadowScale.BeginAnimation(ScaleTransform.ScaleYProperty, pulseAnimation);
            shadow2Scale.BeginAnimation(ScaleTransform.ScaleXProperty, pulseAnimation);
            shadow2Scale.BeginAnimation(ScaleTransform.ScaleYProperty, pulseAnimation);
            shadow3Scale.BeginAnimation(ScaleTransform.ScaleXProperty, pulseAnimation);
            shadow3Scale.BeginAnimation(ScaleTransform.ScaleYProperty, pulseAnimation);
            shadow4Scale.BeginAnimation(ScaleTransform.ScaleXProperty, pulseAnimation);
            shadow4Scale.BeginAnimation(ScaleTransform.ScaleYProperty, pulseAnimation);
            shadow5Scale.BeginAnimation(ScaleTransform.ScaleXProperty, pulseAnimation);
            shadow5Scale.BeginAnimation(ScaleTransform.ScaleYProperty, pulseAnimation);
            
            var labelShadowScale = new ScaleTransform(1.0, 1.0);
            var labelShadowTransform = new TransformGroup();
            labelShadowTransform.Children.Add(new TranslateTransform(3.3, 0.7));
            labelShadowTransform.Children.Add(labelShadowScale);
            StreamerApmLabelShadow1.RenderTransform = labelShadowTransform;
            StreamerApmLabelShadow1.RenderTransformOrigin = new Point(0.5, 0.5);
            
            var labelShadow2Scale = new ScaleTransform(1.0, 1.0);
            var labelShadow2Transform = new TransformGroup();
            labelShadow2Transform.Children.Add(new TranslateTransform(3.6, 1));
            labelShadow2Transform.Children.Add(labelShadow2Scale);
            StreamerApmLabelShadow2.RenderTransform = labelShadow2Transform;
            StreamerApmLabelShadow2.RenderTransformOrigin = new Point(0.5, 0.5);
            
            var labelShadow3Scale = new ScaleTransform(1.0, 1.0);
            var labelShadow3Transform = new TransformGroup();
            labelShadow3Transform.Children.Add(new TranslateTransform(3.9, 1.3));
            labelShadow3Transform.Children.Add(labelShadow3Scale);
            StreamerApmLabelShadow3.RenderTransform = labelShadow3Transform;
            StreamerApmLabelShadow3.RenderTransformOrigin = new Point(0.5, 0.5);
            
            var labelShadow4Scale = new ScaleTransform(1.0, 1.0);
            var labelShadow4Transform = new TransformGroup();
            labelShadow4Transform.Children.Add(new TranslateTransform(4.2, 1.6));
            labelShadow4Transform.Children.Add(labelShadow4Scale);
            StreamerApmLabelShadow4.RenderTransform = labelShadow4Transform;
            StreamerApmLabelShadow4.RenderTransformOrigin = new Point(0.5, 0.5);
            
            var labelShadow5Scale = new ScaleTransform(1.0, 1.0);
            var labelShadow5Transform = new TransformGroup();
            labelShadow5Transform.Children.Add(new TranslateTransform(4.6, 2));
            labelShadow5Transform.Children.Add(labelShadow5Scale);
            StreamerApmLabelShadow5.RenderTransform = labelShadow5Transform;
            StreamerApmLabelShadow5.RenderTransformOrigin = new Point(0.5, 0.5);
            
            labelShadowScale.BeginAnimation(ScaleTransform.ScaleXProperty, pulseAnimation);
            labelShadowScale.BeginAnimation(ScaleTransform.ScaleYProperty, pulseAnimation);
            labelShadow2Scale.BeginAnimation(ScaleTransform.ScaleXProperty, pulseAnimation);
            labelShadow2Scale.BeginAnimation(ScaleTransform.ScaleYProperty, pulseAnimation);
            labelShadow3Scale.BeginAnimation(ScaleTransform.ScaleXProperty, pulseAnimation);
            labelShadow3Scale.BeginAnimation(ScaleTransform.ScaleYProperty, pulseAnimation);
            labelShadow4Scale.BeginAnimation(ScaleTransform.ScaleXProperty, pulseAnimation);
            labelShadow4Scale.BeginAnimation(ScaleTransform.ScaleYProperty, pulseAnimation);
            labelShadow5Scale.BeginAnimation(ScaleTransform.ScaleXProperty, pulseAnimation);
            labelShadow5Scale.BeginAnimation(ScaleTransform.ScaleYProperty, pulseAnimation);
            
            var labelScaleTransform = new ScaleTransform(1.0, 1.0);
            StreamerApmLabelText.RenderTransform = labelScaleTransform;
            StreamerApmLabelText.RenderTransformOrigin = new Point(0.5, 0.5);
            labelScaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, pulseAnimation);
            labelScaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, pulseAnimation);
            
            StreamerLiveApmOutline1.RenderTransform = scaleTransform;
            StreamerLiveApmOutline1.RenderTransformOrigin = new Point(0.5, 0.5);
            StreamerLiveApmOutline2.RenderTransform = scaleTransform;
            StreamerLiveApmOutline2.RenderTransformOrigin = new Point(0.5, 0.5);
            StreamerLiveApmOutline3.RenderTransform = scaleTransform;
            StreamerLiveApmOutline3.RenderTransformOrigin = new Point(0.5, 0.5);
            StreamerLiveApmOutline4.RenderTransform = scaleTransform;
            StreamerLiveApmOutline4.RenderTransformOrigin = new Point(0.5, 0.5);
            StreamerLiveApmOutline5.RenderTransform = scaleTransform;
            StreamerLiveApmOutline5.RenderTransformOrigin = new Point(0.5, 0.5);
            StreamerLiveApmOutline6.RenderTransform = scaleTransform;
            StreamerLiveApmOutline6.RenderTransformOrigin = new Point(0.5, 0.5);
            StreamerLiveApmOutline7.RenderTransform = scaleTransform;
            StreamerLiveApmOutline7.RenderTransformOrigin = new Point(0.5, 0.5);
            StreamerLiveApmOutline8.RenderTransform = scaleTransform;
            StreamerLiveApmOutline8.RenderTransformOrigin = new Point(0.5, 0.5);
        }

        private void PlayMainMenuPulseAnimation()
        {
            var scaleTransform = new ScaleTransform(1.0, 1.0);
            CurrentApmText.RenderTransform = scaleTransform;
            CurrentApmText.RenderTransformOrigin = new Point(0.5, 0.5);
            var pulseAnimation = new DoubleAnimation
            {
                From = 1.0,
                To = 1.1,
                Duration = TimeSpan.FromMilliseconds(100),
                AutoReverse = true,
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, pulseAnimation);
            scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, pulseAnimation);
        }

        private Brush GetApmColor(int apm)
        {
            if (_apmColorSettings == null)
            {
                _apmColorSettings = SettingsManager.LoadSettings();
            }
            
            int milestone = GetMilestone(apm);
            
            if (!_colorCache.TryGetValue(milestone, out var brush))
            {
                string hexColor = milestone switch
                {
                    500 => _apmColorSettings.ApmColorLegendary,
                    400 => _apmColorSettings.ApmColorEpic,
                    300 => _apmColorSettings.ApmColorRare,
                    200 => _apmColorSettings.ApmColorUncommon,
                    100 => _apmColorSettings.ApmColorCommon,
                    _ => _apmColorSettings.ApmColorNormal
                };
                
                if (hexColor.Length == 6 && System.Text.RegularExpressions.Regex.IsMatch(hexColor, "^[0-9A-Fa-f]{6}$"))
                {
                    var r = Convert.ToByte(hexColor.Substring(0, 2), 16);
                    var g = Convert.ToByte(hexColor.Substring(2, 2), 16);
                    var b = Convert.ToByte(hexColor.Substring(4, 2), 16);
                    brush = new SolidColorBrush(Color.FromRgb(r, g, b));
                }
                else
                {
                    brush = milestone switch
                    {
                        500 => new SolidColorBrush(Color.FromRgb(255, 0, 128)),
                        400 => new SolidColorBrush(Color.FromRgb(255, 215, 0)),
                        300 => new SolidColorBrush(Color.FromRgb(255, 50, 50)),
                        200 => new SolidColorBrush(Color.FromRgb(180, 0, 255)),
                        100 => new SolidColorBrush(Color.FromRgb(0, 200, 255)),
                        _ => new SolidColorBrush(Color.FromRgb(255, 255, 255))
                    };
                }
                
                brush.Freeze(); // WPF: Freeze für bessere Performance
                _colorCache[milestone] = brush;
            }
            
            return brush;
        }

        private void RefreshApmColors()
        {
            _colorCache.Clear(); // Cache leeren, damit neue Farben geladen werden
            _apmColorSettings = SettingsManager.LoadSettings();
        }
        private static string FormatNumber(int number)
        {
            return number >= 1000 ? $"{number / 1000.0:F1}k" : number.ToString();
        }
        private static string FormatTimeSpan(TimeSpan ts)
        {
            return $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}";
        }
        private void ToggleButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isTracking)
            {
                StopTracking();
            }
            else
            {
                StartTracking();
            }
        }
        private void StartTracking()
        {
            if (!_isInitialized) return;
            
            _isTracking = true;
            _inputHook.Start(this); // Raw Input braucht das Window
            _updateTimer.Start();
            ToggleButton.Content = "⏸ PAUSE";
        }
        private void StopTracking()
        {
            _isTracking = false;
            _inputHook.Stop();
            _updateTimer.Stop();
            ToggleButton.Content = "▶ START";
        }
        private void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            _apmCalculator.Reset();
            UpdateUI();
        }
        private void StreamerModeButton_Click(object sender, RoutedEventArgs e)
        {
            ToggleStreamerMode();
        }
        private void Window_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (_isStreamerMode)
            {
                ToggleStreamerMode();
                e.Handled = true;
            }
        }
        private void ToggleStreamerMode()
        {
            _isStreamerMode = !_isStreamerMode;
            ApplyStreamerMode();
        }
        private void ApplyStreamerMode()
        {
            if (_isStreamerMode)
            {
                if (NormalModeContainer.Visibility == Visibility.Visible)
                {
                    _normalModeLeft = Left;
                    _normalModeTop = Top;
                    var settings = new AppSettings
                    {
                        StreamerFont = _streamerFont,
                        ClickSoundEnabled = _clickSoundManager.IsEnabled,
                        ClickSoundType = _clickSoundManager.SoundType.ToString(),
                        Volume = (int)VolumeSlider.Value,
                        NormalModeLeft = _normalModeLeft,
                        NormalModeTop = _normalModeTop,
                        StreamerModeLeft = _streamerModeLeft, // Streamer Mode Position bleibt erhalten
                        StreamerModeTop = _streamerModeTop,
                        ApmColorNormal = ApmColorNormalText?.Text ?? "FFFFFF",
                        ApmColorCommon = ApmColorCommonText?.Text ?? "00C8FF",
                        ApmColorUncommon = ApmColorUncommonText?.Text ?? "B400FF",
                        ApmColorRare = ApmColorRareText?.Text ?? "FF3232",
                        ApmColorEpic = ApmColorEpicText?.Text ?? "FFD700",
                        ApmColorLegendary = ApmColorLegendaryText?.Text ?? "FF0080"
                    };
                    SettingsManager.SaveSettings(settings);
                }
                
                NormalModeContainer.Visibility = Visibility.Collapsed;
                NormalModeContainer.IsHitTestVisible = false;
                
                Width = StreamerWidth;
                Height = StreamerHeight;
                
                _isSettingPosition = true;
                
                if (!double.IsNaN(_streamerModeLeft) && !double.IsNaN(_streamerModeTop))
                {
                    if (IsPositionOnValidScreen(_streamerModeLeft, _streamerModeTop))
                    {
                        Left = _streamerModeLeft;
                        Top = _streamerModeTop;
                    }
                    else
                    {
                        CenterWindowOnCurrentScreen();
                        _streamerModeLeft = Left;
                        _streamerModeTop = Top;
                    }
                }
                else
                {
                    CenterWindowOnCurrentScreen();
                    _streamerModeLeft = Left;
                    _streamerModeTop = Top;
                }
                
                if (!double.IsNaN(_streamerModeLeft) && !double.IsNaN(_streamerModeTop))
                {
                    var settingsAfterPosition = new AppSettings
                    {
                        StreamerFont = _streamerFont,
                        ClickSoundEnabled = _clickSoundManager.IsEnabled,
                        ClickSoundType = _clickSoundManager.SoundType.ToString(),
                        Volume = (int)VolumeSlider.Value,
                        NormalModeLeft = _normalModeLeft,
                        NormalModeTop = _normalModeTop,
                        StreamerModeLeft = _streamerModeLeft, // Aktuelle Streamer Mode Position
                        StreamerModeTop = _streamerModeTop,
                        ApmColorNormal = ApmColorNormalText?.Text ?? "FFFFFF",
                        ApmColorCommon = ApmColorCommonText?.Text ?? "00C8FF",
                        ApmColorUncommon = ApmColorUncommonText?.Text ?? "B400FF",
                        ApmColorRare = ApmColorRareText?.Text ?? "FF3232",
                        ApmColorEpic = ApmColorEpicText?.Text ?? "FFD700",
                        ApmColorLegendary = ApmColorLegendaryText?.Text ?? "FF0080"
                    };
                    SettingsManager.SaveSettings(settingsAfterPosition);
                }
                
                UpdateLayout();
                
                StreamerModeContainer.Visibility = Visibility.Visible;
                StreamerModeContainer.IsHitTestVisible = true;
                
                UpdateLayout();
                
                if (!double.IsNaN(_streamerModeLeft) && !double.IsNaN(_streamerModeTop))
                {
                    Left = _streamerModeLeft;
                    Top = _streamerModeTop;
                }
                
                _isSettingPosition = false;
                Topmost = true;
                WindowState = WindowState.Normal;
                Visibility = Visibility.Visible;
                Show();
                Activate();
                Focus();
                BringIntoView();
                
                Topmost = false;
                Topmost = true;
                
                UpdateLayout();
                InvalidateVisual();
                if (!_isTracking)
                {
                    StartTracking();
                }
            }
            else
            {
                if (StreamerModeContainer.Visibility == Visibility.Visible)
                {
                    _streamerModeLeft = Left;
                    _streamerModeTop = Top;
                    
                    var settings = new AppSettings
                    {
                        StreamerFont = _streamerFont,
                        ClickSoundEnabled = _clickSoundManager.IsEnabled,
                        ClickSoundType = _clickSoundManager.SoundType.ToString(),
                        Volume = (int)VolumeSlider.Value,
                        NormalModeLeft = _normalModeLeft,
                        NormalModeTop = _normalModeTop,
                        StreamerModeLeft = _streamerModeLeft, // Aktuelle Streamer Mode Position
                        StreamerModeTop = _streamerModeTop,
                        ApmColorNormal = ApmColorNormalText?.Text ?? "FFFFFF",
                        ApmColorCommon = ApmColorCommonText?.Text ?? "00C8FF",
                        ApmColorUncommon = ApmColorUncommonText?.Text ?? "B400FF",
                        ApmColorRare = ApmColorRareText?.Text ?? "FF3232",
                        ApmColorEpic = ApmColorEpicText?.Text ?? "FFD700",
                        ApmColorLegendary = ApmColorLegendaryText?.Text ?? "FF0080"
                    };
                    SettingsManager.SaveSettings(settings);
                }
                StreamerModeContainer.Visibility = Visibility.Collapsed;
                StreamerModeContainer.IsHitTestVisible = false;
                
                Width = NormalWidth;
                Height = NormalHeight;
                CenterWindowOnPrimaryScreen();
                
                UpdateLayout();
                
                NormalModeContainer.Visibility = Visibility.Visible;
                NormalModeContainer.IsHitTestVisible = true;
                
                UpdateLayout();
                InvalidateVisual();
            }
        }

        private bool IsPositionOnValidScreen(double left, double top)
        {
            try
            {
                var screens = Screen.AllScreens;
                foreach (var screen in screens)
                {
                    var bounds = screen.Bounds;
                    if (left >= bounds.Left && left <= bounds.Right && 
                        top >= bounds.Top && top <= bounds.Bottom)
                    {
                        return true;
                    }
                }
                return false;
            }
            catch
            {
                return true;
            }
        }

        private void CenterWindowOnPrimaryScreen()
        {
            try
            {
                var primaryScreen = Screen.PrimaryScreen;
                if (primaryScreen != null)
                {
                    var bounds = primaryScreen.Bounds;
                    var width = _isStreamerMode ? StreamerWidth : NormalWidth;
                    var height = _isStreamerMode ? StreamerHeight : NormalHeight;
                    Left = bounds.Left + (bounds.Width - width) / 2;
                    Top = bounds.Top + (bounds.Height - height) / 2;
                }
                else
                {
                    var screenWidth = SystemParameters.PrimaryScreenWidth;
                    var screenHeight = SystemParameters.PrimaryScreenHeight;
                    var width = _isStreamerMode ? StreamerWidth : NormalWidth;
                    var height = _isStreamerMode ? StreamerHeight : NormalHeight;
                    Left = (screenWidth - width) / 2;
                    Top = (screenHeight - height) / 2;
                }
            }
            catch
            {
                var screenWidth = SystemParameters.PrimaryScreenWidth;
                var screenHeight = SystemParameters.PrimaryScreenHeight;
                var width = _isStreamerMode ? StreamerWidth : NormalWidth;
                var height = _isStreamerMode ? StreamerHeight : NormalHeight;
                Left = (screenWidth - width) / 2;
                Top = (screenHeight - height) / 2;
            }
        }

        private void CenterWindowOnCurrentScreen()
        {
            try
            {
                var screens = Screen.AllScreens;
                var currentScreen = screens.FirstOrDefault(s => 
                {
                    var bounds = s.Bounds;
                    return Left >= bounds.Left && Left <= bounds.Right && 
                           Top >= bounds.Top && Top <= bounds.Bottom;
                }) ?? Screen.PrimaryScreen;
                
                if (currentScreen != null)
                {
                    var bounds = currentScreen.Bounds;
                    var width = _isStreamerMode ? StreamerWidth : NormalWidth;
                    var height = _isStreamerMode ? StreamerHeight : NormalHeight;
                    Left = bounds.Left + (bounds.Width - width) / 2;
                    Top = bounds.Top + (bounds.Height - height) / 2;
                }
                else
                {
                    CenterWindowOnPrimaryScreen();
                }
            }
            catch
            {
                CenterWindowOnPrimaryScreen();
            }
        }
        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 1)
            {
                DragMove();
            }
        }
        
        private void Window_LocationChanged(object? sender, EventArgs e)
        {
            if (_isInitialized && !_isSettingPosition)
            {
                if (_isStreamerMode)
                {
                    _streamerModeLeft = Left;
                    _streamerModeTop = Top;
                    
                    var settings = new AppSettings
                    {
                        StreamerFont = _streamerFont,
                        ClickSoundEnabled = _clickSoundManager.IsEnabled,
                        ClickSoundType = _clickSoundManager.SoundType.ToString(),
                        Volume = (int)VolumeSlider.Value,
                        NormalModeLeft = _normalModeLeft,
                        NormalModeTop = _normalModeTop,
                        StreamerModeLeft = _streamerModeLeft, // Aktuelle Streamer Mode Position
                        StreamerModeTop = _streamerModeTop,
                        ApmColorNormal = ApmColorNormalText?.Text ?? "FFFFFF",
                        ApmColorCommon = ApmColorCommonText?.Text ?? "00C8FF",
                        ApmColorUncommon = ApmColorUncommonText?.Text ?? "B400FF",
                        ApmColorRare = ApmColorRareText?.Text ?? "FF3232",
                        ApmColorEpic = ApmColorEpicText?.Text ?? "FFD700",
                        ApmColorLegendary = ApmColorLegendaryText?.Text ?? "FF0080"
                    };
                    SettingsManager.SaveSettings(settings);
                }
                else
                {
                    _normalModeLeft = Left;
                    _normalModeTop = Top;
                    SaveSettings(); // Normal Mode kann SaveSettings() verwenden
                }
            }
        }
        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            SaveSettings();
            _inputHook.Dispose();
            _clickSoundManager.Dispose();
            Close();
        }

        private async void UpdateButton_Click(object sender, RoutedEventArgs e)
        {
            await CheckForUpdatesAsync(true);
        }

        private async Task CheckForUpdatesAsync(bool manualCheck = false)
        {
            try
            {
                var updateInfo = await UpdateManager.CheckForUpdatesAsync();
                if (updateInfo != null)
                {
                    UpdateButton.Visibility = Visibility.Visible;
                    UpdateButton.ToolTip = $"Update available: v{updateInfo.Version}";
                    
                    if (manualCheck)
                    {
                        var result = System.Windows.MessageBox.Show(
                            $"A new version is available!\n\n" +
                            $"Current: v{UpdateManager.GetCurrentVersion()}\n" +
                            $"Latest: v{updateInfo.Version}\n\n" +
                            $"Would you like to download and install it now?",
                            "Update Available",
                            System.Windows.MessageBoxButton.YesNo,
                            System.Windows.MessageBoxImage.Information);

                        if (result == System.Windows.MessageBoxResult.Yes)
                        {
                            await InstallUpdateAsync(updateInfo);
                        }
                    }
                }
                else if (manualCheck)
                {
                    System.Windows.MessageBox.Show(
                        "You are using the latest version!",
                        "No Updates",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Information);
                }
            }
            catch
            {
                if (manualCheck)
                {
                    System.Windows.MessageBox.Show(
                        "Failed to check for updates. Please try again later.",
                        "Update Check Failed",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Warning);
                }
            }
        }

        private async Task InstallUpdateAsync(UpdateInfo updateInfo)
        {
            try
            {
                if (string.IsNullOrEmpty(updateInfo.DownloadUrl))
                {
                    System.Windows.MessageBox.Show(
                        "Download URL is missing. Please download manually from GitHub.",
                        "Update Failed",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Error);
                    return;
                }

                var result = System.Windows.MessageBox.Show(
                    $"Downloading update v{updateInfo.Version}...\n\n" +
                    "The application will close and restart after installation.",
                    "Installing Update",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);

                var success = await UpdateManager.DownloadAndInstallUpdateAsync(
                    updateInfo.DownloadUrl, 
                    updateInfo.Version);

                if (!success)
                {
                    System.Windows.MessageBox.Show(
                        $"Failed to install update.\n\n" +
                        $"Please download manually from:\n" +
                        $"https://github.com/justinjd00/APMTracker/releases/latest",
                        "Update Failed",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"Error installing update:\n\n{ex.Message}\n\n" +
                    $"Please download manually from:\n" +
                    $"https://github.com/justinjd00/APMTracker/releases/latest",
                    "Update Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
        }
        
        private void ClickSoundCheckbox_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            _clickSoundManager.IsEnabled = ClickSoundCheckbox.IsChecked ?? false;
        }
        private void ClickSoundComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (!_isInitialized) return;
            
            if (ClickSoundComboBox.SelectedItem is System.Windows.Controls.ComboBoxItem selectedItem)
            {
                var soundName = selectedItem.Tag?.ToString() ?? "WindowsDefault";
                if (Enum.TryParse<ClickSoundType>(soundName, out var soundType))
                {
                    _clickSoundManager.SoundType = soundType;
                    
                    if (_clickSoundManager.IsEnabled)
                    {
                        _clickSoundManager.PlayClick();
                    }
                }
            }
        }
        private void OpenSoundsFolder_Click(object sender, RoutedEventArgs e)
        {
            var folder = _clickSoundManager.GetSoundsFolder();
            
            if (!System.IO.Directory.Exists(folder))
            {
                System.IO.Directory.CreateDirectory(folder);
            }
            
            System.Diagnostics.Process.Start("explorer.exe", folder);
            
            System.Windows.MessageBox.Show(
                "Lege WAV oder MP3 Dateien in diesen Ordner!\n\n" +
                "Nach dem Hinzufügen: App neustarten oder\n" +
                "Sound-Checkbox aus/an schalten zum Aktualisieren.",
                "Sounds-Ordner", 
                System.Windows.MessageBoxButton.OK, 
                MessageBoxImage.Information);
            
            _clickSoundManager.RefreshCustomSounds();
            LoadSoundOptions();
        }
        private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_isInitialized) return;
            
            var volume = (float)(VolumeSlider.Value / 100.0);
            _clickSoundManager.Volume = volume;
            VolumeText.Text = $"{(int)VolumeSlider.Value}%";
        }
        private void FontComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (!_isInitialized) return; // Warten bis Fenster geladen
            
            if (FontComboBox.SelectedItem is System.Windows.Controls.ComboBoxItem selectedItem)
            {
                _streamerFont = selectedItem.Tag?.ToString() ?? "pack://application:,,,/Fonts/#Orbitron";
                ApplyStreamerFont();
            }
        }
        private void ApplyStreamerFont()
        {
            if (StreamerLiveApmText == null) return; // Sicherheitscheck
            
            var fontFamily = new FontFamily(_streamerFont);
            
            StreamerLiveApmText.FontFamily = fontFamily;
            StreamerLiveApmShadow.FontFamily = fontFamily;
            StreamerLiveApmShadow2.FontFamily = fontFamily;
            StreamerLiveApmShadow3.FontFamily = fontFamily;
            StreamerLiveApmShadow4.FontFamily = fontFamily;
            StreamerLiveApmShadow5.FontFamily = fontFamily;
            StreamerLiveApmOutline1.FontFamily = fontFamily;
            StreamerLiveApmOutline2.FontFamily = fontFamily;
            StreamerLiveApmOutline3.FontFamily = fontFamily;
            StreamerLiveApmOutline4.FontFamily = fontFamily;
            StreamerLiveApmOutline5.FontFamily = fontFamily;
            StreamerLiveApmOutline6.FontFamily = fontFamily;
            StreamerLiveApmOutline7.FontFamily = fontFamily;
            StreamerLiveApmOutline8.FontFamily = fontFamily;
            
            StreamerApmLabelText.FontFamily = fontFamily;
            StreamerApmLabelShadow1.FontFamily = fontFamily;
            StreamerApmLabelShadow2.FontFamily = fontFamily;
            StreamerApmLabelShadow3.FontFamily = fontFamily;
            StreamerApmLabelShadow4.FontFamily = fontFamily;
            StreamerApmLabelShadow5.FontFamily = fontFamily;
            StreamerApmLabelOutline1.FontFamily = fontFamily;
            StreamerApmLabelOutline2.FontFamily = fontFamily;
            StreamerApmLabelOutline3.FontFamily = fontFamily;
            StreamerApmLabelOutline4.FontFamily = fontFamily;
            StreamerApmLabelOutline5.FontFamily = fontFamily;
            StreamerApmLabelOutline6.FontFamily = fontFamily;
            StreamerApmLabelOutline7.FontFamily = fontFamily;
            StreamerApmLabelOutline8.FontFamily = fontFamily;
        }
        protected override void OnClosed(EventArgs e)
        {
            SaveSettings();
            _inputHook.Dispose();
            _clickSoundManager.Dispose();
            base.OnClosed(e);
        }
    }
}
