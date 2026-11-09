using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using System.Windows.Forms;
using IcarusProspectEditor;
using IcarusProspectEditor.Mapping;
using IcarusProspectEditor.Models;
using IcarusProspectEditor.Services;
using Xunit;
using Xunit.Abstractions;

namespace IcarusServerManager.Tests;

/// <summary>
/// Renders the real editor shell with a copied fixture and writes screenshots for manual visual
/// review (T07/T08/T09 evidence). It disables the startup update check first so no network request
/// is made, and it never touches a real save.
/// </summary>
[Trait("Category", "Visual")]
[Collection("WinFormsUi")]
public sealed class MainFormVisualHarnessTests
{
    private readonly ITestOutputHelper _output;

    public MainFormVisualHarnessTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void RendersShellAtSupportedSizesInBothThemes()
    {
        var outputDirectory = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "artifacts", "verification", "ui"));
        Directory.CreateDirectory(outputDirectory);

        var fixture = TestData.ResolveFile("SufferingResort.json");
        var (copy, workDirectory) = CreateSanitizedFixture(fixture);

        var failures = new List<string>();
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                Application.EnableVisualStyles();

                // A fresh form per size: a hidden form whose Size is changed after Show keeps the
                // child layout it had at creation, which would misrepresent the narrow layout.
                var sizes = new[] { new Size(1440, 900), new Size(1024, 700), new Size(760, 520) };
                foreach (var size in sizes)
                {
                    foreach (var dark in new[] { false, true })
                    {
                        using var form = new MainForm { Size = size };
                        DisableAutomaticUpdateCheck(form);
                        InvokePrivate(form, "LoadDocument", copy);
                        Assert.NotNull(GetPrivateField(form, "_document"));

                        form.StartPosition = FormStartPosition.Manual;
                        form.Location = new Point(-4000, -4000);
                        form.ShowInTaskbar = false;
                        form.Show();
                        SetTheme(form, dark);
                        Application.DoEvents();
                        form.PerformLayout();
                        Application.DoEvents();

                        var name = $"{(dark ? "dark" : "light")}-{size.Width}x{size.Height}.png";
                        var path = Path.Combine(outputDirectory, name);
                        using var bitmap = new Bitmap(Math.Max(1, form.Width), Math.Max(1, form.Height));
                        form.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
                        bitmap.Save(path, ImageFormat.Png);

                        var colors = CountSampledColors(bitmap);
                        if (colors < 4)
                        {
                            failures.Add($"{name}: render looks blank ({colors} distinct sampled colors)");
                        }

                        var metadataRoot = GetPrivateField(form, "_metadataRoot") as Control;
                        var metadataColumns = metadataRoot is null
                            ? -1
                            : (int)metadataRoot.GetType().GetProperty("ColumnCount")!.GetValue(metadataRoot)!;
                        var tabs = (TabControl)GetPrivateField(form, "_tabs")!;
                        var tabStripWidth = 0;
                        for (var tabIndex = 0; tabIndex < tabs.TabCount; tabIndex++)
                        {
                            tabStripWidth += tabs.GetTabRect(tabIndex).Width;
                        }
                        _output.WriteLine(
                            $"{name}: bitmap {bitmap.Width}x{bitmap.Height}, form {form.Width}x{form.Height}, " +
                            $"client {form.ClientSize.Width}, device DPI {form.DeviceDpi}, colors {colors}, " +
                            $"metadata columns {metadataColumns}, metadata root {metadataRoot?.Width}, " +
                            $"tabs {tabs.Width} dock {tabs.Dock}, parent {tabs.Parent?.GetType().Name} {tabs.Parent?.Width}x{tabs.Parent?.Height}, " +
                            $"tab strip {tabStripWidth}px");

                        if (size.Width <= 800 && metadataColumns != 1)
                        {
                            failures.Add($"{name}: metadata stayed at {metadataColumns} columns in a narrow window");
                        }

                        if (tabStripWidth > tabs.Width)
                        {
                            failures.Add($"{name}: tab strip needs {tabStripWidth}px but only {tabs.Width}px is available");
                        }

                        form.Hide();
                    }
                }

                _output.WriteLine($"screenshots: {outputDirectory}");
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromMinutes(2)), "visual harness thread did not finish");

        try
        {
            if (error is not null)
            {
                throw new Xunit.Sdk.XunitException(error.ToString());
            }

            Assert.Empty(failures);
        }
        finally
        {
            try
            {
                Directory.Delete(workDirectory, recursive: true);
            }
            catch
            {
                // Best-effort temp cleanup; screenshots are the retained evidence.
            }
        }
    }

    [Fact]
    public void LargeFontScale_KeepsTabsAndMetadataUsable()
    {
        var outputDirectory = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "artifacts", "verification", "ui"));
        Directory.CreateDirectory(outputDirectory);
        var fixture = TestData.ResolveFile("SufferingResort.json");
        var (copy, workDirectory) = CreateSanitizedFixture(fixture);

        var failures = new List<string>();
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                Application.EnableVisualStyles();
                foreach (var scale in new[] { 1.5f, 2.0f })
                {
                    using var form = new MainForm { Size = new Size(1024, 700) };
                    var baseFont = form.Font;
                    form.Font = new Font(baseFont.FontFamily, baseFont.Size * scale, baseFont.Style);
                    DisableAutomaticUpdateCheck(form);
                    InvokePrivate(form, "LoadDocument", copy);
                    form.StartPosition = FormStartPosition.Manual;
                    form.Location = new Point(-4000, -4000);
                    form.ShowInTaskbar = false;
                    form.Show();
                    SetTheme(form, dark: true);
                    Application.DoEvents();
                    form.PerformLayout();
                    Application.DoEvents();

                    var tabs = (TabControl)GetPrivateField(form, "_tabs")!;
                    var tabStripWidth = 0;
                    var overflowedTab = false;
                    for (var i = 0; i < tabs.TabCount; i++)
                    {
                        var rect = tabs.GetTabRect(i);
                        tabStripWidth += rect.Width;
                        overflowedTab |= rect.Right > tabs.Width;
                    }

                    var metadataRoot = GetPrivateField(form, "_metadataRoot");
                    var metadataColumns = metadataRoot is null
                        ? -1
                        : (int)metadataRoot.GetType().GetProperty("ColumnCount")!.GetValue(metadataRoot)!;

                    var name = $"dark-1024x700-font{(int)(scale * 100)}.png";
                    using var bitmap = new Bitmap(form.Width, form.Height);
                    form.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
                    bitmap.Save(Path.Combine(outputDirectory, name), ImageFormat.Png);

                    _output.WriteLine($"{name}: font {form.Font.Size:F1}pt, tab strip {tabStripWidth}px of {tabs.Width}px, metadata columns {metadataColumns}");
                    if (overflowedTab)
                    {
                        failures.Add($"{name}: a tab is clipped past the control edge");
                    }

                    if (metadataColumns < 1)
                    {
                        failures.Add($"{name}: metadata layout did not initialise");
                    }

                    if (scale >= 2.0f && metadataColumns != 1)
                    {
                        failures.Add($"{name}: metadata stayed at {metadataColumns} columns with double-size text");
                    }

                    form.Hide();
                }
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromMinutes(2)), "font-scale harness thread did not finish");

        try
        {
            if (error is not null)
            {
                throw new Xunit.Sdk.XunitException(error.ToString());
            }

            Assert.Empty(failures);
        }
        finally
        {
            try
            {
                Directory.Delete(workDirectory, recursive: true);
            }
            catch
            {
                // Best-effort temp cleanup; screenshots are retained.
            }
        }
    }

    [Fact]
    public void CapturesEveryTabAndBothWorkflowDialogs()
    {
        var root = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "artifacts", "verification", "ui"));
        var tabDirectory = Path.Combine(root, "tabs");
        var dialogDirectory = Path.Combine(root, "dialogs");
        Directory.CreateDirectory(tabDirectory);
        Directory.CreateDirectory(dialogDirectory);

        var fixture = TestData.ResolveFile("SufferingResort.json");
        var (copy, workDirectory) = CreateSanitizedFixture(fixture);

        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                Application.EnableVisualStyles();
                using var form = new MainForm { Size = new Size(1440, 900) };
                DisableAutomaticUpdateCheck(form);
                InvokePrivate(form, "LoadDocument", copy);
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-4000, -4000);
                form.ShowInTaskbar = false;
                form.Show();
                SetTheme(form, dark: false);
                Application.DoEvents();

                var tabs = (TabControl)GetPrivateField(form, "_tabs")!;
                for (var index = 0; index < tabs.TabCount; index++)
                {
                    tabs.SelectedIndex = index;
                    Application.DoEvents();
                    form.PerformLayout();
                    Application.DoEvents();
                    var label = tabs.TabPages[index].Text.Replace(" ", "-", StringComparison.Ordinal);
                    Capture(form, Path.Combine(tabDirectory, $"light-{index + 1:00}-{label}.png"));
                }

                SetTheme(form, dark: true);
                tabs.SelectedIndex = 6; // Mounts: the densest grid in dark mode
                Application.DoEvents();
                form.PerformLayout();
                Application.DoEvents();
                Capture(form, Path.Combine(tabDirectory, "dark-07-Mounts.png"));

                var document = ProspectLoadService.Load(copy);
                var mount = ProspectModelMapper.ReadMountsFromProspect(document.Prospect).First();
                var fields = ProspectModelMapper.ReadRecorderFields(document.Prospect, mount.RecorderIndex);
                var talents = ProspectModelMapper.ReadTalentRows(document.Prospect, mount.RecorderIndex);

                using (var mountEditor = new MountEditorForm(mount, talents, fields, Array.Empty<MemberRow>()))
                {
                    mountEditor.StartPosition = FormStartPosition.Manual;
                    mountEditor.Location = new Point(-4000, -4000);
                    mountEditor.ShowInTaskbar = false;
                    mountEditor.Show();
                    mountEditor.ApplyTheme(dark: false);
                    Application.DoEvents();
                    mountEditor.PerformLayout();
                    Application.DoEvents();

                    var editorTabs = Descendants(mountEditor).OfType<TabControl>().FirstOrDefault();
                    if (editorTabs is null)
                    {
                        Capture(mountEditor, Path.Combine(dialogDirectory, "mount-editor-light.png"));
                    }
                    else
                    {
                        _output.WriteLine(
                            $"mount editor tabs: DrawMode={editorTabs.DrawMode}, SizeMode={editorTabs.SizeMode}, " +
                            $"tabHeight={editorTabs.ItemSize.Height}");
                        for (var index = 0; index < editorTabs.TabCount; index++)
                        {
                            editorTabs.SelectedIndex = index;
                            Application.DoEvents();
                            mountEditor.PerformLayout();
                            Application.DoEvents();
                            var label = editorTabs.TabPages[index].Text.Replace(" ", "-", StringComparison.Ordinal);
                            Capture(mountEditor, Path.Combine(dialogDirectory, $"mount-editor-{index + 1:00}-{label}.png"));
                        }
                    }

                    mountEditor.Close();
                }

                using (var inspector = new RecorderInspectorForm("Recorder inspector — MountRecorder", fields, Array.Empty<MemberRow>()))
                {
                    inspector.StartPosition = FormStartPosition.Manual;
                    inspector.Location = new Point(-4000, -4000);
                    inspector.ShowInTaskbar = false;
                    inspector.Show();
                    inspector.ApplyTheme(dark: true);
                    Application.DoEvents();
                    inspector.PerformLayout();
                    Application.DoEvents();
                    Capture(inspector, Path.Combine(dialogDirectory, "recorder-inspector-dark.png"));
                    inspector.Close();
                }

                _output.WriteLine($"tabs: {tabDirectory}");
                _output.WriteLine($"dialogs: {dialogDirectory}");
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromMinutes(3)), "tab/dialog capture thread did not finish");

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
                // Best-effort temp cleanup; screenshots are retained.
            }
        }
    }

    private const string DemoPlayerId = "76561190000000001";

    /// <summary>
    /// Copies the fixture somewhere neutral and replaces every real account id, player name and
    /// profile path, so published screenshots contain no personal data. The path deliberately lives
    /// under the shared documents folder: a temp path would embed the signed-in user's name.
    /// </summary>
    private static (string Path, string Directory) CreateSanitizedFixture(string fixturePath)
    {
        // One self-contained directory under the shared documents folder: no user name in the path,
        // and cleanup removes everything it created.
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments),
            $"IcarusEditorShots-{Guid.NewGuid():N}"[..21]);
        Directory.CreateDirectory(root);

        var document = ProspectLoadService.Load(fixturePath);

        var info = document.Prospect.ProspectInfo;
        info.LobbyName = "Demo Prospect";
        info.ProspectID = "IcarusProspect";
        info.ClaimedAccountID = DemoPlayerId;
        info.ClaimedAccountCharacter = 0;
        document.Prospect.ProspectInfo = info;

        ProspectModelMapper.ApplyMembers(document.Prospect, new[]
        {
            new MemberRow
            {
                AccountName = "Demo Leader",
                CharacterName = "Astra",
                UserID = DemoPlayerId,
                ChrSlot = 0,
                Status = "Active"
            },
            new MemberRow
            {
                AccountName = "Demo Ranger",
                CharacterName = "Bramble",
                UserID = "76561190000000002",
                ChrSlot = 1,
                Status = "Active"
            },
            new MemberRow
            {
                AccountName = "Demo Builder",
                CharacterName = "Cinder",
                UserID = "76561190000000003",
                ChrSlot = 2,
                Status = "Idle"
            }
        });

        foreach (var mount in ProspectModelMapper.ReadMountsFromProspect(document.Prospect))
        {
            var demoMount = mount.Clone();
            demoMount.OwnerPlayerId = DemoPlayerId;
            demoMount.OwnerName = "Demo Leader";
            demoMount.OwnerCharacterSlot = 0;

            var fields = ProspectModelMapper.ReadRecorderFields(document.Prospect, mount.RecorderIndex);
            var rows = ProspectModelMapper.BuildMountFieldValues(demoMount, fields)
                .Select(kv => new RecorderFieldRow
                {
                    Path = kv.Key,
                    PropertyType = string.Empty,
                    Value = kv.Value,
                    Editable = true
                })
                .ToList();

            var applied = ProspectModelMapper.ApplyMountEdits(document.Prospect, mount.RecorderIndex, rows, talents: null);
            if (!applied.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Screenshot fixture mount {mount.RecorderIndex} could not be anonymised: {applied.Describe()}");
            }
        }

        var path = Path.Combine(root, "DemoProspect.json");
        var saved = ProspectSaveService.SaveProspect(document.Prospect, path);
        if (!saved.Succeeded)
        {
            throw new InvalidOperationException($"Screenshot fixture could not be written: {saved.Error}");
        }

        return (path, root);
    }

    private static void Capture(Form form, string path)
    {
        using var bitmap = new Bitmap(Math.Max(1, form.Width), Math.Max(1, form.Height));
        form.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
        bitmap.Save(path, ImageFormat.Png);
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

    private static void DisableAutomaticUpdateCheck(MainForm form)
    {
        var settings = (ProspectEditorUpdateSettings?)GetPrivateField(form, "_updateSettings");
        Assert.NotNull(settings);
        settings!.UpdateCheckEnabled = false;
    }

    private static void SetTheme(MainForm form, bool dark)
    {
        var field = typeof(MainForm).GetField("_isDarkTheme", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field!.SetValue(form, dark);
        UiThemeService.ApplyTheme(form, dark);
        var accent = typeof(MainForm).GetMethod("ApplyAccentButton", BindingFlags.Instance | BindingFlags.NonPublic);
        accent?.Invoke(form, null);
    }

    private static object? GetPrivateField(MainForm form, string name)
    {
        var field = typeof(MainForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return field!.GetValue(form);
    }

    private static void InvokePrivate(MainForm form, string name, params object[] arguments)
    {
        var method = typeof(MainForm).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        try
        {
            method!.Invoke(form, arguments);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw ex.InnerException;
        }
    }

    private static int CountSampledColors(Bitmap bitmap)
    {
        var colors = new HashSet<int>();
        for (var y = 0; y < bitmap.Height; y += Math.Max(1, bitmap.Height / 40))
        {
            for (var x = 0; x < bitmap.Width; x += Math.Max(1, bitmap.Width / 40))
            {
                colors.Add(bitmap.GetPixel(x, y).ToArgb());
            }
        }

        return colors.Count;
    }
}
