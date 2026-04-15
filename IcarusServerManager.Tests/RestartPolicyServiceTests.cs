using IcarusServerManager.Models;
using IcarusServerManager.Services;
using Xunit;

namespace IcarusServerManager.Tests;

public sealed class RestartPolicyServiceTests
{
    [Fact]
    public void Evaluate_ReturnsNothing_WhenServerNotStarted_AndNotCrashed()
    {
        var service = new RestartPolicyService();
        var options = new ManagerOptions { IntervalRestartEnabled = true, IntervalRestartMinutes = 60 };
        var start = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var decision = service.Evaluate(options, false, start, start.AddHours(2), false, 100, false);
        Assert.False(decision.ShouldWarn);
        Assert.False(decision.ShouldRestart);
    }

    [Fact]
    public void Evaluate_ReturnsNothing_WhenAllPoliciesDisabled()
    {
        var service = new RestartPolicyService();
        var options = new ManagerOptions
        {
            IntervalRestartEnabled = false,
            HighMemoryRestartEnabled = false,
            EmptyServerRestartEnabled = false
        };
        var start = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var decision = service.Evaluate(options, true, start, start.AddDays(1), false, 50_000, true);
        Assert.False(decision.ShouldWarn);
        Assert.False(decision.ShouldRestart);
    }

    [Fact]
    public void Evaluate_ReturnsIntervalWarning_BeforeRestart()
    {
        var service = new RestartPolicyService();
        var options = new ManagerOptions
        {
            IntervalRestartEnabled = true,
            IntervalRestartMinutes = 60,
            IntervalWarningMinutes = 5
        };

        var start = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var now = start.AddMinutes(55);

        var decision = service.Evaluate(options, true, start, now, false, 500, false);
        Assert.True(decision.ShouldWarn);
        Assert.False(decision.ShouldRestart);
    }

    [Fact]
    public void Evaluate_ReturnsIntervalRestart_WhenPastDue()
    {
        var service = new RestartPolicyService();
        var options = new ManagerOptions
        {
            IntervalRestartEnabled = true,
            IntervalRestartMinutes = 60,
            IntervalWarningMinutes = 5
        };

        var start = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var now = start.AddMinutes(61);

        var decision = service.Evaluate(options, true, start, now, false, 500, false);
        Assert.True(decision.ShouldRestart);
        Assert.Contains("Interval", decision.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Evaluate_ReturnsHighMemoryRestart_AfterSustainWindow()
    {
        var service = new RestartPolicyService();
        var options = new ManagerOptions
        {
            IntervalRestartEnabled = false,
            HighMemoryRestartEnabled = true,
            HighMemoryMbThreshold = 1000,
            HighMemorySustainMinutes = 2,
            HighMemoryWarningMinutes = 1
        };
        var start = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);

        _ = service.Evaluate(options, true, start, start, false, 1200, false);
        var decision = service.Evaluate(options, true, start, start.AddMinutes(3), false, 1200, false);

        Assert.True(decision.ShouldRestart);
        Assert.Contains("High memory", decision.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Evaluate_ReturnsHighMemoryWarning_BeforeSustainThreshold()
    {
        var service = new RestartPolicyService();
        var options = new ManagerOptions
        {
            IntervalRestartEnabled = false,
            HighMemoryRestartEnabled = true,
            HighMemoryMbThreshold = 1000,
            HighMemorySustainMinutes = 5,
            HighMemoryWarningMinutes = 2
        };
        var start = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var t0 = start;
        _ = service.Evaluate(options, true, start, t0, false, 2000, false);
        var decision = service.Evaluate(options, true, start, t0.AddMinutes(3).AddSeconds(30), false, 2000, false);
        Assert.True(decision.ShouldWarn);
        Assert.False(decision.ShouldRestart);
    }

    [Fact]
    public void Evaluate_ReturnsEmptyServerWarning_BeforeRestart()
    {
        var service = new RestartPolicyService();
        var options = new ManagerOptions
        {
            IntervalRestartEnabled = false,
            EmptyServerRestartEnabled = true,
            EmptyServerRestartMinutes = 30,
            EmptyServerWarningMinutes = 5
        };
        var start = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        _ = service.Evaluate(options, true, start, start, false, 100, true);
        var decision = service.Evaluate(options, true, start, start.AddMinutes(26), false, 100, true);
        Assert.True(decision.ShouldWarn);
        Assert.False(decision.ShouldRestart);
    }

    [Fact]
    public void Evaluate_ReturnsEmptyServerRestart_WhenSustained()
    {
        var service = new RestartPolicyService();
        var options = new ManagerOptions
        {
            IntervalRestartEnabled = false,
            EmptyServerRestartEnabled = true,
            EmptyServerRestartMinutes = 30,
            EmptyServerWarningMinutes = 5
        };
        var start = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        _ = service.Evaluate(options, true, start, start, false, 100, true);
        var decision = service.Evaluate(options, true, start, start.AddMinutes(31), false, 100, true);
        Assert.True(decision.ShouldRestart);
        Assert.Contains("Empty", decision.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Evaluate_StopsCrashRestart_AfterMaxAttempts()
    {
        var service = new RestartPolicyService();
        var options = new ManagerOptions { CrashRestartEnabled = true, CrashRestartMaxAttempts = 2 };
        var start = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        Assert.True(service.Evaluate(options, false, start, start, true, 0, false).ShouldRestart);
        Assert.True(service.Evaluate(options, false, start, start, true, 0, false).ShouldRestart);
        var third = service.Evaluate(options, false, start, start, true, 0, false);
        Assert.False(third.ShouldRestart);
    }

    [Fact]
    public void GetCrashDelaySeconds_ClampsToMinimumOne()
    {
        var service = new RestartPolicyService();
        var options = new ManagerOptions { CrashRestartRetryDelaySeconds = 0 };
        Assert.Equal(1, service.GetCrashDelaySeconds(options));
    }
}
