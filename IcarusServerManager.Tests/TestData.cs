using System;
using System.IO;

namespace IcarusServerManager.Tests;

internal static class TestData
{
    public static string ResolveFile(string filename)
    {
        var fromOutput = Path.Combine(AppContext.BaseDirectory, "TestData", filename);
        if (File.Exists(fromOutput))
        {
            return fromOutput;
        }

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var testDataCandidate = Path.Combine(dir.FullName, "IcarusServerManager.Tests", "TestData", filename);
            var slnCandidate = Path.Combine(dir.FullName, "IcarusServerManager.sln");
            if (File.Exists(testDataCandidate) && File.Exists(slnCandidate))
            {
                return testDataCandidate;
            }

            var rootCandidate = Path.Combine(dir.FullName, filename);
            if (File.Exists(rootCandidate) && File.Exists(slnCandidate))
            {
                return rootCandidate;
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException(
            $"Unable to resolve test data file '{filename}'. " +
            $"Expected it in 'IcarusServerManager.Tests/TestData/' (copied to the test output directory on build).");
    }
}
