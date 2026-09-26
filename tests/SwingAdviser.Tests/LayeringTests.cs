using System.Xml.Linq;

namespace SwingAdviser.Tests;

public class LayeringTests
{
    [Fact]
    public void Domain_HasNoPackageOrProjectReferences()
    {
        var csprojPath = FindRepoRoot("src", "SwingAdviser.Domain", "SwingAdviser.Domain.csproj");
        var document = XDocument.Load(csprojPath);

        var references = document.Descendants()
            .Where(e => e.Name.LocalName is "PackageReference" or "ProjectReference")
            .ToList();

        Assert.Empty(references);
    }

    private static string FindRepoRoot(params string[] relativeSegments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SwingAdviser.slnx")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            throw new InvalidOperationException("リポジトリルート（SwingAdviser.slnx）が見つかりません。");
        }

        return Path.Combine([directory.FullName, .. relativeSegments]);
    }
}
