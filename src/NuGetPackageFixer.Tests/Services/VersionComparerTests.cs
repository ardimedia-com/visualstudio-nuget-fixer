namespace NuGetPackageFixer.Tests.Services;

using NuGetPackageFixer.Models;
using NuGetPackageFixer.Services;

[TestClass]
public class VersionComparerTests
{
    [TestMethod]
    [TestCategory("Unit")]
    public void DetectsPatchUpdate()
    {
        var result = VersionComparer.Compare("4.14.1", "4.15.1");

        Assert.IsTrue(result.IsNewer);
        Assert.IsFalse(result.IsMajor);
        Assert.IsFalse(result.IsBogus);
        Assert.AreEqual(UpdateType.Patch, result.UpdateType);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void DetectsMajorUpdate()
    {
        var result = VersionComparer.Compare("6.1.4", "7.0.0");

        Assert.IsTrue(result.IsNewer);
        Assert.IsTrue(result.IsMajor);
        Assert.AreEqual(UpdateType.Major, result.UpdateType);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void DetectsBogusVersion_TooHigh()
    {
        // System.ComponentModel.Composition 10.0.2 -> 2010.2.11.1
        var result = VersionComparer.Compare("10.0.2", "2010.2.11.1");

        Assert.IsFalse(result.IsNewer);
        Assert.IsTrue(result.IsBogus);
        Assert.AreEqual(UpdateType.Bogus, result.UpdateType);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void DetectsBogusVersion_Zero()
    {
        // System.Data.DataSetExtensions 4.5.0 -> 0
        var result = VersionComparer.Compare("4.5.0", "0");

        Assert.IsFalse(result.IsNewer);
        Assert.IsTrue(result.IsBogus);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void NormalizesVersionPadding()
    {
        // Owin 1.0 vs 1.0.0 -- should not be detected as newer
        var result = VersionComparer.Compare("1.0", "1.0.0");

        Assert.IsFalse(result.IsNewer);
        Assert.IsFalse(result.IsBogus);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void ComparesPrereleaseSuffix()
    {
        var result = VersionComparer.Compare("4.0.0-20260128-01", "4.0.0-20260323-01");

        Assert.IsTrue(result.IsNewer);
        Assert.IsFalse(result.IsMajor);
        Assert.AreEqual(UpdateType.Prerelease, result.UpdateType);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void DetectsStablePromotion()
    {
        // 4.0.0-rc1 -> 4.0.0
        var result = VersionComparer.Compare("4.0.0-rc1", "4.0.0");

        Assert.IsTrue(result.IsNewer);
        Assert.AreEqual(UpdateType.StablePromotion, result.UpdateType);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void SameVersionIsNotNewer()
    {
        var result = VersionComparer.Compare("4.14.1", "4.14.1");

        Assert.IsFalse(result.IsNewer);
        Assert.AreEqual(UpdateType.UpToDate, result.UpdateType);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void MinorUpdate_SameMajor()
    {
        var result = VersionComparer.Compare("10.0.2", "10.0.5");

        Assert.IsTrue(result.IsNewer);
        Assert.IsFalse(result.IsMajor);
        Assert.AreEqual(UpdateType.Patch, result.UpdateType);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void HandlesNullAndEmpty()
    {
        Assert.IsFalse(VersionComparer.Compare("", "1.0.0").IsNewer);
        Assert.IsFalse(VersionComparer.Compare("1.0.0", "").IsNewer);
        Assert.IsFalse(VersionComparer.Compare(null!, null!).IsNewer);
    }
}
