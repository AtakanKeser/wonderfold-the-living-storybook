using System.Text;

namespace Wonderfold.Core.Live
{
    /// <summary>
    /// The short block of text a player posts after finishing a shared page, and the long code that lets
    /// anyone replay it.
    ///
    /// <para>The two halves do different jobs. The card is the brag: it fits in a message, it spoils
    /// nothing about the solution, and it says enough that a friend can tell whether they beat it. The
    /// thread is the proof: paste it back into the game and the run plays out move for move on the same
    /// board, with the score recomputed rather than believed.</para>
    /// </summary>
    public static class ShareCard
    {
        public static string Compose(string pageTitle, PageRunReport report, int storyInk, int streak,
            string threadCode)
        {
            var text = new StringBuilder();
            text.Append("Wonderfold — ").Append(pageTitle ?? "a lost page").Append('\n');

            if (!report.Won)
            {
                text.Append("The page kept its secret today.");
                return text.ToString();
            }

            text.Append(Stars(report.Stars)).Append("  ")
                .Append(storyInk.ToString("N0", System.Globalization.CultureInfo.InvariantCulture))
                .Append(" story ink");

            if (report.Stats != null)
            {
                text.Append("  ·  ").Append(report.Stats.MovesRemaining).Append(" moves spare");
                if (report.Stats.GoldenStitches > 0)
                    text.Append('\n').Append("Golden stitches: ").Append(report.Stats.GoldenStitches)
                        .Append("  ·  seam matches: ").Append(report.Stats.SeamMatches);
            }

            if (streak > 1) text.Append('\n').Append(streak).Append("-day streak");
            if (!string.IsNullOrEmpty(threadCode)) text.Append('\n').Append("Thread: ").Append(threadCode);

            return text.ToString();
        }

        public static string Stars(int stars)
        {
            switch (stars)
            {
                case 3: return "★★★";
                case 2: return "★★☆";
                case 1: return "★☆☆";
                default: return "☆☆☆";
            }
        }
    }
}
