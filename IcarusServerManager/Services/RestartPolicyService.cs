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
            return new RestartDecision();
        }

        if (options.IntervalRestartEnabled)
        {
            var nextRestart = startTime.AddMinutes(options.IntervalRestartMinutes);
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

        if (options.EmptyServerRestartEnabled)
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
        }

        return new RestartDecision();
    }

    public int GetCrashDelaySeconds(ManagerOptions options) => Math.Max(1, options.CrashRestartRetryDelaySeconds);
    public void ResetCrashAttempts() => _crashAttempts = 0;
}
