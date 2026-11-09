using System.Reflection;
using System.Windows.Forms;
using IcarusProspectEditor;
using IcarusProspectEditor.Services;
using Xunit;

namespace IcarusServerManager.Tests;

/// <summary>
/// Accessibility contract for the shell: inputs and grids must expose a name, because a screen
/// reader otherwise announces only the value. Constructs the real form without showing it.
/// </summary>
[Collection("WinFormsUi")]
public sealed class MainFormAccessibilityTests
{
    [Fact]
    public void InputsAndGridsExposeAccessibleNames()
    {
        var failures = new List<string>();
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                Application.EnableVisualStyles();
                using var form = new MainForm();
                var settings = (ProspectEditorUpdateSettings)typeof(MainForm)
                    .GetField("_updateSettings", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(form)!;
                settings.UpdateCheckEnabled = false;

                var save = (Button)GetField(form, "_saveButton")!;
                if (string.IsNullOrWhiteSpace(save.AccessibleName))
                {
                    failures.Add("Save button has no accessible name");
                }

                var tabs = (TabControl)GetField(form, "_tabs")!;
                if (string.IsNullOrWhiteSpace(tabs.AccessibleName))
                {
                    failures.Add("Tab control has no accessible name");
                }

                var advanced = (Button)GetField(form, "_advancedToggle")!;
                if (string.IsNullOrWhiteSpace(advanced.AccessibleName))
                {
                    failures.Add("Advanced identifiers toggle has no accessible name");
                }

                foreach (TabPage page in tabs.TabPages)
                {
                    if (string.IsNullOrWhiteSpace(page.Text))
                    {
                        failures.Add("A tab page has no text");
                    }
                }

                foreach (var grid in Descendants(form).OfType<DataGridView>())
                {
                    if (string.IsNullOrWhiteSpace(grid.AccessibleName))
                    {
                        failures.Add($"A {grid.GetType().Name} has no accessible name");
                    }
                }

                var metadataRoot = (Control)GetField(form, "_metadataRoot")!;
                foreach (var input in Descendants(metadataRoot).Where(IsNamedInput))
                {
                    if (string.IsNullOrWhiteSpace(input.AccessibleName))
                    {
                        failures.Add($"{input.GetType().Name} in Metadata has no accessible name");
                    }
                }
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromMinutes(1)), "accessibility test did not finish");
        if (error is not null)
        {
            throw new Xunit.Sdk.XunitException(error.ToString());
        }

        Assert.Empty(failures);
    }

    private static bool IsNamedInput(Control control) =>
        control is TextBox or NumericUpDown or ComboBox or CheckBox or DateTimePicker
        && control.Parent is not NumericUpDown;

    private static object? GetField(MainForm form, string name) =>
        typeof(MainForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form);

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
}
