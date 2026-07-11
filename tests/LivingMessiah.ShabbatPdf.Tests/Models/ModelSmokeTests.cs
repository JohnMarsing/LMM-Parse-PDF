using LivingMessiah.ShabbatPdf.Core.Models;
using LivingMessiah.ShabbatPdf.Core.Options;

namespace LivingMessiah.ShabbatPdf.Tests.Models;

public class ModelSmokeTests
{
    [Fact]
    public void PdfWordBox_ComputesMidpoints()
    {
        var word = new PdfWordBox("Welcome", Left: 10, Right: 50, Bottom: 100, Top: 120);

        Assert.Equal(110.0, word.MidY);
        Assert.Equal(30.0, word.MidX);
    }

    [Fact]
    public void PdfPageText_CollapsedText_JoinsAndCollapsesWhitespace()
    {
        var page = new PdfPageText(
            PageNumber: 86,
            Lines: ["Welcome", "Bienvenido", "  "]);

        Assert.Equal(86, page.PageNumber);
        Assert.Equal("Welcome Bienvenido", page.CollapsedText);
    }

    [Fact]
    public void AnchorResult_HoldsExpectedRangeFields()
    {
        var anchors = new AnchorResult(
            StartAnchorPage: 86,
            EndAnchorPage: 114,
            ProvisionalContentStartPage: 87,
            ContentStartPage: 88,
            ContentEndPage: 113,
            EndMatchMethod: "Line",
            IntroSkippedPages: [87]);

        Assert.Equal(88, anchors.ContentStartPage);
        Assert.Equal(113, anchors.ContentEndPage);
        Assert.Single(anchors.IntroSkippedPages);
        Assert.Equal(87, anchors.IntroSkippedPages[0]);
    }

    [Fact]
    public void ParseRequest_DefaultsMatchDesign()
    {
        var request = new ParseRequest("2026-07-04-Lev-16.pdf");

        Assert.True(request.Overwrite);
        Assert.False(request.SkipIfDestinationExists);
        Assert.False(request.DryRun);
        Assert.True(request.RequireStandardBlobName);
        Assert.Null(request.PdfStream);
        Assert.Null(request.LocalInputPath);
    }

    [Fact]
    public void ParseResult_CanRepresentSuccessAndFailure()
    {
        var ok = new ParseResult(true, "OK", Markdown: "# hi");
        var fail = new ParseResult(false, "AnchorNotFound:Start");

        Assert.True(ok.Success);
        Assert.Equal("# hi", ok.Markdown);
        Assert.False(fail.Success);
        Assert.Null(fail.Markdown);
    }

    [Fact]
    public void ParseOptions_DefaultsMatchDesign()
    {
        var options = new ParseOptions();

        Assert.Equal("Welcome", options.StartWelcomeLine);
        Assert.Contains("Bienvenido", options.StartBienvenidoLines);
        Assert.Contains("Bienvenidos", options.StartBienvenidoLines);
        Assert.Equal("The Avinu Prayer", options.EndAvinuPhrase);
        Assert.True(options.SkipIntroPages);
        Assert.Contains("fair use", options.IntroSkipLineContains);
        Assert.Equal(3.0, options.YTolerance);
        Assert.True(options.Overwrite);
        Assert.True(options.RequireStandardBlobName);

        var cluster = options.ToLineClusterOptions();
        Assert.Equal(3.0, cluster.YTolerance);
    }

    [Fact]
    public void BlobOptions_DefaultsMatchDesign()
    {
        var options = new BlobOptions();

        Assert.Equal("shabbat-service", options.SourceContainer);
        Assert.Equal("shabbat-service-md", options.DestinationContainer);
        Assert.False(options.UseDefaultAzureCredential);
        Assert.Equal(string.Empty, options.ConnectionString);
    }
}
