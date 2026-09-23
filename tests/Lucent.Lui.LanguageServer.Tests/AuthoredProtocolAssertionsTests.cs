using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lucent.Lui.LanguageServer.Tests;

[TestClass]
public sealed class AuthoredProtocolAssertionsTests
{
    [TestMethod]
    public void AuthoredProtocolOraclesPreserveUtf16CrlfAndRejectExtraOrDuplicateResults()
    {
        var fixture = AuthoredFixture.Parse(
            "🧭/*<first>*/Count/*</first>*/\r\n/*<second>*/Value/*</second>*/"
        );
        Assert.AreEqual("🧭Count\r\nValue", fixture.Source);
        Assert.AreEqual(new AuthoredPosition(0, 2), fixture.Position("first"));
        Assert.AreEqual(new AuthoredPosition(1, 0), fixture.Position("second"));

        const string uri = "file:///fixture.lui";
        var first = fixture.Location(uri, "first");
        using var duplicateLocations = JsonDocument.Parse(
            """
            [
              {"uri":"file:///fixture.lui","range":{"start":{"line":0,"character":2},"end":{"line":0,"character":7}}},
              {"uri":"file:///fixture.lui","range":{"start":{"line":0,"character":2},"end":{"line":0,"character":7}}}
            ]
            """
        );
        ExpectFailure(
            () => AuthoredProtocolAssertions.Locations(duplicateLocations.RootElement, first),
            "Duplicate actual location"
        );

        using var unexpectedLocation = JsonDocument.Parse(
            """
            [{"uri":"file:///unexpected.lui","range":{"start":{"line":0,"character":2},"end":{"line":0,"character":7}}}]
            """
        );
        ExpectFailure(
            () => AuthoredProtocolAssertions.Locations(unexpectedLocation.RootElement, first),
            "Unexpected: file:///unexpected.lui"
        );

        var expectedEdit = fixture.Edit(uri, "first", "Total");
        using var duplicateDocumentEdits = JsonDocument.Parse(
            """
            {"changes":{
              "file:///fixture.lui":[{"range":{"start":{"line":0,"character":2},"end":{"line":0,"character":7}},"newText":"Total"}],
              "file:///fixture.lui":[{"range":{"start":{"line":1,"character":0},"end":{"line":1,"character":5}},"newText":"Other"}]
            }}
            """
        );
        ExpectFailure(
            () =>
                AuthoredProtocolAssertions.WorkspaceEdits(
                    duplicateDocumentEdits.RootElement,
                    expectedEdit
                ),
            "Duplicate actual document edit key"
        );

        using var duplicateChanges = JsonDocument.Parse(
            """
            {
              "changes":{"file:///fixture.lui":[
                {"range":{"start":{"line":0,"character":2},"end":{"line":0,"character":7}},"newText":"Total"}
              ]},
              "changes":{"file:///fixture.lui":[
                {"range":{"start":{"line":0,"character":2},"end":{"line":0,"character":7}},"newText":"Total"}
              ]}
            }
            """
        );
        ExpectFailure(
            () =>
                AuthoredProtocolAssertions.WorkspaceEdits(
                    duplicateChanges.RootElement,
                    expectedEdit
                ),
            "exactly one 'changes' property"
        );

        using var unexpectedDocumentEdit = JsonDocument.Parse(
            """
            {"changes":{"file:///unexpected.lui":[
              {"range":{"start":{"line":0,"character":2},"end":{"line":0,"character":7}},"newText":"Total"}
            ]}}
            """
        );
        ExpectFailure(
            () =>
                AuthoredProtocolAssertions.WorkspaceEdits(
                    unexpectedDocumentEdit.RootElement,
                    expectedEdit
                ),
            "Unexpected: file:///unexpected.lui"
        );

        using var shiftedRangeEdit = JsonDocument.Parse(
            """
            {"changes":{"file:///fixture.lui":[
              {"range":{"start":{"line":0,"character":3},"end":{"line":0,"character":8}},"newText":"Total"}
            ]}}
            """
        );
        ExpectFailure(
            () =>
                AuthoredProtocolAssertions.WorkspaceEdits(
                    shiftedRangeEdit.RootElement,
                    expectedEdit
                ),
            "Exact workspace edits differed"
        );

        using var wrongReplacementEdit = JsonDocument.Parse(
            """
            {"changes":{"file:///fixture.lui":[
              {"range":{"start":{"line":0,"character":2},"end":{"line":0,"character":7}},"newText":"Other"}
            ]}}
            """
        );
        ExpectFailure(
            () =>
                AuthoredProtocolAssertions.WorkspaceEdits(
                    wrongReplacementEdit.RootElement,
                    expectedEdit
                ),
            "=> Other"
        );

        using var mixedWorkspaceEditForms = JsonDocument.Parse(
            """
            {
              "changes":{"file:///fixture.lui":[
                {"range":{"start":{"line":0,"character":2},"end":{"line":0,"character":7}},"newText":"Total"}
              ]},
              "documentChanges":[]
            }
            """
        );
        ExpectFailure(
            () =>
                AuthoredProtocolAssertions.WorkspaceEdits(
                    mixedWorkspaceEditForms.RootElement,
                    expectedEdit
                ),
            "Unexpected workspace edit payload properties: documentChanges"
        );

        using var duplicateRangeEdits = JsonDocument.Parse(
            """
            {"changes":{"file:///fixture.lui":[
              {"range":{"start":{"line":0,"character":2},"end":{"line":0,"character":7}},"newText":"Total"},
              {"range":{"start":{"line":0,"character":2},"end":{"line":0,"character":7}},"newText":"Other"}
            ]}}
            """
        );
        ExpectFailure(
            () =>
                AuthoredProtocolAssertions.WorkspaceEdits(
                    duplicateRangeEdits.RootElement,
                    expectedEdit
                ),
            "Duplicate actual edit range"
        );

        static void ExpectFailure(Action action, string expectedMessage)
        {
            try
            {
                action();
                Assert.Fail("The deliberately invalid protocol result was accepted.");
            }
            catch (AssertFailedException exception)
            {
                StringAssert.Contains(exception.Message, expectedMessage);
            }
        }
    }
}
