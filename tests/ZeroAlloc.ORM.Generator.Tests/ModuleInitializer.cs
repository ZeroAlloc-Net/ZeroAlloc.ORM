using System.IO;
using System.Runtime.CompilerServices;
using VerifyTests;
using VerifyXunit;

namespace ZeroAlloc.ORM.Generator.Tests;

internal static class ModuleInitializer
{
    [System.Runtime.CompilerServices.ModuleInitializer]
    public static void Init()
    {
        VerifySourceGenerators.Initialize();
        Verifier.DerivePathInfo((sourceFile, projectDirectory, type, method) =>
            new PathInfo(
                directory: Path.Combine(projectDirectory, "Snapshots"),
                typeName: type.Name,
                methodName: method.Name));
    }
}
