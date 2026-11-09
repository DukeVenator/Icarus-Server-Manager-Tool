using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using IcarusProspectEditor;
using IcarusProspectEditor.Services;
using Xunit;

namespace IcarusServerManager.Tests;

/// <summary>
/// Command-line opening: parsing is pure and always tested; the window-level check launches the real
/// form with a copied fixture and disables the startup update check so no network call happens.
/// </summary>
[Collection("WinFormsUi")]
public sealed class StartupArgumentsTests
{
    [Fact]
    public void ParsesOpenSwitchAndBarePath()
    {
        using var temp = new FixtureFile();
        var args = new[] { "--open", temp.Path };
        Assert.Equal(temp.Path, StartupArguments.GetProspectPath(args));

        Assert.Equal(temp.Path, StartupArguments.GetProspectPath(new[] { temp.Path }));
        Assert.Equal(temp.Path, StartupArguments.GetProspectPath(new[] { "-o", temp.Path }));
        Assert.Equal(temp.Path, StartupArguments.GetProspectPath(new[] { "/open", temp.Path }));
    }

    [Fact]
    public void IgnoresMissingValuesAndNonexistentFiles()
    {
        Assert.Null(StartupArguments.GetProspectPath(Array.Empty<string>()));
        Assert.Null(StartupArguments.GetProspectPath(new[] { "--open" }));
        Assert.Null(StartupArguments.GetProspectPath(new[] { "--open", "  " }));
        Assert.Null(StartupArguments.GetProspectPath(new[] { @"C:\definitely\missing\prospect.json" }));
        Assert.Null(StartupArguments.GetProspectPath(new[] { "--unknown-switch" }));
    }

    [Fact]
    public void UpdateCheckSeam_IsHonouredByEnvironmentSwitch()
    {
        var property = typeof(MainForm).GetProperty(
            "AutomaticUpdateCheckDisabled",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(property);

        Environment.SetEnvironmentVariable("ICARUS_EDITOR_DISABLE_UPDATE_CHECK", "1");
        try
        {
            Assert.True((bool)property!.GetValue(null)!);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ICARUS_EDITOR_DISABLE_UPDATE_CHECK", null);
        }

        Assert.False((bool)property.GetValue(null)!);
    }

    [Fact]
    public void StartsWithTheSuppliedProspectLoaded()
    {
        using var temp = new FixtureFile();
        Exception? error = null;
        var loaded = false;
        var thread = new Thread(() =>
        {
            try
            {
                Application.EnableVisualStyles();
                using var form = new MainForm(temp.Path);
                var settings = typeof(MainForm)
                    .GetField("_updateSettings", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.GetValue(form) as ProspectEditorUpdateSettings;
                Assert.NotNull(settings);
                settings!.UpdateCheckEnabled = false;

                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-4000, -4000);
                form.ShowInTaskbar = false;
                form.Show();
                Application.DoEvents();

                var document = typeof(MainForm)
                    .GetField("_document", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.GetValue(form);
                loaded = document is not null;

                var title = (Label)typeof(MainForm)
                    .GetField("_documentTitle", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(form)!;
                Assert.False(string.IsNullOrWhiteSpace(title.Text));
                Assert.NotEqual("No prospect open", title.Text);

                form.Hide();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromMinutes(1)), "startup test did not finish");
        if (error is not null)
        {
            throw new Xunit.Sdk.XunitException(error.ToString());
        }

        Assert.True(loaded, "the form did not load the command-line prospect");
    }

    private sealed class FixtureFile : IDisposable
    {
        private readonly string _directory;

        public FixtureFile()
        {
            // One self-contained directory under the shared documents folder: no user name in the
            // path, and cleanup removes everything it created.
            _directory = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments),
                $"IcarusEditorStartup-{Guid.NewGuid():N}"[..24]);
            Directory.CreateDirectory(_directory);
            Path = System.IO.Path.Combine(_directory, "DemoProspect.json");
            File.Copy(TestData.ResolveFile("SufferingResort.json"), Path, overwrite: true);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_directory, recursive: true);
            }
            catch
            {
                // Best-effort cleanup of the test-owned directory.
            }
        }
    }
}
