using LivingMessiah.ShabbatPdf.Functions;

namespace LivingMessiah.ShabbatPdf.Tests.Functions;

public class ShabbatBlobTriggerFilterTests
{
    [Theory]
    [InlineData("2026-07-04-Lev-16.pdf", true)]
    [InlineData("folder/2026-07-04-Lev-16.pdf", true)]
    [InlineData("2026-07-04-Lev-16.PDF", true)]
    [InlineData("2026-07-04-Lev-16-teaching.pdf", false)]
    [InlineData("2026-07-04-Lev-16-teaching.PDF", false)]
    [InlineData("notes.txt", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void ShouldProcess_FiltersTeachingAndNonPdf(string? name, bool expected)
    {
        Assert.Equal(expected, ShabbatBlobTriggerFilter.ShouldProcess(name));
    }
}
