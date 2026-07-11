using LivingMessiah.ShabbatPdf.Core.Extraction;
using LivingMessiah.ShabbatPdf.Core.Models;
using LivingMessiah.ShabbatPdf.Core.Options;

namespace LivingMessiah.ShabbatPdf.Tests.Extraction;

public class LineClustererTests
{
    [Fact]
    public void ClusterLines_DoesNotMergeDistantWelcomeAndBienvenido()
    {
        // Far apart in Y (PDF Y up): Welcome near top, Bienvenido lower
        var words = new List<PdfWordBox>
        {
            new("Welcome", Left: 100, Right: 200, Bottom: 900, Top: 920),
            new("Bienvenido", Left: 100, Right: 220, Bottom: 400, Top: 420),
        };

        var lines = LineClusterer.ClusterLines(words, new LineClusterOptions { YTolerance = 3.0 });

        Assert.Equal(2, lines.Count);
        Assert.Equal("Welcome", lines[0]);
        Assert.Equal("Bienvenido", lines[1]);
    }

    [Fact]
    public void ClusterLines_MergesAvinuTitleWordsWithinTolerance()
    {
        // midY ≈ 951–952 as on sample title
        var words = new List<PdfWordBox>
        {
            new("The", Left: 400, Right: 450, Bottom: 945, Top: 957),   // midY 951
            new("Avinu", Left: 460, Right: 540, Bottom: 946, Top: 958), // midY 952
            new("Prayer", Left: 550, Right: 640, Bottom: 945.5, Top: 957.5), // midY 951.5
        };

        var lines = LineClusterer.ClusterLines(words, new LineClusterOptions { YTolerance = 3.0 });

        Assert.Single(lines);
        Assert.Equal("The Avinu Prayer", lines[0]);
    }

    [Fact]
    public void ClusterLines_OrdersWordsLeftToRightOnSameLine()
    {
        var words = new List<PdfWordBox>
        {
            new("world", Left: 200, Right: 260, Bottom: 100, Top: 110),
            new("Hello", Left: 50, Right: 120, Bottom: 100, Top: 110),
        };

        var lines = LineClusterer.ClusterLines(words);

        Assert.Single(lines);
        Assert.Equal("Hello world", lines[0]);
    }

    [Fact]
    public void ClusterLines_EmptyInput_ReturnsEmpty()
    {
        Assert.Empty(LineClusterer.ClusterLines([]));
    }
}
