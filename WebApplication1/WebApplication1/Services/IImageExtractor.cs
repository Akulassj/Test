using iText.Kernel.Pdf;

namespace WebApplication1.Services
{
    public interface IImageExtractor
    {
        byte[] ExtractImagesFromPage(PdfPage page);
    }
}
