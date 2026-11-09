using IcarusServerManager.Models;

namespace IcarusServerManager.Services;

/// <summary>
/// Explicit outcome of the crash-restart budget. Callers previously had to infer intent from
/// <see cref="RestartDecision.ShouldRestart"/> alone, which made "budget left", "a retry is already
/// pending" and "budget used up" indistinguishable.
/// </summary>
internal enum CrashRestartState
{
    /// <summary>No crash was part of this evaluation.</summary>
    NotCrashed = 0,

    /// <summary>The crash budget has room and no retry has been authorized for this crash yet.</summary>
    StartAllowed,

    /// <summary>
    /// The crash budget has room, but a restart was already authorized for this crash episode and
    /// the replacement has not reported back yet. The caller still wants a retry, but must not
    /// start a second process: use the lifecycle guard to serialize the launch.
    /// </summary>
    RetryPending,

    /// <summary>The configured crash-attempt budget is used up; the server is left stopped.</summary>
    Exhausted
}

internal sealed class RestartDecision
{
    public bool ShouldRestart { get; init; }
    public bool ShouldWarn { get; init; }
    public string Reason { get; init; } = string.Empty;

    /// <summary>Crash-budget outcome for this evaluation; see <see cref="CrashRestartState"/>.</summary>
    public CrashRestartState CrashState { get; init; } = CrashRestartState.NotCrashed;
}

internal sealed class RestartPolicyService
{
    /// <summary>
    /// PROVISIONAL: ten minutes is an engineering placeholder for "a run was stable". Product has
    /// not approved this value, so it must not be treated as a support contract or exposed as a
    /// user-facing promise until that decision is recorded.
    /// </summary>
    private const int StableRunMinutes = 10;

    /// <summary>Uptime that must pass, without a crash, before crash attempts are cleared.</summary>
    public static readonly TimeSpan StableRunInterval = TimeSpan.FromMinutes(StableRunMinutes);

    private DateTime _nextIntervalWarningAt = DateTime.MinValue;
    private int _crashAttempts;
    private bool _crashRestartPending;
    private DateTime _highMemorySince = DateTime.MinValue;
    private DateTime _emptySince = DateTime.MinValue;
    private DateTime _intervalPauseStarted = DateTime.MinValue;
    private TimeSpan _intervalPausedTotal = TimeSpan.Zero;

    public int CrashAttempts => _crashAttempts;

    public RestartDecision Evaluate(
        ManagerOptions options,
        bool serverStarted,
        DateTime startTime,
        DateTime now,
        bool crashed,
        double memoryMb,
        bool maybeEmptyFromLogs)
    {
        // Only a successful, non-crashed run may clear the crash history. Clearing it for a crashed
        // evaluation (even after long uptime) erased the evidence of repeated crashes and refilled
        // the retry budget during a crash loop.
        var stableRun = serverStarted && !crashed && now - startTime >= StableRunInterval;
        if (stableRun)
        {
            _crashAttempts = 0;
        }

        if (!crashed)
        {
            // The crash episode is over. A later crash is a new decision and gets a fresh start
            // grant; the attempt history itself is only cleared by a stable run above.
            _crashRestartPending = false;
        }

        if (crashed && options.CrashRestartEnabled)
        {
            _crashAttempts++;
            if (_crashAttempts <= options.CrashRestartMaxAttempts)
            {
                var state = _crashRestartPending ? CrashRestartState.RetryPending : CrashRestartState.StartAllowed;
                _crashRestartPending = true;
                return new RestartDecision
                {
                    // ShouldRestart stays true for a pending retry so callers that only look at the
                    // booleans keep asking for a replacement; the lifecycle guard prevents a second
                    // launch while the first retry is unresolved.
                    ShouldRestart = true,
                    CrashState = state,
                    Reason = state == CrashRestartState.StartAllowed
                        ? $"Crash policy triggered restart attempt {_crashAttempts}."
                        : $"Crash policy retry pending for restart attempt {_crashAttempts}."
                };
            }

            return new RestartDecision
            {
                CrashState = CrashRestartState.Exhausted,
                Reason = $"Crash policy exhausted after {_crashAttempts - 1} restart attempt(s); the server is left stopped."
            };
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

    /// <summary>
    /// Force-clears crash history. Not part of normal launch flow: only an explicit operator action
    /// (or a future product-approved reset) should call this, never the start path.
    /// </summary>
    public void ResetCrashAttempts()
    {
        _crashAttempts = 0;
        _crashRestartPending = false;
    }
}
