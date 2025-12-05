using System;
using System.Collections.Generic;
using System.Linq;

namespace ApmTracker
{
    public class ApmCalculator
    {
        private readonly object _lock = new();
        
        private const int WindowSize = 60;
        private readonly int[] _actionsPerSecond = new int[WindowSize];
        private int _rollingActionCount = 0;
        private int _totalSeconds = 0;
        private DateTime _lastSecondIncrement = DateTime.Now;
        
        // Verbesserte EMA-Glättung mit adaptivem Alpha
        private double _emaApm = 0.0;
        private const double EmaAlpha = 0.15; // Schnellere Reaktion als vorher (0.02)
        private DateTime? _lastActionTime = null;
        
        // Quantisierung für stabile Anzeige
        private int _quantizedApm = 0;
        private double _targetApm = 0.0;
        private DateTime? _lastStepTime = null;
        
        // APM-Historie für spätere Graphen (letzte 10 Minuten, alle 5 Sekunden)
        private const int HistorySize = 120; // 10 Minuten * 60 Sekunden / 5 Sekunden
        private readonly Queue<(DateTime timestamp, double apm)> _apmHistory = new();
        
        // Zusätzliche Statistiken
        private int _minApm = int.MaxValue;
        private readonly List<int> _recentApmValues = new(); // Für verschiedene Zeitfenster

        public int TotalActions { get; private set; }
        public int KeyboardActions { get; private set; }
        public int MouseActions { get; private set; }
        public int PeakApm { get; private set; }
        public int MinApm => _minApm == int.MaxValue ? 0 : _minApm;
        public DateTime? SessionStart { get; private set; }
        
        // APM-Historie für Export/Graphen
        public IReadOnlyList<(DateTime timestamp, double apm)> GetApmHistory()
        {
            lock (_lock)
            {
                return _apmHistory.ToList();
            }
        }
        
        public void UpdatePeakApm(int currentApm)
        {
            if (currentApm > PeakApm)
            {
                lock (_lock)
                {
                    if (currentApm > PeakApm)
                    {
                        PeakApm = currentApm;
                    }
                }
            }
        }

        public void RecordAction(InputType type)
        {
            lock (_lock)
            {
                var now = DateTime.Now;
                IncrementSecondsIfNeeded(now);
                
                int currentSecond = _totalSeconds % WindowSize;
                _actionsPerSecond[currentSecond]++;
                _rollingActionCount++;
                _lastActionTime = now;
                TotalActions++;

                switch (type)
                {
                    case InputType.Keyboard:
                        KeyboardActions++;
                        break;
                    case InputType.MouseLeft:
                    case InputType.MouseRight:
                    case InputType.MouseMiddle:
                    case InputType.MouseExtra:
                    case InputType.MouseWheel:
                        MouseActions++;
                        break;
                }

                SessionStart ??= now;
            }
        }

        private void IncrementSecondsIfNeeded(DateTime now)
        {
            var secondsSinceLastIncrement = (now - _lastSecondIncrement).TotalSeconds;
            
            if (secondsSinceLastIncrement >= 1.0)
            {
                int secondsToIncrement = (int)Math.Floor(secondsSinceLastIncrement);
                
                for (int i = 0; i < secondsToIncrement; i++)
                {
                    _totalSeconds++;
                    int currentSecond = _totalSeconds % WindowSize;
                    _rollingActionCount -= _actionsPerSecond[currentSecond];
                    _actionsPerSecond[currentSecond] = 0;
                }
                
                _lastSecondIncrement = now;
            }
        }

