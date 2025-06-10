using iText.IO.Image;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Layout.Element;
using iText.Layout;
using Microsoft.AspNetCore.Mvc;
using iText.Commons.Datastructures;
using System.Drawing;
using System.Drawing.Imaging;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using iText.Kernel.Pdf.Canvas.Parser;
using System.Runtime.ConstrainedExecution;
using iText.Kernel.Pdf.Canvas.Parser.Data;
using WebApplication1.Models;
using WebApplication1.Services;
using FileResult = WebApplication1.Models.FileResult;

namespace WebApplication1.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PdfController : ControllerBase
    {
        private readonly IImageExtractor _imageExtractor;
        public PdfController(IImageExtractor imageExtractor)
        {
            _imageExtractor = imageExtractor;
        }

        [HttpPost("MakePDF")]
        public ActionResult<FileResult> MakePDF([FromBody] FileRequest fileRequest)
        {
            if (string.IsNullOrEmpty(fileRequest.Base64Content))
                return BadRequest("Пустое содержимое файла");

            try
            {
                using (var outputStream = new MemoryStream())
                {
                    using (var pdfWriter = new PdfWriter(outputStream))
                    {
                        using (var pdfDoc = new PdfDocument(pdfWriter))
                        {
                            var document = new iText.Layout.Document(pdfDoc);
                            var fileBytes = Convert.FromBase64String(fileRequest.Base64Content);
                            var extension = GetFileExtension(fileBytes);

                            if (IsImage(fileBytes))
                            {
                                using (var ms = new MemoryStream(fileBytes))
                                {
                                    var imageData = ImageDataFactory.Create(ms.ToArray());
                                    var image = new iText.Layout.Element.Image(imageData);
                                    var pageSize = PageSize.A4;

                                    // Масштабирование изображения
                                    image.ScaleToFit(pageSize.GetWidth() * 0.9f, pageSize.GetHeight() * 0.9f);

                                    // Создание страницы с размером изображения
                                    pdfDoc.AddNewPage(pageSize);
                                    var page = pdfDoc.GetLastPage();
                                    var canvas = new iText.Kernel.Pdf.Canvas.PdfCanvas(page);
                                    image.SetFixedPosition(0, 0);
                                    new Canvas(canvas, page.GetPageSize()).Add(image);
                                }
                            }
                            else if (IsTextFile(fileBytes))
                            {
                                // Здесь происходит извлечение текста с поддержкой кириллицы
                                var textContent = System.Text.Encoding.UTF8.GetString(fileBytes);
                                var pageSize = PageSize.A4;
                                pdfDoc.AddNewPage(pageSize);
                                var page = pdfDoc.GetLastPage();
                                var canvas = new iText.Kernel.Pdf.Canvas.PdfCanvas(page);
                                new Canvas(canvas, pageSize).Add(new Paragraph(textContent));
                            }
                            else
                            {
                                // Неизвестный тип файла
                                pdfDoc.AddNewPage(PageSize.A4);
                                var page = pdfDoc.GetLastPage();
                                var canvas = new iText.Kernel.Pdf.Canvas.PdfCanvas(page);
                                new Canvas(canvas, PageSize.A4).Add(new Paragraph($"Файл типа {extension} не поддерживается для отображения."));
                            }

                            // Закрытие документа
                            pdfDoc.Close();

                            // Возврат результата
                            return new FileResult
                            {
                                FileName = fileRequest.FileName,
                                Base64Content = Convert.ToBase64String(outputStream.ToArray())
                            };
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Логирование ошибки (при необходимости)
                // logger.LogError(ex, "Ошибка при создании PDF");
                return StatusCode(500, new { message = "Внутренняя ошибка сервера", detail = ex.Message });
            }
        }


        [HttpPost("DisassemblePDF")]
        public ActionResult<FileResult> DisassemblePDF([FromBody] FileResult request)
        {
            try
            {
                var pdfBytes = Convert.FromBase64String(request.Base64Content);

                using (var inputStream = new MemoryStream(pdfBytes))
                {
                    var pdfReader = new PdfReader(inputStream);
                    var pdfDoc = new PdfDocument(pdfReader);

                    // Получаем базовое имя файла без расширения .pdf
                    var baseFileName = System.IO.Path.GetFileNameWithoutExtension(request.FileName);

                    // Перебираем страницы
                    for (int i = 1; i <= pdfDoc.GetNumberOfPages(); i++)
                    {
                        var page = pdfDoc.GetPage(i);

                        // Попытка извлечь текст
                        var text = PdfTextExtractor.GetTextFromPage(page);
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            // Возвращаем текст с оригинальным именем и расширением .txt
                            return new FileResult
                            {
                                FileName = $"{baseFileName}.txt",
                                Base64Content = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(text))
                            };
                        }

                        var imageBytes = _imageExtractor.ExtractImagesFromPage(page);

                        if (imageBytes != null && imageBytes.Length > 0)
                        {
                            // Возвращаем изображение с оригинальным именем и расширением .png
                            return new FileResult
                            {
                                FileName = $"{baseFileName}.png",
                                Base64Content = Convert.ToBase64String(imageBytes)
                            };
                        }
                    }
                }

                // Если ничего не найдено
                return BadRequest("На страницах PDF не обнаружено текста или изображений");
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Ошибка при разборе PDF", detail = ex.Message });
            }
        }



        // Вспомогательные методы
        private string GetFileExtension(byte[] fileBytes)
        {
            if (IsImage(fileBytes))
                return "image";
            if (IsTextFile(fileBytes))
                return "txt";
            return "unknown";
        }

        private bool IsImage(byte[] fileBytes)
        {
            if (fileBytes.Length < 4) return false;
            if (fileBytes.Take(2).SequenceEqual(new byte[] { 0xFF, 0xD8 })) return true; // JPEG
            if (fileBytes.Take(8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return true; // PNG
            return false;
        }

        private bool IsTextFile(byte[] fileBytes)
        {
            try
            {
                var text = System.Text.Encoding.UTF8.GetString(fileBytes);
                return text.All(ch =>
                    char.IsControl(ch) ||
                    (ch >= ' ' && ch <= '~') ||  // ASCII printable
                    (ch >= '\u0400' && ch <= '\u04FF') // Кириллица
                );
            }
            catch
            {
                return false;
            }
        }
    }
}


