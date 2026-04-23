namespace IcarusProspectEditor.Services;

internal static class DangerWarningService
{
    public static string BuildWarningMessage(IEnumerable<string> inspectorEdits, IEnumerable<string> dangerNotes)
    {
        var inspector = inspectorEdits.ToList();
        var notes = dangerNotes.ToList();
        if (inspector.Count == 0 && notes.Count == 0)
        {
            return string.Empty;
        }

        var message = "Dangerous changes detected.\n\n";
        if (inspector.Count > 0)
        {
            message += $"- Inspector edits present ({inspector.Count}). These can break save compatibility.\n";
        }
        foreach (var note in notes)
        {
            message += $"- {note}\n";
        }

        message += "\nDo you want to continue with save?";
        return message;
    }
}
