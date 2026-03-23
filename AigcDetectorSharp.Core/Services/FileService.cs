using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using UglyToad.PdfPig;
using System.Text;

namespace AigcDetectorSharp.Core.Services;

public static class FileService
{
    public static string ReadFile(string path)
    {
        var ext = Path.GetExtension(path).ToLower();
        return ext switch
        {
            ".docx" => ReadDocx(path),
            ".pdf" => ReadPdf(path),
            _ => File.ReadAllText(path).Trim()
        };
    }

    public static bool IsSupportedFile(string path)
    {
        var ext = Path.GetExtension(path).ToLower();
        return ext is ".txt" or ".md" or ".docx" or ".pdf";
    }

    private static string ReadDocx(string path)
    {
        using var doc = WordprocessingDocument.Open(path, false);
        var body = doc.MainDocumentPart?.Document?.Body;
        if (body == null) return "";
        var sb = new StringBuilder();
        foreach (var para in body.Descendants<Paragraph>())
        {
            sb.AppendLine(para.InnerText);
        }
        return sb.ToString().Trim();
    }

    private static string ReadPdf(string path)
    {
        var sb = new StringBuilder();
        using (var document = PdfDocument.Open(path))
        {
            for (int i = 1; i <= document.NumberOfPages; i++)
            {
                var page = document.GetPage(i);
                sb.AppendLine(page.Text);
            }
        }
        return sb.ToString().Trim();
    }
}
