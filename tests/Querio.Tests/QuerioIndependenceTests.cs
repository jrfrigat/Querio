using System.Reflection;

namespace Querio.Tests;

/// <summary>
/// Architecture-boundary guard for every shipped package.
/// <para>
/// Querio's selling point is that it can be dropped into something that already exists - a worker, a
/// console tool, an old service nobody wants to retarget - without an argument about dependencies.
/// That property is easy to lose one convenient reference at a time, so it is enforced by a failing
/// build rather than by good intentions.
/// </para>
/// </summary>
public sealed class QuerioIndependenceTests
{
    private static readonly string[] ForbiddenPrefixes =
    [
        // A UI library has no business inside a query model, whichever one it is.
        "Flare",
        // Nor does the web stack: this has to stay usable well away from a web host.
        "Microsoft.AspNetCore",
        "Microsoft.JSInterop",
        "Microsoft.Extensions",
        // Nor an ORM. Querio.Linq builds expression trees that Entity Framework happens to consume,
        // which is exactly why it must not reference it.
        "Microsoft.EntityFrameworkCore",
        "Dapper",
    ];

    /// <summary>
    /// Every shipped assembly, found through a type in each rather than by scanning a directory, so
    /// a package that stopped being built fails the test instead of quietly leaving it.
    /// </summary>
    public static TheoryData<string> ShippedAssemblies() =>
    [
        typeof(QuerySpec).Assembly.GetName().Name!,
        typeof(Querio.Sql.SqlRenderer).Assembly.GetName().Name!,
        typeof(Querio.OneC.OneCRenderer).Assembly.GetName().Name!,
        typeof(Querio.Text.QueryDescriber).Assembly.GetName().Name!,
        typeof(Querio.Linq.QueryExecutor).Assembly.GetName().Name!,
        typeof(Querio.Http.QueryHttp).Assembly.GetName().Name!,
        typeof(Querio.Language.QueryLanguage).Assembly.GetName().Name!,
    ];

    private static Assembly Load(string name) => Assembly.Load(name);

    [Theory]
    [MemberData(nameof(ShippedAssemblies))]
    public void EveryPackageDependsOnNothingButTheBaseClassLibraryAndQuerio(string assemblyName)
    {
        var offenders = Load(assemblyName).GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .Where(IsForbidden)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.True(offenders.Length == 0,
            $"{assemblyName} references {string.Join(", ", offenders)}. Querio packages take no "
            + "dependency beyond the base class library and the Querio core, so they can be added to "
            + "an existing application without dragging anything in behind them.");
    }

    /// <summary>
    /// The same rule stated the way the mandate states it. The list above names the specific
    /// temptations and says why each one is refused, which is worth keeping for the error message it
    /// produces - but a deny-list only refuses what somebody thought to write down, and "no
    /// dependencies" is a claim about everything else too. A reference to a package nobody predicted
    /// would pass that test and fail this one.
    /// </summary>
    [Theory]
    [MemberData(nameof(ShippedAssemblies))]
    public void EveryPackageReferencesNothingButTheBaseClassLibraryAndQuerio(string assemblyName)
    {
        var strangers = Load(assemblyName).GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .Where(name => !IsBaseClassLibrary(name) && !IsQuerio(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.True(strangers.Length == 0,
            $"{assemblyName} references {string.Join(", ", strangers)}, which is neither the base class "
            + "library nor Querio. Adding a dependency to a shipped package is a product decision: it "
            + "is the property that lets Querio be dropped into something that already exists.");
    }

    [Theory]
    [MemberData(nameof(ShippedAssemblies))]
    public void EveryPackageClaimsOnlyItsOwnNamespace(string assemblyName)
    {
        var assembly = Load(assemblyName);
        var strays = assembly.GetExportedTypes()
            .Where(type => type.Namespace is null
                || !(type.Namespace.Equals(assemblyName, StringComparison.Ordinal)
                    || type.Namespace.StartsWith(assemblyName + ".", StringComparison.Ordinal)))
            .Select(type => type.FullName ?? type.Name)
            .ToArray();

        Assert.True(strays.Length == 0,
            $"{assemblyName} exports types outside its own namespace, so the package claims a name it "
            + $"does not own. Found: {string.Join(", ", strays)}");
    }

    [Theory]
    [MemberData(nameof(ShippedAssemblies))]
    public void EveryPackageShipsItsDocumentation(string assemblyName)
    {
        // The XML file drives the generated API reference, and CS1591 is an error repo-wide, so a
        // missing file means the packaging changed rather than one comment going astray.
        var assembly = Load(assemblyName);
        var documentation = Path.ChangeExtension(assembly.Location, ".xml");

        Assert.True(File.Exists(documentation),
            $"Expected generated XML documentation next to {assembly.Location}.");
    }

    // The reference assemblies a library gets for free on every target it builds for. netstandard2.0
    // resolves through the netstandard facade; net8 and later through System.*.
    private static bool IsBaseClassLibrary(string assemblyName)
        => assemblyName.Equals("netstandard", StringComparison.OrdinalIgnoreCase)
            || assemblyName.Equals("mscorlib", StringComparison.OrdinalIgnoreCase)
            || assemblyName.Equals("System", StringComparison.OrdinalIgnoreCase)
            || assemblyName.StartsWith("System.", StringComparison.OrdinalIgnoreCase);

    private static bool IsQuerio(string assemblyName)
        => assemblyName.Equals("Querio", StringComparison.OrdinalIgnoreCase)
            || assemblyName.StartsWith("Querio.", StringComparison.OrdinalIgnoreCase);

    private static bool IsForbidden(string assemblyName)
        => ForbiddenPrefixes.Any(prefix =>
            assemblyName.Equals(prefix, StringComparison.OrdinalIgnoreCase)
            || assemblyName.StartsWith(prefix + ".", StringComparison.OrdinalIgnoreCase));
}
