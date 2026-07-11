using LivingMessiah.ShabbatPdf.Core.Extraction;
using LivingMessiah.ShabbatPdf.Core.Models;

namespace LivingMessiah.ShabbatPdf.Tests.Extraction;

public class MarkdownBuilderTests
{
    private static readonly DateTimeOffset FixedUtc =
        new(2026, 7, 10, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Build_StandardFile_EmitsFrontMatterH1AndPages()
    {
        var pages = new List<PdfPageText>
        {
            new(88, ["Jude 6 And angels who did not keep their domain"]),
            new(95, ["TOTAL SURRENDER", "Body line after heading"]),
        };

        var md = new MarkdownBuilder().Build(pages, "2026-07-04-Lev-16.pdf", FixedUtc);

        Assert.Contains("source_pdf: 2026-07-04-Lev-16.pdf", md);
        Assert.Contains("service_date: 2026-07-04", md);
        Assert.Contains("citation: Lev-16", md);
        Assert.Contains("extracted_pages: 88-95", md);
        Assert.Contains("generated_utc: 2026-07-10T18:00:00Z", md);
        Assert.Contains("tool: LMM-Parse-PDF", md);
        Assert.Contains("# 2026-07-04 — Lev-16", md);
        Assert.Contains("<!-- page 88 -->", md);
        Assert.Contains("Jude 6 And angels who did not keep their domain", md);
        Assert.Contains("<!-- page 95 -->", md);
        Assert.Contains("## TOTAL SURRENDER", md);
        Assert.Contains("Body line after heading", md);

        // No intro / Fair Use content in this slice
        Assert.DoesNotContain("fair use", md, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_NonStandardFile_UsesUnknownCitationAndBaseNameH1()
    {
        var pages = new List<PdfPageText> { new(1, ["Hello"]) };
        var md = new MarkdownBuilder().Build(pages, "scratch-notes.pdf", FixedUtc);

        Assert.Contains("citation: unknown", md);
        Assert.Contains("service_date: ", md);
        Assert.Contains("# scratch-notes", md);
        Assert.Contains("<!-- page 1 -->", md);
        Assert.Contains("Hello", md);
    }

    [Fact]
    public void IsOptionalHeading_AllCapsWithinLimit_IsHeading()
    {
        Assert.True(MarkdownBuilder.IsOptionalHeading("TOTAL SURRENDER"));
        Assert.True(MarkdownBuilder.IsOptionalHeading("Sin Requires Payment")); // Title Case
    }

    [Fact]
    public void IsOptionalHeading_RejectsQuestionsLongLinesAndProse()
    {
        Assert.False(MarkdownBuilder.IsOptionalHeading("What will we talk about today?"));
        Assert.False(MarkdownBuilder.IsOptionalHeading("This is a longer line that exceeds sixty characters easily for sure."));
        Assert.False(MarkdownBuilder.IsOptionalHeading("Jude 6 And angels who did not keep."));
        Assert.False(MarkdownBuilder.IsOptionalHeading("lowercase title"));
    }

    [Fact]
    public void Build_SkipsBlankLinesWithinPage()
    {
        var pages = new List<PdfPageText>
        {
            // lowercase so optional heading rule does not rewrite lines
            new(10, ["first line of body", "  ", "second line of body"]),
        };

        var md = new MarkdownBuilder().Build(pages, "2026-01-01-Test.pdf", FixedUtc)
            .Replace("\r\n", "\n");

        Assert.Contains("first line of body\nsecond line of body", md);
        Assert.DoesNotContain("first line of body\n\nsecond line of body", md);
    }

    [Fact]
    public void Build_EmptyPages_StillHasFrontMatter()
    {
        var md = new MarkdownBuilder().Build([], "2026-07-04-Lev-16.pdf", FixedUtc);

        Assert.Contains("extracted_pages: ", md);
        Assert.Contains("# 2026-07-04 — Lev-16", md);
        Assert.DoesNotContain("<!-- page", md);
    }
}
