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
        
        private double _smoothedApm = 0.0;
        private const double SmoothingFactor = 0.02;
        private DateTime? _lastActionTime = null;
        
        private int _quantizedApm = 0;
        private double _targetApm = 0.0;
        private DateTime? _lastFallStepTime = null;

        public int TotalActions { get; private set; }
        public int KeyboardActions { get; private set; }
        public int MouseActions { get; private set; }
        public int PeakApm { get; private set; }
        public DateTime? SessionStart { get; private set; }
        
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
                
                double rawApm = 0;
                
                if (_totalSeconds == 0)
                {
                    rawApm = 0;
                }
                else if (_totalSeconds < WindowSize)
                {
                    rawApm = _rollingActionCount * (WindowSize / (double)_totalSeconds);
                }
                else
                {
                    rawApm = _rollingActionCount;
                }
                
                double secondsSinceLastAction = _lastActionTime.HasValue 
                    ? (now - _lastActionTime.Value).TotalSeconds 
                    : double.MaxValue;
                
                if (_smoothedApm == 0.0)
                {
                    _smoothedApm = rawApm * 0.1;
                    _targetApm = _smoothedApm;
                }
                else
                {
                    if (secondsSinceLastAction > 3.0)
                    {
                        double decayFactor = secondsSinceLastAction > 6.0 ? 0.92 : 0.96;
                        _smoothedApm = Math.Max(0, _smoothedApm * decayFactor);
                    }
                    else
                    {
                        _smoothedApm = _smoothedApm * (1.0 - SmoothingFactor) + rawApm * SmoothingFactor;
                    }
                    _targetApm = _smoothedApm;
                }
                
                int stepSize = CalculateStepSize(_quantizedApm);
                int targetQuantized = (int)Math.Round(_targetApm / stepSize) * stepSize;
                
                if (targetQuantized > _quantizedApm)
                {
                    _quantizedApm += stepSize;
                }
                else if (targetQuantized < _quantizedApm)
                {
                    if (!_lastFallStepTime.HasValue || (now - _lastFallStepTime.Value).TotalMilliseconds >= 300)
                    {
                        _quantizedApm = Math.Max(0, _quantizedApm - stepSize);
                        _lastFallStepTime = now;
                    }
                }
                else
                {
                    _lastFallStepTime = null;
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

        public TimeSpan GetSessionDuration()
        {
            if (SessionStart == null)
                return TimeSpan.Zero;

            return DateTime.Now - SessionStart.Value;
        }

        private int CalculateStepSize(int currentApm)
        {
            if (currentApm < 50) return 1;
            if (currentApm < 100) return 2;
            if (currentApm < 200) return 4;
            if (currentApm < 300) return 6;
            if (currentApm < 400) return 8;
            return 10;
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
                SessionStart = null;
                _smoothedApm = 0.0;
                _lastActionTime = null;
                _quantizedApm = 0;
                _targetApm = 0.0;
                _lastFallStepTime = null;
            }
        }
    }
}
