// File: KadampaScreenSaver/Program.cs
using System;
using System.Diagnostics;
using System.Drawing;
using System.Text;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

HttpClient client = new HttpClient();
ILogger<Program> logger = null;
IConfigurationRoot configuration = new ConfigurationBuilder()
.AddJsonFile("appsettings.json", true, true)
.Build();

int linkDepth = configuration.GetValue<int>("Policies:LinkDepth");
int retentionDays = configuration.GetValue<int>("Policies:RetentionDays");
string baseDirectory = configuration.GetValue<string>("Directories:Base");
string subDirectory = configuration.GetValue<string>("Directories:SubDirectory");
if (configuration.GetValue<bool>("Directories:UseMyPictures"))
{
    baseDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), subDirectory);
}
else
{
    baseDirectory = Path.Combine(baseDirectory, subDirectory);
}

using ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
    builder.AddSimpleConsole(options =>
    {
        options.IncludeScopes = true;
        options.SingleLine = true;
        options.TimestampFormat = "HH:mm:ss ";
    }));

logger = loggerFactory.CreateLogger<Program>();

string webpageUrl = "https://kadampa.org/news";
Directory.CreateDirectory(baseDirectory);

// Download the webpage
logger.LogInformation("Starting download of webpage");

// Extract page URLs from HTML
string htmlContent = await DownloadHtmlContentAsync(webpageUrl);
var pageUrls = Regex.Matches(htmlContent, "<a.*?href=[\"'](.*?)[\"']")
    .Cast<Match>()
    .Select(match => match.Groups[1].Value)
    .ToList();

// Check if any page URLs were found
if (pageUrls.Count == 0)
{
    logger.LogError("No page URLs found in the HTML");
    return;
}

// Get the current year
int currentYear = DateTime.Now.Year;
logger.LogInformation($"Current year: {currentYear}");

int pageCount = 0;
// Download images from each page
foreach (string pageUrl in pageUrls)
{
    // Skip pages that do not start with the current year
    if (!pageUrl.Contains($"/{currentYear}/"))
    {
        continue;
    }
    if (pageCount == linkDepth)
    {
        break;
    }

    var web = new HtmlWeb();
    var doc = web.Load(pageUrl);


    // Now, using LINQ to get all Images
    List<HtmlNode> imageNodes = null;
    imageNodes = (from HtmlNode node in doc.DocumentNode.SelectNodes("//*//img")
                      //where node.Name == "a"
                      //&& node.Attributes["class"] != null
                      //&& node.Attributes["class"].Value.StartsWith("img_")
                  select node).ToList();
    var SaveList = new List<string>();
    foreach (HtmlNode node in imageNodes)
    {
        //Console.WriteLine(node);

        if (node.Attributes.Any(w => w.Name=="class" && w.Value.Contains("thumbnail") || w.Value.Contains("f1-photo-content")|| w.Value.Contains("fl-photo-img") ))
            continue;

        string imageUrl = node.Attributes["src"].Value;
                
    //}
        // Download the page
    //    logger.LogInformation($"Starting download of {pageUrl}");
    //string pageHtmlContent = await DownloadHtmlContentAsync(pageUrl);

    //// Extract image URLs from the page
    //var imageUrls = Regex.Matches(pageHtmlContent, "<img.*?src=[\"'](.*?)[\"']")
    //    .Cast<Match>()
    //    .Select(match => match.Groups[1].Value)
    //    .ToList();

    //// Download images
    //foreach (string imageUrl in imageUrls)
    //{
        if (imageUrl.Contains("Mirror-of-Dharma"))
            System.Diagnostics.Debugger.Break();


        // Skip images with "150x" in the URL
        if (imageUrl == "" || imageUrl.Contains("150x") || imageUrl.Contains("paperback") || imageUrl.Contains("Book") || imageUrl.Contains("book") || imageUrl.Contains("Gen-"))
        {
            continue;
        }

        try
        {
            string fileName = Path.GetFileName(imageUrl);
            string savePath = Path.Combine(baseDirectory, fileName);

            // Download the image
            await DownloadFile(imageUrl, savePath);

            if (!File.Exists(savePath)) { continue; }

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
                        logger.LogWarning($"Deleted image: {fileName} because it was smaller than 1024px");
                    }
                    else
                    {
                        logger.LogInformation($"Downloaded image: {fileName}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            // Logging error in downloading image
            logger.LogError($"Error downloading image: {imageUrl}. Error: {ex.Message}");
        }
    }

    // Download video
    try
    {
        // Extract the part of the URL after the last slash
        string fileName = pageUrl.Substring(pageUrl.LastIndexOf("/") + 1);

        // Replace any characters that are not allowed in filenames
        fileName = fileName.Replace('[', '-').Replace('\\', '-').Replace('/', '-')
            .Replace(':', '-').Replace('*', '-').Replace('?', '-')
            .Replace('"', '-').Replace('<', '-').Replace('>', '-')
            .Replace('|', '-');

        // Add the directory and extension to the filename
        string outputPath = Path.Combine(baseDirectory, $"{fileName}.mp4");

        // Prepare the processStartInfo
        var processStartInfo = new ProcessStartInfo
        {
            FileName = "youtube-dl",
            Arguments = $"-f mp4 -o \"{outputPath}\" {pageUrl}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        // Start the process
        var process = Process.Start(processStartInfo);

        // Wait for the process to finish
        process.WaitForExit();
    }
    catch (Exception ex)
    {
        logger.LogError($"Error downloading video: {ex.Message}");
    }

    pageCount++;
}

// Get the current date
DateTime currentDate = DateTime.Now;

// Get all files in the directory
var files = Directory.GetFiles(baseDirectory);

// Filter out files that are older than retentionDays or not images or videos
foreach (string file in files)
{
    FileInfo fileInfo = new FileInfo(file);
    if ((currentDate - fileInfo.LastWriteTime).TotalDays > retentionDays ||
        !new[] { ".jpg", ".jpeg", ".gif", ".bmp", ".mp4" }.Contains(fileInfo.Extension))
    {
        // Delete old files
        try
        {
            File.Delete(file);
            logger.LogInformation($"Deleted old file: {file}");
        }
        catch (Exception ex)
        {
            logger.LogError($"Error deleting file: {file}. Error: {ex.Message}");
        }
    }
}

async Task DownloadFile(string url, string outputPath)
{
    logger.LogInformation(url, outputPath);
    byte[] data = await client.GetByteArrayAsync(url);

    // Check if file already exists
    if (File.Exists(outputPath))
    {
        logger.LogWarning($"File already exists: {outputPath}");
        return;
    }

    await File.WriteAllBytesAsync(outputPath, data);
}
async Task<string> DownloadHtmlContentAsync(string url)
{
    using var httpClient = new HttpClient();
    var response = await httpClient.GetAsync(url);
    var stream = await response.Content.ReadAsStreamAsync();
    using var streamReader = new StreamReader(stream);
    return await streamReader.ReadToEndAsync();
}
