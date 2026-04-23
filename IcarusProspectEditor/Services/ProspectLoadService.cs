using IcarusProspectEditor.Models;
using IcarusSaveLib;

namespace IcarusProspectEditor.Services;

internal static class ProspectLoadService
{
    public static ProspectDocument Load(string prospectPath)
    {
        using var file = File.OpenRead(prospectPath);
        var prospect = ProspectSave.Load(file) ?? throw new InvalidDataException("Unable to decode prospect save.");

        return new ProspectDocument
        {
            ProspectPath = prospectPath,
            Prospect = prospect
        };
    }
}
