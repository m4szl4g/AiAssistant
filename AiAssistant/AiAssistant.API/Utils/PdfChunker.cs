using System.Diagnostics;
using System.Text;

namespace AiAssistant.API.Utils
{
    public class PdfChunker
    {
        private const int ChunkSize = 400;
        private const int Overlap = 50;

        public List<string> Chunk(Stream pdfStream)
        {
            var tmpPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.pdf");
            try
            {
                using (var fs = File.Create(tmpPath))
                    pdfStream.CopyTo(fs);

                var text = ExtractTextWithPdfToText(tmpPath);
                text = CleanText(text);
                return SplitIntoChunks(text);
            }
            finally
            {
                if (File.Exists(tmpPath))
                    File.Delete(tmpPath);
            }
        }

        private static string ExtractTextWithPdfToText(string pdfPath)
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "pdftotext",
                    Arguments = $"-enc UTF-8 -layout \"{pdfPath}\" -",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    StandardOutputEncoding = Encoding.UTF8
                }
            };

            process.Start();
            var text = process.StandardOutput.ReadToEnd();
            process.WaitForExit(30_000);

            return text;
        }

        private static string CleanText(string text)
        {
            // ■ (U+25A0) is substituted by pdftotext when a glyph has no ToUnicode mapping.
            // Removing it improves embedding quality for Hungarian PDFs with broken font tables.
            return text.Replace("\u25a0", "").Replace("\f", " ");
        }

        private static List<string> SplitIntoChunks(string text)
        {
            var chunks = new List<string>();
            if (string.IsNullOrWhiteSpace(text))
                return chunks;

            int start = 0;
            while (start < text.Length)
            {
                int end = Math.Min(start + ChunkSize, text.Length);

                if (end < text.Length)
                {
                    int lastSpace = text.LastIndexOf(' ', end, Math.Min(end - start, 100));
                    if (lastSpace > start)
                        end = lastSpace;
                }

                var chunk = text[start..end].Trim();
                if (chunk.Length > 0)
                    chunks.Add(chunk);

                int next = end - Overlap;
                if (next <= start)
                    next = end;

                if (next >= text.Length)
                    break;

                start = next;
            }

            return chunks;
        }
    }
}
