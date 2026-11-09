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
/// Clicking a column header must sort. BindingList has no native sorting, so the shell sorts it
/// programmatically; this test drives the same method the header click calls and checks order,
/// direction toggling and that sorting does not mark the document dirty.
/// </summary>
[Collection("WinFormsUi")]
public sealed class GridSortingTests
{
    [Fact]
    public void ClickingAColumnHeader_SortsAndTogglesDirection()
    {
        var workDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments),
            $"IcarusEditorSort-{Guid.NewGuid():N}"[..22]);
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

                var members = (BindingList<MemberRow>)typeof(MainForm)
                    .GetField("_members", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(form)!;
                var grid = (DataGridView)typeof(MainForm)
                    .GetField("_membersGrid", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(form)!;

                members.Clear();
                Add(members, "Charlie");
                Add(members, "Alpha");
                Add(members, "Bravo");
                Application.DoEvents();
                Assert.Equal(new[] { "Charlie", "Alpha", "Bravo" }, Names(members));

                var headerClick = typeof(MainForm).GetMethod("GridOnColumnHeaderMouseClick", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var column = grid.Columns["CharacterName"];
                Assert.NotNull(column);

                // Drive the same handler a real header click raises.
                var clickArgs = new DataGridViewCellMouseEventArgs(
                    column!.Index,
                    -1,
                    0,
                    0,
                    new MouseEventArgs(MouseButtons.Left, 1, 0, 0, 0));
                headerClick.Invoke(form, new object?[] { grid, clickArgs });
                Application.DoEvents();
                Assert.Equal(new[] { "Alpha", "Bravo", "Charlie" }, Names(members));
                Assert.Equal(SortOrder.Ascending, column.HeaderCell.SortGlyphDirection);

                headerClick.Invoke(form, new object?[] { grid, clickArgs });
                Application.DoEvents();
                Assert.Equal(new[] { "Charlie", "Bravo", "Alpha" }, Names(members));
                Assert.Equal(SortOrder.Descending, column.HeaderCell.SortGlyphDirection);

                // Sorting is a view operation, not an edit.
                var revision = (int)typeof(MainForm)
                    .GetField("_revision", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
                var savedRevision = (int)typeof(MainForm)
                    .GetField("_savedRevision", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
                Assert.Equal(savedRevision, revision);
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromMinutes(1)), "grid sorting test did not finish");

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

    private static void Add(BindingList<MemberRow> members, string name) => members.Add(new MemberRow
    {
        AccountName = name,
        CharacterName = name,
        UserID = "76561190000000010",
        ChrSlot = 0,
        Status = "Active"
    });

    private static string[] Names(BindingList<MemberRow> members) =>
        members.Select(m => m.CharacterName).ToArray();
}
