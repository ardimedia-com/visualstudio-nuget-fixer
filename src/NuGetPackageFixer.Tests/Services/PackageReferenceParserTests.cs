namespace NuGetPackageFixer.Tests.Services;

using NuGetPackageFixer.Services;

[TestClass]
public class PackageReferenceParserTests
{
    private string _tempDir = null!;

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "NuGetFixerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void ParsesPackageReferenceAttribute()
    {
        var csproj = CreateFile("test.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Newtonsoft.Json" Version="13.0.4" />
                <PackageReference Include="Serilog" Version="4.3.1" />
              </ItemGroup>
            </Project>
            """);

        var result = PackageReferenceParser.Parse(csproj);

        Assert.AreEqual(2, result.Count);
        Assert.IsTrue(result.Any(p => p.Id == "Newtonsoft.Json" && p.Version == "13.0.4"));
        Assert.IsTrue(result.Any(p => p.Id == "Serilog" && p.Version == "4.3.1"));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void ParsesVersionAsChildElement()
    {
        var csproj = CreateFile("test.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Am.BaseSystem">
                  <Version>4.0.0-20260128-01</Version>
                </PackageReference>
              </ItemGroup>
            </Project>
            """);

        var result = PackageReferenceParser.Parse(csproj);

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("Am.BaseSystem", result[0].Id);
        Assert.AreEqual("4.0.0-20260128-01", result[0].Version);
        Assert.IsTrue(result[0].IsPrerelease);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void DetectsPrivateAssets()
    {
        var csproj = CreateFile("test.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="MSTest" Version="4.1.0" />
                <PackageReference Include="StyleCop.Analyzers" Version="1.2.0" PrivateAssets="all" />
              </ItemGroup>
            </Project>
            """);

        var result = PackageReferenceParser.Parse(csproj);

        Assert.AreEqual(2, result.Count);
        Assert.IsFalse(result.First(p => p.Id == "MSTest").IsDevelopmentDependency);
        Assert.IsTrue(result.First(p => p.Id == "StyleCop.Analyzers").IsDevelopmentDependency);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void SkipsPackagesWithoutVersion()
    {
        // CPM packages have no Version attribute
        var csproj = CreateFile("test.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Newtonsoft.Json" />
                <PackageReference Include="Serilog" Version="4.3.1" />
              </ItemGroup>
            </Project>
            """);

        var result = PackageReferenceParser.Parse(csproj);

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("Serilog", result[0].Id);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void HandlesOldStyleCsprojWithNamespace()
    {
        var csproj = CreateFile("test.csproj", """
            <Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
              <ItemGroup>
                <PackageReference Include="Newtonsoft.Json" Version="13.0.4" />
              </ItemGroup>
            </Project>
            """);

        var result = PackageReferenceParser.Parse(csproj);

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("Newtonsoft.Json", result[0].Id);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void HandlesNonExistentFile()
    {
        var result = PackageReferenceParser.Parse(Path.Combine(_tempDir, "nonexistent.csproj"));

        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void HandlesMultipleItemGroups()
    {
        var csproj = CreateFile("test.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Newtonsoft.Json" Version="13.0.4" />
              </ItemGroup>
              <ItemGroup>
                <PackageReference Include="Serilog" Version="4.3.1" />
              </ItemGroup>
            </Project>
            """);

        var result = PackageReferenceParser.Parse(csproj);

        Assert.AreEqual(2, result.Count);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void DetectsConditionOnElement()
    {
        var csproj = CreateFile("test.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="BenchmarkDotNet" Version="0.13.5" Condition="'$(Configuration)' == 'Debug'" />
                <PackageReference Include="Newtonsoft.Json" Version="13.0.4" />
              </ItemGroup>
            </Project>
            """);

        var result = PackageReferenceParser.Parse(csproj);

        Assert.AreEqual(2, result.Count);
        Assert.IsTrue(result.First(p => p.Id == "BenchmarkDotNet").HasCondition);
        Assert.IsFalse(result.First(p => p.Id == "Newtonsoft.Json").HasCondition);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void DetectsConditionOnItemGroup()
    {
        var csproj = CreateFile("test.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup Condition="'$(TargetFramework)' == 'net48'">
                <PackageReference Include="System.Memory" Version="4.5.5" />
              </ItemGroup>
              <ItemGroup>
                <PackageReference Include="Serilog" Version="4.3.1" />
              </ItemGroup>
            </Project>
            """);

        var result = PackageReferenceParser.Parse(csproj);

        Assert.AreEqual(2, result.Count);
        Assert.IsTrue(result.First(p => p.Id == "System.Memory").HasCondition);
        Assert.IsFalse(result.First(p => p.Id == "Serilog").HasCondition);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void DetectsFloatingVersion()
    {
        var csproj = CreateFile("test.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="AutoMapper" Version="12.*" />
                <PackageReference Include="FluentAssertions" Version="6.12.0" />
              </ItemGroup>
            </Project>
            """);

        var result = PackageReferenceParser.Parse(csproj);

        Assert.AreEqual(2, result.Count);
        Assert.IsTrue(result.First(p => p.Id == "AutoMapper").IsFloating);
        Assert.IsFalse(result.First(p => p.Id == "FluentAssertions").IsFloating);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void DetectsVersionOverrideAttribute()
    {
        var csproj = CreateFile("test.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="FluentValidation" Version="11.5.0" VersionOverride="11.6.0" />
                <PackageReference Include="Polly" Version="8.0.0" />
              </ItemGroup>
            </Project>
            """);

        var result = PackageReferenceParser.Parse(csproj);

        Assert.AreEqual(2, result.Count);
        Assert.IsTrue(result.First(p => p.Id == "FluentValidation").HasVersionOverride);
        Assert.IsFalse(result.First(p => p.Id == "Polly").HasVersionOverride);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void DetectsVersionOverrideElement()
    {
        var csproj = CreateFile("test.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Dapper">
                  <Version>2.0.123</Version>
                  <VersionOverride>2.1.0</VersionOverride>
                </PackageReference>
                <PackageReference Include="Moq" Version="4.18.4" />
              </ItemGroup>
            </Project>
            """);

        var result = PackageReferenceParser.Parse(csproj);

        Assert.AreEqual(2, result.Count);
        Assert.IsTrue(result.First(p => p.Id == "Dapper").HasVersionOverride);
        Assert.IsFalse(result.First(p => p.Id == "Moq").HasVersionOverride);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void ProjectUsesCpm_ManagePackageVersionsCentrally_True()
    {
        var csproj = CreateFile("test.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Newtonsoft.Json" />
              </ItemGroup>
            </Project>
            """);

        var result = PackageReferenceParser.ProjectUsesCpm(csproj);

        Assert.IsTrue(result);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void ProjectUsesCpm_DirectoryPackagesProps_True()
    {
        // Create Directory.Packages.props in the temp directory
        CreateFile("Directory.Packages.props", """
            <Project>
              <PropertyGroup>
                <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
              </PropertyGroup>
            </Project>
            """);

        var csproj = CreateFile("test.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Newtonsoft.Json" />
              </ItemGroup>
            </Project>
            """);

        var result = PackageReferenceParser.ProjectUsesCpm(csproj);

        Assert.IsTrue(result);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void ProjectUsesCpm_NoIndicators_False()
    {
        var csproj = CreateFile("test.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Newtonsoft.Json" Version="13.0.4" />
              </ItemGroup>
            </Project>
            """);

        var result = PackageReferenceParser.ProjectUsesCpm(csproj);

        Assert.IsFalse(result);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void ProjectUsesCpm_NonExistentFile_False()
    {
        var result = PackageReferenceParser.ProjectUsesCpm(Path.Combine(_tempDir, "nonexistent.csproj"));

        Assert.IsFalse(result);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void ProjectUsesCpm_ExplicitFalse_IgnoresDirectoryPackagesProps()
    {
        // Directory.Packages.props alone would normally trigger CPM detection...
        CreateFile("Directory.Packages.props", """
            <Project>
              <PropertyGroup>
                <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
              </PropertyGroup>
            </Project>
            """);

        // ...but the project explicitly opts out.
        var csproj = CreateFile("test.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Newtonsoft.Json" Version="13.0.4" />
              </ItemGroup>
            </Project>
            """);

        var result = PackageReferenceParser.ProjectUsesCpm(csproj);

        Assert.IsFalse(result);
    }

    private string CreateFile(string name, string content)
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, content);
        return path;
    }
}
