using IcarusProspectEditor.Models;
using IcarusSaveLib;

namespace IcarusProspectEditor.Services;

internal static class ProspectSaveService
{
    public static string CreateBackup(string path)
    {
        var backupPath = $"{path}.{DateTime.Now:yyyyMMdd-HHmmss}.bak";
        File.Copy(path, backupPath, overwrite: false);
        return backupPath;
    }

    public static void SaveProspect(ProspectSave prospect, string path)
    {
        using var stream = File.Create(path);
        prospect.Save(stream);
    }

    public static void SaveDocument(ProspectDocument document, bool createBackup = true)
    {
        if (File.Exists(document.ProspectPath))
        {
            CreateBackup(document.ProspectPath);
        }

        SaveProspect(document.Prospect, document.ProspectPath);
    }
}
