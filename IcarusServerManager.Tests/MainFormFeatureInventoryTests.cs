using System.Reflection;
using System.Windows.Forms;
using IcarusProspectEditor;
using IcarusProspectEditor.Services;
using Xunit;

namespace IcarusServerManager.Tests;

/// <summary>
/// Feature-inventory guard (T15): the refreshed shell must keep every tab, command and shortcut that
/// existed before the UI work. Constructs the real form without showing it, so nothing is persisted
/// and no network call runs.
/// </summary>
[Collection("WinFormsUi")]
public sealed class MainFormFeatureInventoryTests
{
    [Fact]
    public void ShellKeepsAllTabsCommandsAndShortcuts()
    {
        var failures = new List<string>();
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                Application.EnableVisualStyles();
                using var form = new MainForm();
                var updateSettings = GetField(form, "_updateSettings") as ProspectEditorUpdateSettings;
                if (updateSettings is not null)
                {
                    updateSettings.UpdateCheckEnabled = false;
                }

                var tabs = GetField(form, "_tabs") as TabControl;
                Assert.NotNull(tabs);
                var tabNames = tabs!.TabPages.Cast<TabPage>().Select(p => p.Text).ToArray();
                var expectedTabs = new[]
                {
                    "Metadata", "Members", "Custom Settings", "Characters",
                    "Missions", "Prebuilts", "Mounts", "Diagnostics"
                };
                foreach (var expected in expectedTabs)
                {
                    if (!tabNames.Contains(expected, StringComparer.Ordinal))
                    {
                        failures.Add($"missing tab '{expected}' (found: {string.Join(", ", tabNames)})");
                    }
                }

                var menu = GetField(form, "_menu") as MenuStrip;
                Assert.NotNull(menu);
                var topLevel = menu!.Items.Cast<ToolStripItem>().Select(i => i.Text?.Replace("&", string.Empty)).ToArray();
                foreach (var expected in new[] { "File", "Edit", "View", "Tools", "Help" })
                {
                    if (!topLevel.Contains(expected, StringComparer.Ordinal))
                    {
                        failures.Add($"missing menu '{expected}' (found: {string.Join(", ", topLevel)})");
                    }
                }

                var shortcuts = menu.Items
                    .Cast<ToolStripMenuItem>()
                    .SelectMany(item => item.DropDownItems.Cast<ToolStripItem>())
                    .OfType<ToolStripMenuItem>()
                    .Where(item => item.ShortcutKeys != Keys.None)
                    .Select(item => item.ShortcutKeys)
                    .ToHashSet();

                var expectedShortcuts = new Dictionary<string, Keys>
                {
                    ["Open"] = Keys.Control | Keys.O,
                    ["Save"] = Keys.Control | Keys.S,
                    ["Save As"] = Keys.Control | Keys.Shift | Keys.S,
                    ["Export"] = Keys.Control | Keys.E,
                    ["Revert all"] = Keys.Control | Keys.R,
                    ["Revert selected"] = Keys.Control | Keys.Shift | Keys.R,
                    ["Refresh"] = Keys.F5
                };
                foreach (var (name, key) in expectedShortcuts)
                {
                    if (!shortcuts.Contains(key))
                    {
                        failures.Add($"missing shortcut {name} ({key})");
                    }
                }

                var view = menu.Items.Cast<ToolStripMenuItem>()
                    .Single(item => item.Text?.Replace("&", string.Empty) == "View");
                var theme = view.DropDownItems.OfType<ToolStripMenuItem>()
                    .SingleOrDefault(item => item.Text?.Replace("&", string.Empty) == "Theme");
                if (theme is null ||
                    !theme.DropDownItems.OfType<ToolStripMenuItem>().Any(i => i.Text?.Contains("Dark", StringComparison.OrdinalIgnoreCase) == true) ||
                    !theme.DropDownItems.OfType<ToolStripMenuItem>().Any(i => i.Text?.Contains("Light", StringComparison.OrdinalIgnoreCase) == true))
                {
                    failures.Add("View > Theme must keep both Dark and Light options");
                }

                if (GetField(form, "_saveButton") is not Button saveButton || saveButton.Text != "Save")
                {
                    failures.Add("primary Save button missing");
                }

                if (GetField(form, "_status") is not Label status || string.IsNullOrWhiteSpace(status.Text))
                {
                    failures.Add("status line missing");
                }
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromMinutes(1)), "feature inventory thread did not finish");

        if (error is not null)
        {
            throw new Xunit.Sdk.XunitException(error.ToString());
        }

        Assert.Empty(failures);
    }

    private static object? GetField(MainForm form, string name)
    {
        var field = typeof(MainForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return field!.GetValue(form);
    }
}
