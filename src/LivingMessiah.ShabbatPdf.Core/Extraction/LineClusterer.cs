using LivingMessiah.ShabbatPdf.Core.Models;
using LivingMessiah.ShabbatPdf.Core.Options;

namespace LivingMessiah.ShabbatPdf.Core.Extraction;

/// <summary>
/// Full-page word → line clustering (midY greedy). No multi-column logic.
/// PDF Y increases upward; visual top lines have larger MidY.
/// </summary>
public static class LineClusterer
{
    public static IReadOnlyList<string> ClusterLines(
        IReadOnlyList<PdfWordBox> words,
        LineClusterOptions? options = null)
    {
        options ??= new LineClusterOptions();
        var tolerance = options.YTolerance;

        if (words.Count == 0)
        {
            return Array.Empty<string>();
        }

        var ordered = words
            .Where(w => !string.IsNullOrWhiteSpace(w.Text))
            .OrderByDescending(w => w.MidY)
            .ThenBy(w => w.Left)
            .ToList();

        if (ordered.Count == 0)
        {
            return Array.Empty<string>();
        }

        var clusters = new List<WordCluster>();

        foreach (var word in ordered)
        {
            var match = clusters.FirstOrDefault(c =>
                Math.Abs(word.MidY - c.MeanMidY) <= tolerance);

            if (match is null)
            {
                clusters.Add(new WordCluster(word));
            }
            else
            {
                match.Add(word);
            }
        }

        return clusters
            .OrderByDescending(c => c.MeanMidY)
            .Select(c => c.ToLine())
            .Where(line => line.Length > 0)
            .ToList();
    }

    private sealed class WordCluster
    {
        private readonly List<PdfWordBox> _words = [];
        private double _sumMidY;

        public WordCluster(PdfWordBox first)
        {
            Add(first);
        }

        public double MeanMidY => _sumMidY / _words.Count;

        public void Add(PdfWordBox word)
        {
            _words.Add(word);
            _sumMidY += word.MidY;
        }

        public string ToLine()
        {
            var parts = _words
                .OrderBy(w => w.Left)
                .Select(w => w.Text.Trim())
                .Where(t => t.Length > 0);

            return string.Join(' ', parts).Trim();
        }
    }
}
