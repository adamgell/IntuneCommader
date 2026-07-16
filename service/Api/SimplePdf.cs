using System.Globalization;
using System.Text;

namespace CmProjectX.Api;

// A dependency-free minimal PDF writer: a single-column, multi-page text document in the
// Base-14 Helvetica font (no font embedding, no external engine, no trial watermark on an
// audit artifact). Enough for the M19 posture score-summary evidence PDF. Deterministic —
// the bytes are a pure function of the input lines, so the evidence pack stays reproducible.
internal static class SimplePdf
{
    private const int PageWidth = 612;   // US Letter, points
    private const int PageHeight = 792;
    private const int Margin = 54;
    private const double FontSize = 10;
    private const double Leading = 14;
    private const int WrapChars = 95;    // ~504pt usable width / ~5.3pt avg Helvetica glyph
    private static readonly int LinesPerPage = (int)((PageHeight - 2.0 * Margin) / Leading); // ~48

    public static byte[] FromLines(IReadOnlyList<string> rawLines)
    {
        // Word-wrap, then paginate.
        var lines = new List<string>();
        foreach (var raw in rawLines) lines.AddRange(Wrap(raw));
        if (lines.Count == 0) lines.Add("");

        var pages = new List<List<string>>();
        for (var i = 0; i < lines.Count; i += LinesPerPage)
            pages.Add(lines.GetRange(i, Math.Min(LinesPerPage, lines.Count - i)));

        // Object layout: 1=Catalog, 2=Pages, 3=Font, then per page: pageObj (4+2i), contentObj (5+2i).
        var buf = new MemoryStream();
        void W(string s) { var b = Encoding.ASCII.GetBytes(s); buf.Write(b, 0, b.Length); }
        var offsets = new Dictionary<int, long>();
        void Obj(int n, string body) { offsets[n] = buf.Position; W($"{n} 0 obj\n{body}\nendobj\n"); }

        W("%PDF-1.4\n");

        var pageObjNums = Enumerable.Range(0, pages.Count).Select(i => 4 + 2 * i).ToList();
        Obj(1, "<< /Type /Catalog /Pages 2 0 R >>");
        Obj(2, $"<< /Type /Pages /Kids [{string.Join(" ", pageObjNums.Select(n => $"{n} 0 R"))}] /Count {pages.Count} >>");
        Obj(3, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");

        for (var i = 0; i < pages.Count; i++)
        {
            var pageObj = 4 + 2 * i;
            var contentObj = 5 + 2 * i;
            Obj(pageObj,
                $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {PageWidth} {PageHeight}] " +
                $"/Resources << /Font << /F1 3 0 R >> >> /Contents {contentObj} 0 R >>");

            var content = Encoding.ASCII.GetBytes(BuildContent(pages[i]));
            offsets[contentObj] = buf.Position;
            W($"{contentObj} 0 obj\n<< /Length {content.Length} >>\nstream\n");
            buf.Write(content, 0, content.Length);
            W("\nendstream\nendobj\n");
        }

        // xref — 20-byte entries, offsets in bytes from file start.
        var xrefPos = buf.Position;
        var maxObj = 3 + 2 * pages.Count;
        var size = maxObj + 1;
        W($"xref\n0 {size}\n");
        W("0000000000 65535 f \n");
        for (var n = 1; n <= maxObj; n++)
        {
            var off = offsets.TryGetValue(n, out var o) ? o : 0;
            W($"{off.ToString("D10", CultureInfo.InvariantCulture)} 00000 n \n");
        }
        W($"trailer\n<< /Size {size} /Root 1 0 R >>\nstartxref\n{xrefPos}\n%%EOF\n");
        return buf.ToArray();
    }

    private static string BuildContent(IReadOnlyList<string> lines)
    {
        var sb = new StringBuilder();
        sb.Append("BT\n");
        sb.Append($"/F1 {Fmt(FontSize)} Tf\n{Fmt(Leading)} TL\n{Margin} {PageHeight - Margin} Td\n");
        foreach (var line in lines)
            sb.Append($"({Escape(line)}) Tj\nT*\n");
        sb.Append("ET\n");
        return sb.ToString();
    }

    private static IEnumerable<string> Wrap(string line)
    {
        if (line.Length <= WrapChars) { yield return line; yield break; }
        var words = line.Split(' ');
        var cur = new StringBuilder();
        foreach (var w in words)
        {
            if (cur.Length > 0 && cur.Length + 1 + w.Length > WrapChars) { yield return cur.ToString(); cur.Clear(); }
            if (cur.Length > 0) cur.Append(' ');
            // A single over-long token (e.g. a GUID list) is hard-split so it never runs off-page.
            if (w.Length > WrapChars)
            {
                if (cur.Length > 0) { yield return cur.ToString(); cur.Clear(); }
                for (var i = 0; i < w.Length; i += WrapChars) yield return w.Substring(i, Math.Min(WrapChars, w.Length - i));
            }
            else cur.Append(w);
        }
        if (cur.Length > 0) yield return cur.ToString();
    }

    private static string Fmt(double d) => d.ToString("0.###", CultureInfo.InvariantCulture);

    // PDF literal-string escaping; drop control/non-ASCII so the WinAnsi text stays clean.
    private static string Escape(string s)
    {
        var sb = new StringBuilder(s.Length + 8);
        foreach (var ch in s)
        {
            switch (ch)
            {
                case '\\': sb.Append("\\\\"); break;
                case '(': sb.Append("\\("); break;
                case ')': sb.Append("\\)"); break;
                case '\r': case '\n': break;
                default: sb.Append(ch is >= ' ' and <= '~' ? ch : ' '); break;
            }
        }
        return sb.ToString();
    }
}
