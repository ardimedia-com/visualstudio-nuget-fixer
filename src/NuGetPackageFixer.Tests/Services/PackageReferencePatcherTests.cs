namespace NuGetPackageFixer.Tests.Services;

using NuGetPackageFixer.Services;

[TestClass]
public class PackageReferencePatcherTests
{
    [TestMethod]
    [TestCategory("Unit")]
    public void UpdateVersion_AttributeForm_Success()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Newtonsoft.Json" Version="13.0.1" />
              </ItemGroup>
            </Project>
            """);

        // Act
        var result = PackageReferencePatcher.UpdateVersion(tempFile, "Newtonsoft.Json", "13.0.1", "13.0.3");

        // Assert
        Assert.AreEqual(UpdateStatus.Success, result.Status);
        var content = File.ReadAllText(tempFile);
        Assert.Contains("Version=\"13.0.3\"", content);
        Assert.DoesNotContain("Version=\"13.0.1\"", content);

        // Cleanup
        File.Delete(tempFile);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void UpdateVersion_ChildElementForm_Success()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Serilog">
                  <Version>3.0.1</Version>
                </PackageReference>
              </ItemGroup>
            </Project>
            """);

        // Act
        var result = PackageReferencePatcher.UpdateVersion(tempFile, "Serilog", "3.0.1", "3.1.0");

        // Assert
        Assert.AreEqual(UpdateStatus.Success, result.Status);
        var content = File.ReadAllText(tempFile);
        Assert.Contains("<Version>3.1.0</Version>", content);
        Assert.DoesNotContain("<Version>3.0.1</Version>", content);

        // Cleanup
        File.Delete(tempFile);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void UpdateVersion_ConditionOnElement_Skipped()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Microsoft.Extensions.Logging" Version="8.0.0" Condition="'$(TargetFramework)' == 'net8.0'" />
              </ItemGroup>
            </Project>
            """);

        // Act
        var result = PackageReferencePatcher.UpdateVersion(tempFile, "Microsoft.Extensions.Logging", "8.0.0", "9.0.0");

        // Assert
        Assert.AreEqual(UpdateStatus.SkippedConditional, result.Status);
        Assert.Contains("Condition attribute", result.Message);

        // Cleanup
        File.Delete(tempFile);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void UpdateVersion_ConditionOnItemGroup_Skipped()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup Condition="'$(Configuration)' == 'Debug'">
                <PackageReference Include="BenchmarkDotNet" Version="0.13.5" />
              </ItemGroup>
            </Project>
            """);

        // Act
        var result = PackageReferencePatcher.UpdateVersion(tempFile, "BenchmarkDotNet", "0.13.5", "0.13.6");

        // Assert
        Assert.AreEqual(UpdateStatus.SkippedConditional, result.Status);

        // Cleanup
        File.Delete(tempFile);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void UpdateVersion_FloatingVersion_Skipped()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="AutoMapper" Version="12.*" />
              </ItemGroup>
            </Project>
            """);

        // Act
        var result = PackageReferencePatcher.UpdateVersion(tempFile, "AutoMapper", "12.*", "13.0.0");

        // Assert
        Assert.AreEqual(UpdateStatus.SkippedFloating, result.Status);
        Assert.Contains("Floating version", result.Message);

        // Cleanup
        File.Delete(tempFile);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void UpdateVersion_VersionOverrideAttribute_Skipped()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="FluentValidation" Version="11.5.0" VersionOverride="11.6.0" />
              </ItemGroup>
            </Project>
            """);

        // Act
        var result = PackageReferencePatcher.UpdateVersion(tempFile, "FluentValidation", "11.5.0", "11.7.0");

        // Assert
        Assert.AreEqual(UpdateStatus.SkippedVersionOverride, result.Status);

        // Cleanup
        File.Delete(tempFile);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void UpdateVersion_VersionOverrideElement_Skipped()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Polly">
                  <Version>8.0.0</Version>
                  <VersionOverride>8.1.0</VersionOverride>
                </PackageReference>
              </ItemGroup>
            </Project>
            """);

        // Act
        var result = PackageReferencePatcher.UpdateVersion(tempFile, "Polly", "8.0.0", "8.2.0");

        // Assert
        Assert.AreEqual(UpdateStatus.SkippedVersionOverride, result.Status);

        // Cleanup
        File.Delete(tempFile);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void UpdateVersion_PackageNotFound_NotFound()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="xunit" Version="2.4.2" />
              </ItemGroup>
            </Project>
            """);

        // Act
        var result = PackageReferencePatcher.UpdateVersion(tempFile, "NUnit", "3.13.0", "3.14.0");

        // Assert
        Assert.AreEqual(UpdateStatus.NotFound, result.Status);

        // Cleanup
        File.Delete(tempFile);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void UpdateVersion_VersionMismatch_NotFound()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Moq" Version="4.18.4" />
              </ItemGroup>
            </Project>
            """);

        // Act
        var result = PackageReferencePatcher.UpdateVersion(tempFile, "Moq", "4.18.0", "4.20.0");

        // Assert
        Assert.AreEqual(UpdateStatus.NotFound, result.Status);
        Assert.Contains("Version mismatch", result.Message);

        // Cleanup
        File.Delete(tempFile);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void UpdateVersion_MultiplePackageReferences_Ambiguous()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Dapper" Version="2.0.123" />
              </ItemGroup>
              <ItemGroup>
                <PackageReference Include="Dapper" Version="2.0.123" />
              </ItemGroup>
            </Project>
            """);

        // Act
        var result = PackageReferencePatcher.UpdateVersion(tempFile, "Dapper", "2.0.123", "2.1.0");

        // Assert
        Assert.AreEqual(UpdateStatus.SkippedAmbiguous, result.Status);
        Assert.Contains("Multiple", result.Message);

        // Cleanup
        File.Delete(tempFile);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void UpdateVersion_PreservesFormatting()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        var originalContent = """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>

              <!-- Comment -->
              <ItemGroup>
                <PackageReference Include="CsvHelper" Version="30.0.1" />
                <PackageReference Include="ClosedXML" Version="0.102.0" />
              </ItemGroup>
            </Project>
            """;
        File.WriteAllText(tempFile, originalContent);

        // Act
        var result = PackageReferencePatcher.UpdateVersion(tempFile, "CsvHelper", "30.0.1", "31.0.0");

        // Assert
        Assert.AreEqual(UpdateStatus.Success, result.Status);
        var content = File.ReadAllText(tempFile);
        Assert.Contains("<!-- Comment -->", content);
        Assert.Contains("ClosedXML", content);
        Assert.Contains("Version=\"31.0.0\"", content);

        // Cleanup
        File.Delete(tempFile);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void UpdateVersion_NoVersion_SkippedCpm()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="NodaTime" />
              </ItemGroup>
            </Project>
            """);

        // Act
        var result = PackageReferencePatcher.UpdateVersion(tempFile, "NodaTime", "3.1.0", "3.2.0");

        // Assert
        Assert.AreEqual(UpdateStatus.SkippedCpm, result.Status);
        Assert.Contains("No Version found", result.Message);

        // Cleanup
        File.Delete(tempFile);
    }
}
