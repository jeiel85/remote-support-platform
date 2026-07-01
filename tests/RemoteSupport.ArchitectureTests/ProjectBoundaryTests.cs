using System.Xml.Linq;

namespace RemoteSupport.ArchitectureTests;

public sealed class ProjectBoundaryTests
{
    private static readonly string Root = FindRoot();

    [Fact]
    [Trait("Requirement", "NFR-MNT-005")]
    public void DomainHasNoProjectDependencies()
    {
        string project = Path.Combine(Root, "src", "client", "managed", "RemoteSupport.Domain", "RemoteSupport.Domain.csproj");
        Assert.Empty(ProjectReferences(project));
    }

    [Fact]
    [Trait("Requirement", "NFR-MNT-005")]
    public void ApplicationDependsOnlyOnDomain()
    {
        string project = Path.Combine(Root, "src", "client", "managed", "RemoteSupport.Application", "RemoteSupport.Application.csproj");
        string[] references = ProjectReferences(project).Select(path => Path.GetFileNameWithoutExtension(path)!).Order().ToArray();
        Assert.Equal(["RemoteSupport.Domain"], references);
    }

    [Fact]
    [Trait("Requirement", "NFR-MNT-005")]
    public void InfrastructureOwnsExternalAdaptersWithoutReversingDependencies()
    {
        string project = Path.Combine(Root, "src", "client", "managed", "RemoteSupport.Infrastructure", "RemoteSupport.Infrastructure.csproj");
        string[] references = ProjectReferences(project).Select(path => Path.GetFileNameWithoutExtension(path)!).Order().ToArray();
        Assert.Equal([
            "RemoteSupport.Application",
            "RemoteSupport.Ipc",
            "RemoteSupport.Observability",
            "RemoteSupport.Protocol",
            "RemoteSupport.Security",
        ], references);
    }

    private static IEnumerable<string> ProjectReferences(string project) =>
        XDocument.Load(project).Descendants("ProjectReference").Select(element => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(project)!, element.Attribute("Include")!.Value)));

    private static string FindRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "global.json")))
        {
            current = current.Parent;
        }
        return current?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }
}
