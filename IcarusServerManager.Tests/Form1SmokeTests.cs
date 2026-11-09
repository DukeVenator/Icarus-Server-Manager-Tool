using System.Reflection;
using System.Windows.Forms;
using IcarusServerManager;
using IcarusServerManager.Properties;
using Xunit;

namespace IcarusServerManager.Tests;

/// <summary>
/// These tests load real application settings, profiles and timers. They are quarantined until
/// those dependencies can be redirected before form construction. Saving and restoring a few
/// preference values is not isolation and cannot protect against timeouts or process termination.
/// </summary>
[Collection("ManagerSettingsSideEffects")]
public sealed class Form1SmokeTests
{
    /// <summary>
    /// WinForms requires STA; xUnit defaults are not STA. Catches ctor / InitializeComponent regressions before Form1 splits.
    /// </summary>
    [Fact(Skip = "Requires isolated settings, profile storage, timers and process execution; do not run against the developer environment.")]
    public void Form1_can_be_constructed_and_disposed_on_sta_thread()
    {
        Exception? caught = null;
        var thread = new Thread(() =>
        {
            try
            {
                ApplicationConfiguration.Initialize();
                using var form = new Form1();
                form.ShowInTaskbar = false;
            }
            catch (Exception ex)
            {
                caught = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(60_000), "STA thread did not complete within timeout.");
        Assert.Null(caught);
    }

    /// <summary>
    /// Exercises moved partials (UI build, persistence, timers, theme) via the real <see cref="Form.Load"/> path.
    /// </summary>
    [Fact(Skip = "Requires isolated settings, profile storage, timers and process execution; do not run against the developer environment.")]
    public void Form1_Load_completes_on_sta_thread()
    {
        Exception? caught = null;
        var thread = new Thread(() =>
        {
            try
            {
                ApplicationConfiguration.Initialize();
                // Avoid Welcome MessageBox in RunSetupWizardIfNeeded (blocks headless test runs).
                Settings.Default.serverLocation = Path.Combine(Path.GetTempPath(), "IcarusManagerTests", "fake-install");
                Settings.Default.Save();

                using var form = new Form1();
                form.ShowInTaskbar = false;
                form.CreateControl();
                var onLoad = typeof(Form).GetMethod(
                    "OnLoad",
                    BindingFlags.Instance | BindingFlags.NonPublic,
                    binder: null,
                    types: new[] { typeof(EventArgs) },
                    modifiers: null);
                Assert.NotNull(onLoad);
                onLoad.Invoke(form, new object[] { EventArgs.Empty });
            }
            catch (Exception ex)
            {
                caught = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(120_000), "STA thread did not complete within timeout.");
        Assert.Null(caught);
    }

}
