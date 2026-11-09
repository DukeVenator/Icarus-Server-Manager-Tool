using IcarusServerManager.Models;
using IcarusServerManager.Services;
using Xunit;

namespace IcarusServerManager.Tests;

/// <summary>
/// Scheduling rules with an explicit clock: the restart becomes due at the configured time, stays
/// due through the grace window so a delayed tick still fires, and never repeats an occurrence.
/// </summary>
public sealed class ScheduledRestartPolicyTests
{
    private static ManagerOptions Options(string time = "04:00", bool enabled = true) =>
        new() { UpdateScheduleEnabled = enabled, UpdateScheduleTime = time };

    [Fact]
    public void DisabledSchedule_IsNeverDue()
    {
        var policy = new ScheduledRestartPolicy();
        Assert.False(policy.IsDue(Options(enabled: false), new DateTime(2026, 4, 15, 4, 0, 0), out _));
    }

    [Fact]
    public void BeforeConfiguredTime_IsNotDue()
    {
        var policy = new ScheduledRestartPolicy();
        Assert.False(policy.IsDue(Options(), new DateTime(2026, 4, 15, 3, 59, 59), out _));
    }

    [Fact]
    public void AtConfiguredTime_IsDueForThatOccurrence()
    {
        var policy = new ScheduledRestartPolicy();
        var now = new DateTime(2026, 4, 15, 4, 0, 0);

        Assert.True(policy.IsDue(Options(), now, out var occurrence));
        Assert.Equal(new DateTime(2026, 4, 15, 4, 0, 0), occurrence);
    }

    [Fact]
    public void InsideGraceWindow_IsStillDue()
    {
        var policy = new ScheduledRestartPolicy();
        var late = new DateTime(2026, 4, 15, 4, 14, 59);
        Assert.True(policy.IsDue(Options(), late, out _));
    }

    [Fact]
    public void PastGraceWindow_IsNoLongerDue()
    {
        var policy = new ScheduledRestartPolicy();
        var tooLate = new DateTime(2026, 4, 15, 4, 15, 0);
        Assert.False(policy.IsDue(Options(), tooLate, out _));
    }

    [Fact]
    public void ConsumedOccurrence_DoesNotRepeat_ButNextDayDoes()
    {
        var policy = new ScheduledRestartPolicy();
        var today = new DateTime(2026, 4, 15, 4, 0, 0);
        Assert.True(policy.IsDue(Options(), today, out var occurrence));
        policy.MarkConsumed(occurrence);

        Assert.True(policy.WasConsumed(occurrence));
        Assert.False(policy.IsDue(Options(), today.AddMinutes(1), out _));

        var tomorrow = today.AddDays(1);
        Assert.True(policy.IsDue(Options(), tomorrow, out var nextOccurrence));
        Assert.NotEqual(occurrence, nextOccurrence);
    }

    [Fact]
    public void FailedAction_ConsumesTheOccurrence_SoItDoesNotLoop()
    {
        // The form marks the occurrence before dispatching, so a failed restart is not retried every
        // tick for the rest of the grace window.
        var policy = new ScheduledRestartPolicy();
        var now = new DateTime(2026, 4, 15, 4, 0, 0);
        Assert.True(policy.IsDue(Options(), now, out var occurrence));
        policy.MarkConsumed(occurrence);

        Assert.False(policy.IsDue(Options(), now.AddSeconds(30), out _));
    }

    [Theory]
    [InlineData("04:00", true)]
    [InlineData("4:00", true)]
    [InlineData("23:59", true)]
    [InlineData("24:00", false)]
    [InlineData("not-a-time", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void ParsesOnlyRealTimesOfDay(string? text, bool expected)
    {
        Assert.Equal(expected, ScheduledRestartPolicy.TryParseSchedule(text, out _));
    }

    [Fact]
    public void InvalidStoredTime_IsNeverDue()
    {
        var policy = new ScheduledRestartPolicy();
        Assert.False(policy.IsDue(Options("not-a-time"), new DateTime(2026, 4, 15, 4, 0, 0), out _));
    }
}
