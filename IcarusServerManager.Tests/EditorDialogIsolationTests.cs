using System.ComponentModel;
using System.Windows.Forms;
using IcarusProspectEditor;
using IcarusProspectEditor.Mapping;
using IcarusProspectEditor.Models;
using Xunit;

namespace IcarusServerManager.Tests;

/// <summary>
/// Dialog isolation evidence (E10/E13): dialogs edit their own drafts, so typing and cancelling can
/// never change the caller's rows. These tests construct the dialogs without showing them, so no
/// message box or file dialog runs.
/// </summary>
[Collection("WinFormsUi")]
public sealed class EditorDialogIsolationTests
{
    [Fact]
    public void MountEditorForm_DraftsAreIndependentOfCallerRows()
    {
        RunSta(() =>
        {
            var mount = NewMount(level: 4, species: "Tusker");
            var talents = new List<TalentRow>
            {
                new() { Name = "Creature_Base_Health_Tusker", Rank = 2, MaxRank = 10 }
            };
            var fields = new List<RecorderFieldRow>
            {
                new() { Path = "TalentArray", PropertyType = "ArrayProperty", Value = "original", Editable = false },
                new() { Path = "Level", PropertyType = "IntProperty", Value = "4", Editable = true }
            };

            using var form = new MountEditorForm(mount, talents, fields, Array.Empty<MemberRow>());

            mount.Level = 99;
            mount.MountName = "Changed externally";
            talents[0].Rank = 9;
            fields[1].Value = "77";

            Assert.NotSame(mount, form.UpdatedMount);
            Assert.Equal(4, form.UpdatedMount.Level);
            Assert.NotEqual("Changed externally", form.UpdatedMount.MountName);
            Assert.Equal(2, form.UpdatedTalents.Single(t => t.Name == "Creature_Base_Health_Tusker").Rank);
            Assert.Equal("4", form.UpdatedAdvancedFields.Single(f => f.Path == "Level").Value);
            Assert.Equal(DialogResult.None, form.DialogResult);
        });
    }

    [Fact]
    public void MountEditorForm_KeepsUnknownStoredSpeciesSelectable()
    {
        RunSta(() =>
        {
            const string unknownSpecies = "Regression_Unknown_Species";
            var mount = NewMount(level: 1, species: unknownSpecies);

            using var form = new MountEditorForm(
                mount,
                new List<TalentRow>(),
                new List<RecorderFieldRow> { new() { Path = "Level", PropertyType = "IntProperty", Value = "1", Editable = true } },
                Array.Empty<MemberRow>());

            var speciesValues = Descendants(form)
                .OfType<ComboBox>()
                .SelectMany(combo => combo.Items.Cast<object>().Select(item => item?.ToString()))
                .Where(value => value is not null)
                .ToArray();

            Assert.Contains(unknownSpecies, speciesValues);
        });
    }

    [Fact]
    public void RecorderInspectorForm_ExposesClonedRowsOnly()
    {
        RunSta(() =>
        {
            var rows = new List<RecorderFieldRow>
            {
                new() { Path = "Level", PropertyType = "IntProperty", Value = "5", Editable = true }
            };

            using var form = new RecorderInspectorForm("Test recorder", rows, Array.Empty<MemberRow>());

            rows[0].Value = "999";
            rows[0].Path = "RenamedExternally";

            var edited = Assert.Single(form.EditedRows);
            Assert.NotSame(rows[0], edited);
            Assert.Equal("Level", edited.Path);
            Assert.Equal("5", edited.Value);
        });
    }

    [Fact]
    public void RecorderFieldsEditorTabs_InvalidTypedValueReportsFailureInsteadOfCommitting()
    {
        RunSta(() =>
        {
            var rows = new BindingList<RecorderFieldRow>
            {
                new() { Path = "Level", PropertyType = "IntProperty", Value = "5", Editable = true }
            };

            using var editor = new RecorderFieldsEditorTabs(rows, Array.Empty<MemberRow>());
            var textBoxField = typeof(RecorderFieldsEditorTabs)
                .GetField("_wizardValue", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.NotNull(textBoxField);
            var textBox = Assert.IsType<TextBox>(textBoxField!.GetValue(editor));
            textBox.Text = "not-a-number";

            var result = editor.CommitCurrentValue();

            Assert.False(result.Succeeded);
            Assert.Equal("5", rows[0].Value);
        });
    }

    private static MountRow NewMount(int level, string species) => new()
    {
        RecorderIndex = 3,
        RecorderPath = "/Script/Icarus.MountRecorder",
        MountName = "Test Mount",
        OwnerPlayerId = "76561190000000001",
        OwnerName = "Owner",
        MountType = "Mount",
        MountRace = species,
        Level = level,
        Variation = 0,
        Health = 100,
        Experience = 10
    };

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var grandChild in Descendants(child))
            {
                yield return grandChild;
            }
        }
    }

    private static void RunSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                Application.EnableVisualStyles();
                action();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromMinutes(1)), "STA dialog test did not finish");
        if (error is not null)
        {
            throw new Xunit.Sdk.XunitException(error.ToString());
        }
    }
}
