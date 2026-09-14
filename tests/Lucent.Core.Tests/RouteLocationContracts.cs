using System.Text;
using Lucent.Core;

namespace Lucent.Core.Tests;

[TestClass]
public sealed class RouteLocationContracts
{
    private static readonly string[] CanonicalSegments = ["café", "/", "a"];

    [TestMethod]
    public void ParsesCanonicalUnicodePercentEncodingAndOrderedQueryPairs()
    {
        var result = RouteLocation.Parse("/café/%2f/%61?b=%E2%82%AC&a&a=");

        Assert.IsTrue(result.Succeeded);
        var location = result.Location!;
        Assert.AreEqual("/caf%C3%A9/%2F/a?b=%E2%82%AC&a&a=", location.CanonicalText);
        CollectionAssert.AreEqual(CanonicalSegments, location.Segments.ToArray());
        Assert.AreEqual(3, location.Query.Count);
        Assert.AreEqual("€", location.Query[0].Value);
        Assert.IsFalse(location.Query[1].HasValue);
        Assert.AreEqual(string.Empty, location.Query[1].Value);
        Assert.IsTrue(location.Query[2].HasValue);
        Assert.AreEqual(string.Empty, location.Query[2].Value);
    }

    [TestMethod]
    public void UnicodeNormalizationIsNotApplied()
    {
        var composed = RouteLocation.Parse("/é").Location!;
        var decomposed = RouteLocation.Parse("/é").Location!;

        Assert.AreEqual("/%C3%A9", composed.CanonicalText);
        Assert.AreEqual("/e%CC%81", decomposed.CanonicalText);
        Assert.AreNotEqual(composed, decomposed);
    }

    [TestMethod]
    [DataRow(null, RouteLocationErrorKind.Empty)]
    [DataRow("", RouteLocationErrorKind.Empty)]
    [DataRow("relative", RouteLocationErrorKind.NotAbsolutePath)]
    [DataRow("//authority", RouteLocationErrorKind.AuthorityNotAllowed)]
    [DataRow("/a/", RouteLocationErrorKind.EmptyPathSegment)]
    [DataRow("/a//b", RouteLocationErrorKind.EmptyPathSegment)]
    [DataRow("/a#fragment", RouteLocationErrorKind.FragmentNotAllowed)]
    [DataRow("/a\\b", RouteLocationErrorKind.BackslashNotAllowed)]
    [DataRow("/a:b", RouteLocationErrorKind.InvalidCharacter)]
    [DataRow("/a@b", RouteLocationErrorKind.InvalidCharacter)]
    [DataRow("/a%5Cb", RouteLocationErrorKind.BackslashNotAllowed)]
    [DataRow("/%00", RouteLocationErrorKind.ControlCharacterNotAllowed)]
    [DataRow("/%", RouteLocationErrorKind.InvalidPercentEncoding)]
    [DataRow("/%GG", RouteLocationErrorKind.InvalidPercentEncoding)]
    [DataRow("/%C3", RouteLocationErrorKind.InvalidUtf8)]
    [DataRow("/.", RouteLocationErrorKind.DotPathSegment)]
    [DataRow("/%2e%2e", RouteLocationErrorKind.DotPathSegment)]
    [DataRow("/a?", RouteLocationErrorKind.EmptyQuery)]
    [DataRow("/a?=x", RouteLocationErrorKind.EmptyQueryKey)]
    [DataRow("/a?x&&y", RouteLocationErrorKind.EmptyQueryPair)]
    [DataRow("/a?x&", RouteLocationErrorKind.EmptyQueryPair)]
    public void RejectsMalformedAndHostileLocations(string? text, RouteLocationErrorKind expected)
    {
        var result = RouteLocation.Parse(text);

        Assert.IsFalse(result.Succeeded);
        Assert.IsNull(result.Location);
        Assert.AreEqual(expected, result.Error.Kind);
        Assert.IsTrue(result.Error.Utf16Offset >= 0);
    }

    [TestMethod]
    public void RejectsInvalidRawUnicodeWithoutEchoingIt()
    {
        var result = RouteLocation.Parse("/\ud800");

        Assert.AreEqual(RouteLocationErrorKind.InvalidUnicode, result.Error.Kind);
        Assert.IsFalse(result.Error.ToString().Contains('\ud800'));
    }

    [TestMethod]
    public void EncodedReservedDataRemainsInOneCanonicalSegment()
    {
        var location = RouteLocation.Parse("/%3a%40%3f%23%2f").Location!;

        Assert.AreEqual(":@?#/", location.Segments.Single());
        Assert.AreEqual("/%3A%40%3F%23%2F", location.CanonicalText);
    }

