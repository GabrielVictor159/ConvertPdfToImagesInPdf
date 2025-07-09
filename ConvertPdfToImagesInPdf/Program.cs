using Docnet.Core.Models;
using Docnet.Core;
using iTextSharp.text.pdf;
using iTextSharp.text;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Formats.Png;
using System.IO;
using Image = SixLabors.ImageSharp.Image; 

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapPost("/convert-pdf-to-images-pdf", (PdfBase64Request request) =>
{
    if (string.IsNullOrEmpty(request.PdfBase64))
    {
        return Results.BadRequest(new { message = "O corpo da requisição deve conter o Base64 do PDF." });
    }

    try
    {
        string resultBase64Pdf = ConvertPdfToImagesPdf(request.PdfBase64);
        return Results.Ok(new { PdfBase64Result = resultBase64Pdf });
    }
    catch (Exception ex)
    {
        return Results.Problem("Ocorreu um erro inesperado: " + ex.Message, statusCode: StatusCodes.Status500InternalServerError);
    }
}).WithOpenApi();

app.Run();

static string ConvertPdfToImagesPdf(string pdfBase64)
{
    byte[] fileBytes = Convert.FromBase64String(pdfBase64);
    var images = new List<byte[]>();
    using (var docReader = DocLib.Instance.GetDocReader(fileBytes, new PageDimensions(1080, 1920)))
    {
        for (int pageIndex = 0; pageIndex < docReader.GetPageCount(); pageIndex++)
        {
            using (var pageReader = docReader.GetPageReader(pageIndex))
            {
                var rawBytes = pageReader.GetImage();
                var width = pageReader.GetPageWidth();
                var height = pageReader.GetPageHeight();
                var characters = pageReader.GetCharacters();

                using (var image = Image.LoadPixelData<Bgra32>(rawBytes, width, height))
                {
                    using (var stream = new MemoryStream())
                    {
                        image.Save(stream, new PngEncoder());
                        images.Add(stream.ToArray());
                    };
                };
            };
        }
    };
    var pdfBytes = ConvertImagesToPdf(images);
    return Convert.ToBase64String(pdfBytes);
}

static byte[] ConvertImagesToPdf(List<byte[]> images)
{
    using (MemoryStream pdfStream = new MemoryStream())
    {
        Document document = null;

        try
        {
            // Use ImageSharp to get dimensions of the first image
            using (var firstImageMs = new MemoryStream(images[0]))
            {
                using (var firstImage = Image.Load(firstImageMs))
                {
                    iTextSharp.text.Rectangle pageSize = new iTextSharp.text.Rectangle(0, 0, firstImage.Width, firstImage.Height);
                    document = new Document(pageSize, 0, 0, 0, 0);
                }
            }

            PdfWriter writer = PdfWriter.GetInstance(document, pdfStream);
            writer.SetFullCompression();
            document.Open();

            foreach (var imageBytes in images)
            {
                try
                {
                    iTextSharp.text.Image pdfImage = iTextSharp.text.Image.GetInstance(imageBytes);
                    if (pdfImage.Width > document.PageSize.Width || pdfImage.Height > document.PageSize.Height)
                    {
                        pdfImage.ScaleToFit(document.PageSize.Width, document.PageSize.Height);
                    }

                    pdfImage.Alignment = Element.ALIGN_CENTER | Element.ALIGN_MIDDLE;

                    document.Add(pdfImage);

                    if (images.IndexOf(imageBytes) < images.Count - 1)
                    {
                        document.NewPage();
                    }
                }
                catch (Exception imageEx)
                {
                    throw new Exception($"Erro ao processar uma imagem individual para o PDF: {imageEx.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Ocorreu um erro ao criar o documento PDF: {ex.Message}", ex);
        }
        finally
        {
            if (document != null && document.IsOpen())
            {
                document.Close();
            }
        }

        return pdfStream.ToArray();
    }
}

class PdfBase64Request
{
    public string PdfBase64 { get; set; } = string.Empty;
}