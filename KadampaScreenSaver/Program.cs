// File: KadampaScreenSaver/Program.cs
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Net;

class Program
{
    static readonly HttpClient client = new HttpClient(new HttpClientHandler() { AutomaticDecompression = DecompressionMethods.GZip });
    static async Task Main()
    {
        string webpageUrl = "https://kadampa.org/news";
        string baseDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Geshe-la");
        Directory.CreateDirectory(baseDirectory);
        string htmlFilePath = Path.Combine(baseDirectory, "index.html");

        try
        {
            // Download the webpage
            await DownloadFile(webpageUrl, htmlFilePath);

            // Check if the HTML file exists
            if (!File.Exists(htmlFilePath))
            {
                Console.WriteLine($"Error: HTML file not found at {htmlFilePath}");
                return;
            }

            // Extract page URLs from HTML
            string htmlContent = await File.ReadAllTextAsync(htmlFilePath);
            var pageUrls = Regex.Matches(htmlContent, "<a.*?href=[\"'](.*?)[\"']")
                .Cast<Match>()
                .Select(match => match.Groups[1].Value)
                .ToList();

            // Check if any page URLs were found
            if (pageUrls.Count == 0)
            {
                Console.WriteLine("Error: No page URLs found in the HTML");
                return;
            }

            // Get the current year
            int currentYear = DateTime.Now.Year;

            // Download images from each page
            foreach (string pageUrl in pageUrls)
            {
                // Skip pages that do not start with the current year
                if (!pageUrl.Contains($"/{currentYear}/"))
                {
                    continue;
                }

                // Download the page
                string pageHtmlFilePath = Path.Combine(baseDirectory, "page.html");

                await DownloadFile(pageUrl, pageHtmlFilePath);

                // Extract image URLs from the page
                string pageHtmlContent = await File.ReadAllTextAsync(pageHtmlFilePath);
                var imageUrls = Regex.Matches(pageHtmlContent, "<img.*?src=[\"'](.*?)[\"']").Cast<Match>().Select(match => match.Groups[1].Value).ToList();

                // Download images
                foreach (string imageUrl in imageUrls)
                {
                    // Skip images with "150x" in the URL
                    if (imageUrl.Contains("150x") || imageUrl.Contains("paperback") || imageUrl.Contains("Book") || imageUrl.Contains("book") || imageUrl.Contains("Gen-"))
                    {
                        continue;
                    }

                    try
                    {
                        string fileName = Path.GetFileName(imageUrl);
                        string savePath = Path.Combine(baseDirectory, fileName);

                        // Download the image
                        await DownloadFile(imageUrl, savePath);

                        // Check image dimensions
                        byte[] imageBytes = await File.ReadAllBytesAsync(savePath);
                        using (var memoryStream = new MemoryStream(imageBytes))
                        {
                            using (var image = Image.FromStream(memoryStream))
                            {
                                if (image.Width < 1024)
                                {
                                    // Delete image if width is less than 1024
                                    File.Delete(savePath);
                                }
                                else
                                {
                                    Console.WriteLine($"Downloaded image: {fileName}");
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error downloading image: {imageUrl}. Error: {ex.Message}");
                    }
                }
            }

            // Get the current date
            DateTime currentDate = DateTime.Now;

            // Get all files in the directory
            var files = Directory.GetFiles(baseDirectory);

            // Filter out files that are older than 2 weeks
            foreach (string file in files)
            {
                FileInfo fileInfo = new FileInfo(file);
                if ((currentDate - fileInfo.LastWriteTime).TotalDays > 14)
                {
                    // Delete old files
                    try
                    {
                        File.Delete(file);
                        Console.WriteLine($"Deleted old file: {file}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error deleting file: {file}. Error: {ex.Message}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"An error occurred: {ex.Message}");
        }
    }

    static async Task DownloadFile(string url, string outputPath)
    {
        try
        {
            byte[] data;
            using (var client = new HttpClient())
            {
                data = await client.GetByteArrayAsync(new Uri(url));
            }

            await File.WriteAllBytesAsync(outputPath, data);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error downloading file: {url}. Error: {ex.Message}");
        }
    }
}
