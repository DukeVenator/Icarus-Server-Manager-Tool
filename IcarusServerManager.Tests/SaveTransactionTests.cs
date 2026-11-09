using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using IcarusProspectEditor;
using IcarusProspectEditor.Models;
using IcarusProspectEditor.Services;
using Xunit;

namespace IcarusServerManager.Tests;

/// <summary>
/// Saving is transactional: drafts are applied to a snapshot, and the editable document only
/// changes after the file has been published. This guards the old bug where a failed save left the
/// in-memory document holding edits the file never received.
/// </summary>
[Collection("WinFormsUi")]
public sealed class SaveTransactionTests
{
    [Fact]
    public void BuildingASaveCandidate_DoesNotMutateTheEditableDocument()
    {
        var workDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments),
            $"IcarusEditorSaveTx-{Guid.NewGuid():N}"[..24]);
        Directory.CreateDirectory(workDirectory);
        var prospect = Path.Combine(workDirectory, "DemoProspect.json");
        File.Copy(TestData.ResolveFile("SufferingResort.json"), prospect, overwrite: true);

        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                Application.EnableVisualStyles();
                using var form = new MainForm();
                var updateSettings = (ProspectEditorUpdateSettings)typeof(MainForm)
                    .GetField("_updateSettings", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(form)!;
                updateSettings.UpdateCheckEnabled = false;

                typeof(MainForm).GetMethod("LoadDocument", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(form, new object[] { prospect });
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-4000, -4000);
                form.ShowInTaskbar = false;
                form.Show();
                Application.DoEvents();

                var live = (ProspectDocument)typeof(MainForm)
                    .GetField("_document", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(form)!;
                var originalLobby = live.Prospect.ProspectInfo.LobbyName;

                var lobbyField = (TextBox)typeof(MainForm)
                    .GetField("_lobbyName", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(form)!;
                lobbyField.Text = "Changed Lobby";

                var buildCandidate = typeof(MainForm).GetMethod(
                    "TryBuildSaveCandidate",
                    BindingFlags.Instance | BindingFlags.NonPublic)!;
                var arguments = new object?[] { null, null, false };
                var candidate = (ProspectDocument?)buildCandidate.Invoke(form, arguments);

                Assert.NotNull(candidate);
                Assert.Equal("Changed Lobby", candidate!.Prospect.ProspectInfo.LobbyName);
                Assert.NotSame(live.Prospect, candidate.Prospect);

                // The live document must still hold the value that is actually on disk.
                Assert.Equal(originalLobby, live.Prospect.ProspectInfo.LobbyName);
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromMinutes(1)), "save transaction test did not finish");

        try
        {
            if (error is not null)
            {
                throw new Xunit.Sdk.XunitException(error.ToString());
            }
        }
        finally
        {
            try
            {
                Directory.Delete(workDirectory, recursive: true);
            }
            catch
            {
                // Best-effort cleanup.
            }
        }
    }
}
