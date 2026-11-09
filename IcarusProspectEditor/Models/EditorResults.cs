namespace IcarusProspectEditor.Models;

/// <summary>
/// Outcome of a request to edit recorder fields or talents. Distinguishes an edit that changed
/// nothing from one that was rejected, instead of collapsing every non-exception case into true.
/// </summary>
internal enum RecorderEditStatus
{
    /// <summary>The request was valid and produced no change.</summary>
    Unchanged,

    /// <summary>The request was valid and the document was updated.</summary>
    Applied,

    /// <summary>At least one value could not be parsed or was out of range; nothing was committed.</summary>
    Invalid,

    /// <summary>The recorder index does not identify a recorder in the current document.</summary>
    NotFound,

    /// <summary>The document does not contain the structure this edit would require.</summary>
    Unsupported
}

internal sealed record RecorderFieldError(string Path, string Message);

internal sealed class RecorderEditResult
{
    public RecorderEditStatus Status { get; init; } = RecorderEditStatus.Unchanged;
    public IReadOnlyList<RecorderFieldError> Errors { get; init; } = Array.Empty<RecorderFieldError>();
    public int ChangedFieldCount { get; init; }

    public bool Succeeded => Status is RecorderEditStatus.Applied or RecorderEditStatus.Unchanged;
    public bool Changed => Status == RecorderEditStatus.Applied;

    public static RecorderEditResult Unchanged { get; } = new();

    public static RecorderEditResult Applied(int changedFieldCount) => new()
    {
        Status = RecorderEditStatus.Applied,
        ChangedFieldCount = changedFieldCount
    };

    public static RecorderEditResult Invalid(IReadOnlyList<RecorderFieldError> errors) => new()
    {
        Status = RecorderEditStatus.Invalid,
        Errors = errors
    };

    public static RecorderEditResult NotFound() => new() { Status = RecorderEditStatus.NotFound };

    public static RecorderEditResult Unsupported(string message) => new()
    {
        Status = RecorderEditStatus.Unsupported,
        Errors = new[] { new RecorderFieldError(string.Empty, message) }
    };

    /// <summary>Human-readable summary suitable for a status bar or message box.</summary>
    public string Describe()
    {
        return Status switch
        {
            RecorderEditStatus.Unchanged => "No changes to apply.",
            RecorderEditStatus.Applied => $"Applied {ChangedFieldCount} field change(s).",
            RecorderEditStatus.Invalid => "Nothing was applied because these values are invalid:\n" +
                                           string.Join("\n", Errors.Select(e =>
                                               string.IsNullOrEmpty(e.Path) ? e.Message : $"{e.Path}: {e.Message}")),
            RecorderEditStatus.NotFound => "The recorder no longer exists in this document.",
            RecorderEditStatus.Unsupported => Errors.Count > 0 ? Errors[0].Message : "This edit is not supported by the save format.",
            _ => "Edit failed."
        };
    }
}
