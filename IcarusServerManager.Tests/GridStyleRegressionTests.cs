using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using IcarusProspectEditor;
using IcarusProspectEditor.Services;
using Xunit;

namespace IcarusServerManager.Tests;

/// <summary>
/// Regression guard for the overprinted-rows bug: a transparent alternating-row colour makes
/// DataGridView leave stale pixels behind, which looked like rows drawn on top of each other.
/// </summary>
[Collection("WinFormsUi")]
public sealed class GridStyleRegressionTests
{
    [Fact]
    public void GridsUseOpaqueAlternatingRowsAndDoubleBuffering()
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

                var grids = Descendants(form).OfType<DataGridView>().ToList();
                Assert.NotEmpty(grids);

                foreach (var grid in grids)
                {
                    var background = grid.AlternatingRowsDefaultCellStyle.BackColor;
                    if (background.A != 255)
                    {
                        failures.Add($"grid '{grid.AccessibleName}' has a non-opaque alternating row colour {background}");
                    }

                    var doubleBuffered = (bool?)typeof(DataGridView)
                        .GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic)
                        ?.GetValue(grid);
                    if (doubleBuffered != true)
                    {
                        failures.Add($"grid '{grid.AccessibleName}' is not double buffered");
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
        Assert.True(thread.Join(TimeSpan.FromMinutes(1)), "grid style test did not finish");
        if (error is not null)
        {
            throw new Xunit.Sdk.XunitException(error.ToString());
        }

        Assert.Empty(failures);
    }

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