        public int CalculateCurrentApm()
        {
            lock (_lock)
            {
                var now = DateTime.Now;
                IncrementSecondsIfNeeded(now);
                
                // Raw APM berechnen (60-Sekunden-Fenster)
                double rawApm = 0;
                
                if (_totalSeconds == 0)
                {
                    rawApm = 0;
                }
                else if (_totalSeconds < WindowSize)
                {
                    // Extrapolation für frühe Sekunden
                    rawApm = _rollingActionCount * (WindowSize / (double)_totalSeconds);
                }
                else
                {
                    // Exakte 60-Sekunden-Berechnung
                    rawApm = _rollingActionCount;
                }
                
                // Verbesserte EMA-Glättung (Exponential Moving Average)
                double secondsSinceLastAction = _lastActionTime.HasValue 
                    ? (now - _lastActionTime.Value).TotalSeconds 
                    : double.MaxValue;
                
                if (_emaApm == 0.0)
                {
                    // Initialisierung
                    _emaApm = rawApm;
                    _targetApm = rawApm;
                }
                else
                {
                    if (secondsSinceLastAction > 3.0)
                    {
                        // Decay bei Inaktivität (schnellerer Decay)
                        double decayFactor = secondsSinceLastAction > 6.0 ? 0.90 : 0.94;
                        _emaApm = Math.Max(0, _emaApm * decayFactor);
                    }
                    else
                    {
                        // EMA-Update: EMA = alpha * newValue + (1 - alpha) * oldEMA
                        // Höheres Alpha = schnellere Reaktion
                        _emaApm = EmaAlpha * rawApm + (1.0 - EmaAlpha) * _emaApm;
                    }
                    _targetApm = _emaApm;
                }
                
                // APM-Historie aktualisieren (alle 5 Sekunden)
                if (_apmHistory.Count == 0 || (now - _apmHistory.Last().timestamp).TotalSeconds >= 5.0)
                {
                    _apmHistory.Enqueue((now, _emaApm));
                    if (_apmHistory.Count > HistorySize)
                    {
                        _apmHistory.Dequeue();
                    }
                }
                
                // Min APM aktualisieren
                int currentApmInt = (int)Math.Round(_emaApm);
                if (currentApmInt > 0 && currentApmInt < _minApm)
                {
                    _minApm = currentApmInt;
                }
                
                // Quantisierung für stabile Anzeige (optimiert)
                int stepSize = CalculateStepSize(_quantizedApm);
                int targetQuantized = (int)Math.Round(_targetApm / stepSize) * stepSize;
                
                int difference = targetQuantized - _quantizedApm;
                
                if (difference != 0)
                {
                    // Schnellere Reaktion: Kürzere Delays
                    int stepDelay = Math.Abs(difference) > stepSize * 2 
                        ? (difference > 0 ? 50 : 100)  // Große Sprünge schneller
                        : (difference > 0 ? 80 : 150);   // Kleine Änderungen etwas langsamer
                    
                    if (!_lastStepTime.HasValue || (now - _lastStepTime.Value).TotalMilliseconds >= stepDelay)
                    {
                        if (difference > 0)
                        {
                            _quantizedApm = Math.Min(targetQuantized, _quantizedApm + stepSize);
                        }
                        else
                        {
                            _quantizedApm = Math.Max(0, _quantizedApm - stepSize);
                        }
                        _lastStepTime = now;
                    }
                }
                else
                {
                    _lastStepTime = null;
                }
                
                return _quantizedApm;
            }
        }

        public double CalculateAverageApm()
        {
            lock (_lock)
            {
                if (SessionStart == null || TotalActions == 0)
                    return 0;

                var elapsed = DateTime.Now - SessionStart.Value;
                if (elapsed.TotalMinutes < 0.1)
                    return TotalActions;

                return TotalActions / elapsed.TotalMinutes;
            }
        }
        
        // APM für verschiedene Zeitfenster
        public double CalculateApmForWindow(int seconds)
        {
            lock (_lock)
            {
                if (_apmHistory.Count == 0)
                    return 0;
                
                var cutoffTime = DateTime.Now.AddSeconds(-seconds);
                var relevantEntries = _apmHistory.Where(h => h.timestamp >= cutoffTime).ToList();
                
                if (relevantEntries.Count == 0)
                    return 0;
                
                return relevantEntries.Average(h => h.apm);
            }
        }
        
        // 1-Minuten APM
        public double CalculateApm1Min() => CalculateApmForWindow(60);
        
        // 5-Minuten APM
        public double CalculateApm5Min() => CalculateApmForWindow(300);

        public TimeSpan GetSessionDuration()
        {
            if (SessionStart == null)
                return TimeSpan.Zero;

            return DateTime.Now - SessionStart.Value;
        }

        private int CalculateStepSize(int currentApm)
        {
            if (currentApm < 50) return 1;
            if (currentApm < 100) return 1;
            if (currentApm < 200) return 2;
            if (currentApm < 300) return 3;
            if (currentApm < 400) return 4;
            return 5;
        }

        public void Reset()
        {
            lock (_lock)
            {
                Array.Clear(_actionsPerSecond, 0, WindowSize);
                _rollingActionCount = 0;
                _totalSeconds = 0;
                _lastSecondIncrement = DateTime.Now;
                
                TotalActions = 0;
                KeyboardActions = 0;
                MouseActions = 0;
                PeakApm = 0;
                _minApm = int.MaxValue;
                SessionStart = null;
                _emaApm = 0.0;
                _lastActionTime = null;
                _quantizedApm = 0;
                _targetApm = 0.0;
                _lastStepTime = null;
                _apmHistory.Clear();
                _recentApmValues.Clear();
            }
        }
    }
}