    [TestMethod]
    public void PercentRunErrorsReportTheRunSourceOffset()
    {
        var result = RouteLocation.Parse("/%C3%A9%00");

        Assert.AreEqual(RouteLocationErrorKind.ControlCharacterNotAllowed, result.Error.Kind);
        Assert.AreEqual(1, result.Error.Utf16Offset);
    }

    [TestMethod]
    public void AppliesEveryDefaultBoundAtItsEdge()
    {
        var thirtyTwoSegments = "/" + string.Join('/', Enumerable.Repeat("a", 32));
        Assert.IsTrue(RouteLocation.Parse(thirtyTwoSegments).Succeeded);
        Assert.AreEqual(
            RouteLocationErrorKind.TooManySegments,
            RouteLocation.Parse(thirtyTwoSegments + "/a").Error.Kind
        );

        Assert.IsTrue(RouteLocation.Parse("/" + new string('a', 256)).Succeeded);
        Assert.AreEqual(
            RouteLocationErrorKind.PathSegmentTooLong,
            RouteLocation.Parse("/" + new string('a', 257)).Error.Kind
        );

        var thirtyTwoPairs = "/?" + string.Join('&', Enumerable.Repeat("a=", 32));
        Assert.IsTrue(RouteLocation.Parse(thirtyTwoPairs).Succeeded);
        Assert.AreEqual(
            RouteLocationErrorKind.TooManyQueryPairs,
            RouteLocation.Parse(thirtyTwoPairs + "&a=").Error.Kind
        );

        Assert.IsTrue(RouteLocation.Parse("/?" + new string('k', 64) + "=").Succeeded);
        Assert.AreEqual(
            RouteLocationErrorKind.QueryKeyTooLong,
            RouteLocation.Parse("/?" + new string('k', 65) + "=").Error.Kind
        );

        Assert.IsTrue(RouteLocation.Parse("/?k=" + new string('v', 1024)).Succeeded);
        Assert.AreEqual(
            RouteLocationErrorKind.QueryValueTooLong,
            RouteLocation.Parse("/?k=" + new string('v', 1025)).Error.Kind
        );
    }

    [TestMethod]
    public void BoundsSuppliedAndCanonicalUtf8Independently()
    {
        var suppliedLimits = new RouteLocationLimits(
            maximumUtf8Bytes: 8,
            maximumSegments: 4,
            maximumSegmentUtf8Bytes: 32,
            maximumQueryPairs: 4,
            maximumQueryKeyUtf8Bytes: 8,
            maximumQueryValueUtf8Bytes: 8
        );
        Assert.IsTrue(RouteLocation.Parse("/1234567", suppliedLimits).Succeeded);
        Assert.AreEqual(
            RouteLocationErrorKind.InputTooLong,
            RouteLocation.Parse("/12345678", suppliedLimits).Error.Kind
        );

        var canonicalLimits = suppliedLimits with { };
        Assert.AreEqual(
            RouteLocationErrorKind.CanonicalTooLong,
            RouteLocation.Parse("/éé", canonicalLimits).Error.Kind
        );
    }

    [TestMethod]
    public void CheapLengthBoundPrecedesScanningOversizedInvalidUnicode()
    {
        var limits = new RouteLocationLimits(
            maximumUtf8Bytes: 1,
            maximumSegments: 1,
            maximumSegmentUtf8Bytes: 1,
            maximumQueryPairs: 1,
            maximumQueryKeyUtf8Bytes: 1,
            maximumQueryValueUtf8Bytes: 1
        );

        Assert.AreEqual(
            RouteLocationErrorKind.InputTooLong,
            RouteLocation.Parse("/\ud800", limits).Error.Kind
        );
    }

    [TestMethod]
    public void PublicLimitsRequirePositiveFiniteCounts()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new RouteLocationLimits(maximumUtf8Bytes: 0)
        );
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new RouteLocationLimits(maximumSegments: 0)
        );
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new RouteLocationLimits(maximumSegmentUtf8Bytes: 0)
        );
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new RouteLocationLimits(maximumQueryPairs: 0)
        );
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new RouteLocationLimits(maximumQueryKeyUtf8Bytes: 0)
        );
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new RouteLocationLimits(maximumQueryValueUtf8Bytes: 0)
        );
    }

    [TestMethod]
    public void LocationCollectionsAndTextRepresentationsAreSafe()
    {
        var location = RouteLocation.Parse("/secret?token=credential").Location!;

        Assert.ThrowsExactly<NotSupportedException>(() =>
            ((IList<string>)location.Segments)[0] = "changed"
        );
        Assert.IsFalse(location.ToString().Contains("secret", StringComparison.Ordinal));
        Assert.IsFalse(location.ToString().Contains("credential", StringComparison.Ordinal));
        Assert.AreEqual("/secret?token=credential", location.CanonicalText);
    }
}
