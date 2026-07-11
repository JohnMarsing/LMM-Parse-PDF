using LivingMessiah.ShabbatPdf.Core.Extraction;

namespace LivingMessiah.ShabbatPdf.Tests.Extraction;

public class FilenameParserTests
{
    [Theory]
    [InlineData("2026-07-04-Lev-16.pdf", "2026-07-04", "Lev-16")]
    [InlineData("2026-06-06-Lev-12-1-to-13-28.pdf", "2026-06-06", "Lev-12-1-to-13-28")]
    [InlineData(@"C:\Downloads\2026-07-04-Lev-16.pdf", "2026-07-04", "Lev-16")]
    public void Parse_StandardNames_ExtractsDateAndCitation(
        string input,
        string expectedDate,
        string expectedCitation)
    {
        var result = FilenameParser.Parse(input);

        Assert.True(result.IsStandardPattern);
        Assert.Equal(expectedDate, result.ServiceDate);
        Assert.Equal(expectedCitation, result.Citation);
        Assert.Equal($"{expectedDate}-{expectedCitation}.md", result.MarkdownFileName);
        Assert.True(FilenameParser.IsStandardBlobName(input));
    }

    [Fact]
    public void Parse_NonStandard_UsesUnknownCitation()
    {
        var result = FilenameParser.Parse("notes.pdf");

        Assert.False(result.IsStandardPattern);
        Assert.Null(result.ServiceDate);
        Assert.Equal("unknown", result.Citation);
        Assert.Equal("notes.md", result.MarkdownFileName);
        Assert.False(FilenameParser.IsStandardBlobName("notes.pdf"));
    }

    [Fact]
    public void Parse_IsCaseInsensitiveOnExtension()
    {
        var result = FilenameParser.Parse("2026-07-04-Lev-16.PDF");
        Assert.True(result.IsStandardPattern);
        Assert.Equal("Lev-16", result.Citation);
    }
}
