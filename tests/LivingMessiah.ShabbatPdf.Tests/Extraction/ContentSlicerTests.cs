using LivingMessiah.ShabbatPdf.Core.Extraction;
using LivingMessiah.ShabbatPdf.Core.Models;

namespace LivingMessiah.ShabbatPdf.Tests.Extraction;

public class ContentSlicerTests
{
    [Fact]
    public void Slice_ReturnsInclusiveRangeOrderedByPage()
    {
        var pages = new List<PdfPageText>
        {
            new(86, ["Welcome", "Bienvenido"]),
            new(87, ["Fair Use"]),
            new(88, ["Jude"]),
            new(113, ["Last"]),
            new(114, ["The Avinu Prayer"]),
        };

        var anchors = new AnchorResult(
            StartAnchorPage: 86,
            EndAnchorPage: 114,
            ProvisionalContentStartPage: 87,
            ContentStartPage: 88,
            ContentEndPage: 113,
            EndMatchMethod: AnchorLocator.EndMatchLine,
            IntroSkippedPages: [87]);

        var slice = ContentSlicer.Slice(pages, anchors);

        Assert.Equal(2, slice.Count);
        Assert.Equal(88, slice[0].PageNumber);
        Assert.Equal(113, slice[1].PageNumber);
    }
}
