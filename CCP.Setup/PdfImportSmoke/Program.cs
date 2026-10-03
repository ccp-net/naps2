using System.Text;
using NAPS2.Images.Gdi;
using NAPS2.Pdf;
using NAPS2.Scan;

const int ExpectedPageCount = 3;

if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: PdfImportSmoke <installed-ccp-directory>");
    return 2;
}

var installDir = Path.GetFullPath(args[0]);
if (!Directory.Exists(installDir))
{
    Console.Error.WriteLine($"Install directory not found: {installDir}");
    return 3;
}

Environment.SetEnvironmentVariable("NAPS2_TEST_DEPS", installDir);
var pdfPath = Path.Combine(Path.GetTempPath(), $"ccp-pdf-import-smoke-{Guid.NewGuid():N}.pdf");

try
{
    WriteThreePagePdf(pdfPath);

    using var context = new ScanningContext(new GdiImageContext());
    var importer = new PdfImporter(context);
    var count = 0;

    await foreach (var image in importer.Import(pdfPath))
    {
        using (image)
        {
            count++;
        }
    }

    if (count != ExpectedPageCount)
    {
        Console.Error.WriteLine($"PDF import returned {count} pages; expected {ExpectedPageCount}.");
        return 4;
    }

    Console.WriteLine($"PASS: PdfImporter loaded installed Pdfium and imported {count} pages.");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine("FAIL: Installed PDF import smoke test failed.");
    Console.Error.WriteLine(ex);
    return 1;
}
finally
{
    try { File.Delete(pdfPath); } catch { }
}

static void WriteThreePagePdf(string path)
{
    // Deliberately small, dependency-free PDF used by CI to exercise the same PdfImporter/Pdfium path as the GUI.
    // Objects 3-5 are pages and objects 6-8 are their empty content streams.
    var objects = new[]
    {
        "<< /Type /Catalog /Pages 2 0 R >>",
        "<< /Type /Pages /Kids [3 0 R 4 0 R 5 0 R] /Count 3 >>",
        "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << >> /Contents 6 0 R >>",
        "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << >> /Contents 7 0 R >>",
        "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << >> /Contents 8 0 R >>",
        "<< /Length 0 >>\nstream\n\nendstream",
        "<< /Length 0 >>\nstream\n\nendstream",
        "<< /Length 0 >>\nstream\n\nendstream"
    };

    using var stream = new MemoryStream();
    void Write(string text)
    {
        var bytes = Encoding.ASCII.GetBytes(text);
        stream.Write(bytes, 0, bytes.Length);
    }

    Write("%PDF-1.4\n%CCP\n");
    var offsets = new List<long> { 0 };
    for (var i = 0; i < objects.Length; i++)
    {
        offsets.Add(stream.Position);
        Write($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
    }

    var xref = stream.Position;
    Write($"xref\n0 {objects.Length + 1}\n");
    Write("0000000000 65535 f \n");
    for (var i = 1; i < offsets.Count; i++)
    {
        Write($"{offsets[i]:0000000000} 00000 n \n");
    }
    Write($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");

    File.WriteAllBytes(path, stream.ToArray());
}
