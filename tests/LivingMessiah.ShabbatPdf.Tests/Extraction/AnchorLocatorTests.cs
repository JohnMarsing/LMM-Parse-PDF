using LivingMessiah.ShabbatPdf.Core.Extraction;
using LivingMessiah.ShabbatPdf.Core.Models;
using LivingMessiah.ShabbatPdf.Core.Options;

namespace LivingMessiah.ShabbatPdf.Tests.Extraction;

public class AnchorLocatorTests
{
    private static PdfPageText Page(int n, params string[] lines) =>
        new(n, lines);

    private static List<PdfPageText> SampleAgendaPages(
        string? endLine = "The Avinu Prayer",
        IReadOnlyList<string>? endLines = null)
    {
        // Mirrors design sample structure: 86 start, 87 intro, 88+ teaching, 114 avinu
        var end = endLines ?? [endLine!];

        return
        [
            Page(2, "Welcomes You", "to our service"),
            Page(66, "You are welcome anytime"),
            Page(86, "Welcome", "Bienvenido"),
            Page(87,
                "Unless noted otherwise all text in English",
                "What will talk about today?",
                "Fair Use Policy and Legal Disclaimer",
                "Section 107 of the Copyright Act"),
            Page(88, "Jude 6 And angels who did not keep their domain"),
            Page(95, "TOTAL SURRENDER"),
            Page(113, "Last teaching line"),
            Page(114, end.ToArray()),
        ];
    }

    [Fact]
    public void Locate_FindsWelcomeBienvenido_SkipsIntro_EndsBeforeAvinu()
    {
        var locator = new AnchorLocator();
        var result = locator.Locate(SampleAgendaPages());

        Assert.Equal(86, result.StartAnchorPage);
        Assert.Equal(114, result.EndAnchorPage);
        Assert.Equal(87, result.ProvisionalContentStartPage);
        Assert.Equal(88, result.ContentStartPage);
        Assert.Equal(113, result.ContentEndPage);
        Assert.Equal(AnchorLocator.EndMatchLine, result.EndMatchMethod);
        Assert.Equal([87], result.IntroSkippedPages);
    }

    [Fact]
    public void Locate_RejectsWelcomesYouWithoutBienvenido()
    {
        var pages = new List<PdfPageText>
        {
            Page(2, "Welcomes You"),
            Page(3, "The Avinu Prayer"),
        };

        var ex = Assert.Throws<AnchorException>(() => new AnchorLocator().Locate(pages));
        Assert.Equal("AnchorNotFound:Start", ex.Code);
    }

    [Fact]
    public void Locate_AcceptsBienvenidosPlural()
    {
        var pages = new List<PdfPageText>
        {
            Page(1, "Welcome", "Bienvenidos"),
            Page(2, "Teaching"),
            Page(3, "The Avinu Prayer"),
        };

        var result = new AnchorLocator().Locate(pages);
        Assert.Equal(1, result.StartAnchorPage);
        Assert.Equal(2, result.ContentStartPage);
        Assert.Equal(2, result.ContentEndPage);
    }

    [Fact]
    public void Locate_EndMatch_CollapsedPhrase()
    {
        // Phrase appears mid-line (not at start), so Line + MultiLine miss; Collapsed hits.
        var pages = new List<PdfPageText>
        {
            Page(1, "Welcome", "Bienvenido"),
            Page(2, "Teaching content"),
            Page(3, "Closing title: The Avinu Prayer is next"),
        };

        var result = new AnchorLocator().Locate(pages);
        Assert.Equal(3, result.EndAnchorPage);
        Assert.Equal(AnchorLocator.EndMatchCollapsed, result.EndMatchMethod);
    }

    [Fact]
    public void Locate_EndMatch_MultiLineSequence()
    {
        var pages = new List<PdfPageText>
        {
            Page(1, "Welcome", "Bienvenido"),
            Page(2, "Teaching"),
            Page(3, "The Avinu", "Prayer"),
        };

        var result = new AnchorLocator().Locate(pages);

        Assert.Equal(3, result.EndAnchorPage);
        Assert.Equal(AnchorLocator.EndMatchMultiLineSequence, result.EndMatchMethod);
        Assert.Equal(2, result.ContentEndPage);
    }

    [Fact]
    public void Locate_MissingEnd_Throws()
    {
        var pages = new List<PdfPageText>
        {
            Page(1, "Welcome", "Bienvenido"),
            Page(2, "Only teaching"),
        };

        var ex = Assert.Throws<AnchorException>(() => new AnchorLocator().Locate(pages));
        Assert.Equal("AnchorNotFound:End", ex.Code);
    }

    [Fact]
    public void Locate_OnlyIntroBetweenAnchors_ThrowsEmptySlice()
    {
        var pages = new List<PdfPageText>
        {
            Page(1, "Welcome", "Bienvenido"),
            Page(2, "Fair Use Policy"),
            Page(3, "The Avinu Prayer"),
        };

        var ex = Assert.Throws<AnchorException>(() => new AnchorLocator().Locate(pages));
        Assert.Equal("EmptySlice", ex.Code);
    }

    [Fact]
    public void IsIntroSkipPage_MatchesConfiguredPatterns()
    {
        var locator = new AnchorLocator();

        Assert.True(locator.IsIntroSkipPage(Page(87, "Fair Use Policy and Legal Disclaimer")));
        Assert.True(locator.IsIntroSkipPage(Page(87, "What will talk about today?")));
        Assert.False(locator.IsIntroSkipPage(Page(88, "Jude 6 And angels")));
    }

    [Fact]
    public void Locate_SkipIntroDisabled_KeepsIntroPage()
    {
        var options = new ParseOptions { SkipIntroPages = false };
        var result = new AnchorLocator(options).Locate(SampleAgendaPages());

        Assert.Equal(87, result.ContentStartPage);
        Assert.Empty(result.IntroSkippedPages);
    }
}
