using System.ComponentModel;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using IcarusProspectEditor;
using IcarusProspectEditor.Models;
using IcarusProspectEditor.Services;
using Xunit;

namespace IcarusServerManager.Tests;

/// <summary>
/// E9 at the UI layer: renaming a member and choosing Revert selected must restore that row rather
/// than deleting it. Grid editors cannot be typed into from automation, so the rename is applied to
/// the bound row and the same command handler the button uses is invoked.
/// </summary>
[Collection("WinFormsUi")]
public sealed class MemberRevertUiTests
{
    [Fact]
    public void RenamedMember_RevertSelectedRestoresTheRow()
    {
        var workDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments),
            $"IcarusEditorRevert-{Guid.NewGuid():N}"[..24]);
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

                // The grid only materialises bound rows once the form has a handle and binding
                // context, so show it off-screen before touching rows.
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-4000, -4000);
                form.ShowInTaskbar = false;
                form.Show();
                Application.DoEvents();

                var members = (BindingList<MemberRow>)typeof(MainForm)
                    .GetField("_members", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(form)!;
                var grid = (DataGridView)typeof(MainForm)
                    .GetField("_membersGrid", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(form)!;

                members.Clear();
                members.Add(new MemberRow
                {
                    AccountName = "Revert Account",
                    CharacterName = "Original Name",
                    UserID = "76561190000000009",
                    ChrSlot = 0,
                    Status = "Active"
                });
                Application.DoEvents();

                // Treat the seeded row as the saved baseline, then rename it.
                typeof(MainForm).GetMethod("CaptureBaselines", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(form, null);
                members[0].CharacterName = "Renamed By Test";

                grid.ClearSelection();
                grid.Rows[0].Selected = true;
                grid.CurrentCell = grid.Rows[0].Cells[1];
                Assert.Single(grid.SelectedRows);

                typeof(MainForm).GetMethod("RevertSelectedRows", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(form, new object[] { "Members", grid });

                Assert.Single(members);
                Assert.Equal("Original Name", members[0].CharacterName);
                Assert.Equal("76561190000000009", members[0].UserID);
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromMinutes(1)), "member revert test did not finish");

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
