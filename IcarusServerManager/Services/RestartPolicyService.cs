using IcarusServerManager.Models;

namespace IcarusServerManager.Services;

internal sealed class RestartDecision
{
    public bool ShouldRestart { get; init; }
    public bool ShouldWarn { get; init; }
    public string Reason { get; init; } = string.Empty;
}

internal sealed class RestartPolicyService
{
    private DateTime _nextIntervalWarningAt = DateTime.MinValue;
    private int _crashAttempts;
    private DateTime _highMemorySince = DateTime.MinValue;
    private DateTime _emptySince = DateTime.MinValue;
    private DateTime _intervalPauseStarted = DateTime.MinValue;
    private TimeSpan _intervalPausedTotal = TimeSpan.Zero;

    public RestartDecision Evaluate(
        ManagerOptions options,
        bool serverStarted,
        DateTime startTime,
        DateTime now,
        bool crashed,
        double memoryMb,
        bool maybeEmptyFromLogs)
    {
        if (crashed && options.CrashRestartEnabled)
        {
            _crashAttempts++;
            if (_crashAttempts <= options.CrashRestartMaxAttempts)
            {
                return new RestartDecision { ShouldRestart = true, Reason = $"Crash policy triggered restart attempt {_crashAttempts}." };
            }
        }

        if (!serverStarted)
        {
            _emptySince = DateTime.MinValue;
            _highMemorySince = DateTime.MinValue;
            _intervalPauseStarted = DateTime.MinValue;
            _intervalPausedTotal = TimeSpan.Zero;
            _nextIntervalWarningAt = DateTime.MinValue;
            return new RestartDecision();
        }

        var usingEmptyTimerForPausedInterval = false;
        var intervalPausedBecauseEmpty = false;
        if (options.IntervalRestartEnabled)
        {
            if (options.PauseIntervalRestartWhenEmpty && maybeEmptyFromLogs)
            {
                intervalPausedBecauseEmpty = true;
                if (_intervalPauseStarted == DateTime.MinValue)
                {
                    _intervalPauseStarted = now;
                }

                if (options.IntervalRestartUseEmptyIdleTimer && options.EmptyServerRestartEnabled)
                {
                    usingEmptyTimerForPausedInterval = true;
                    var decision = EvaluateEmptyServerPolicy(options, now, maybeEmptyFromLogs);
                    if (decision.ShouldWarn || decision.ShouldRestart)
                    {
                        return new RestartDecision
                        {
                            ShouldWarn = decision.ShouldWarn,
                            ShouldRestart = decision.ShouldRestart,
                            Reason = $"Interval paused while empty: {decision.Reason}"
                        };
                    }
                }
            }
            else
            {
                if (_intervalPauseStarted != DateTime.MinValue)
                {
                    _intervalPausedTotal += now - _intervalPauseStarted;
                    _intervalPauseStarted = DateTime.MinValue;
                }
            }

            if (!intervalPausedBecauseEmpty)
            {
                var nextRestart = startTime
                    .Add(_intervalPausedTotal)
                    .AddMinutes(options.IntervalRestartMinutes);
                var warningAt = nextRestart.AddMinutes(-Math.Abs(options.IntervalWarningMinutes));
                if (now >= warningAt && now < nextRestart && _nextIntervalWarningAt != warningAt)
                {
                    _nextIntervalWarningAt = warningAt;
                    return new RestartDecision
                    {
                        ShouldWarn = true,
                        Reason = $"Interval restart warning: restart in {Math.Max(0, (int)(nextRestart - now).TotalMinutes)} minute(s)."
                    };
                }

                if (now >= nextRestart)
                {
                    _nextIntervalWarningAt = DateTime.MinValue;
                    return new RestartDecision { ShouldRestart = true, Reason = "Interval policy triggered restart." };
                }
            }
        }
        else
        {
            _intervalPauseStarted = DateTime.MinValue;
            _intervalPausedTotal = TimeSpan.Zero;
        }

        if (options.HighMemoryRestartEnabled)
        {
            if (memoryMb >= options.HighMemoryMbThreshold)
            {
                if (_highMemorySince == DateTime.MinValue)
                {
                    _highMemorySince = now;
                }

                var thresholdAt = _highMemorySince.AddMinutes(options.HighMemorySustainMinutes);
                var warnAt = thresholdAt.AddMinutes(-Math.Abs(options.HighMemoryWarningMinutes));
                if (now >= warnAt && now < thresholdAt)
                {
                    return new RestartDecision { ShouldWarn = true, Reason = "High memory warning threshold approaching." };
                }

                if (now >= thresholdAt)
                {
                    _highMemorySince = DateTime.MinValue;
                    return new RestartDecision { ShouldRestart = true, Reason = "High memory policy triggered restart." };
                }
            }
            else
            {
                _highMemorySince = DateTime.MinValue;
            }
        }

        if (options.EmptyServerRestartEnabled && !usingEmptyTimerForPausedInterval)
        {
            var decision = EvaluateEmptyServerPolicy(options, now, maybeEmptyFromLogs);
            if (decision.ShouldWarn || decision.ShouldRestart)
            {
                return decision;
            }
        }

        return new RestartDecision();
    }

    private RestartDecision EvaluateEmptyServerPolicy(ManagerOptions options, DateTime now, bool maybeEmptyFromLogs)
    {
        if (maybeEmptyFromLogs)
        {
            if (_emptySince == DateTime.MinValue)
            {
                _emptySince = now;
            }

            var restartAt = _emptySince.AddMinutes(options.EmptyServerRestartMinutes);
            var warnAt = restartAt.AddMinutes(-Math.Abs(options.EmptyServerWarningMinutes));
            if (now >= warnAt && now < restartAt)
            {
                return new RestartDecision { ShouldWarn = true, Reason = "Empty server warning threshold approaching." };
            }

            if (now >= restartAt)
            {
                _emptySince = DateTime.MinValue;
                return new RestartDecision { ShouldRestart = true, Reason = "Empty server policy triggered restart." };
            }
        }
        else
        {
            _emptySince = DateTime.MinValue;
        }

        return new RestartDecision();
    }

    public int GetCrashDelaySeconds(ManagerOptions options) => Math.Max(1, options.CrashRestartRetryDelaySeconds);
    public void ResetCrashAttempts() => _crashAttempts = 0;
}
