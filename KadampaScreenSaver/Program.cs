// File: KadampaScreenSaver/Program.cs
using System;
using System.Diagnostics;
using System.Drawing;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using HtmlAgilityPack;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Drawing.Imaging;
using System.Linq;
using static System.Net.Mime.MediaTypeNames;
using System.Net;
using Microsoft.Win32.TaskScheduler;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using TaskServiceTask = Microsoft.Win32.TaskScheduler.Task;
using Task = System.Threading.Tasks.Task;
using System.Globalization;
using Microsoft.Playwright;

HttpClient client = new HttpClient();
ILogger<Program> logger = null;
IConfigurationRoot configuration = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json", true, true)
    .Build();


if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
    if (configuration.GetValue<string>("Task Scheduler:StartTime") != null)
    {
        using (TaskService ts = new TaskService())
        {
            bool found = false;
            foreach (TaskServiceTask task in ts.RootFolder.Tasks)
            {
                if (task.Name == System.AppDomain.CurrentDomain.FriendlyName)
                {
                    found = true;
                    break;
                }
            }
            if (!found)
            {
                TaskDefinition td = ts.NewTask();
                td.RegistrationInfo.Author = "kadampa@alexreich.com";
                td.RegistrationInfo.Description = "Kadampa News Service for KadampaScreenSaver";
                td.Actions.Add(new ExecAction(Process.GetCurrentProcess().MainModule.FileName));

                DailyTrigger trigger = new DailyTrigger
                {
                    StartBoundary = DateTime.Today.Add(TimeSpan.Parse(configuration.GetValue<string>("Task Scheduler:StartTime"))), // Set the start time to 5 AM today
                    DaysInterval = 1 // Run every day
                };

                td.Triggers.Add(trigger);

                ts.RootFolder.RegisterTaskDefinition(System.AppDomain.CurrentDomain.FriendlyName, td);

                Console.WriteLine($"Task {System.AppDomain.CurrentDomain.FriendlyName} successfully registered!");
            }
        }
    }

int linkDepth = configuration.GetValue<int>("Policies:LinkDepth");
int retentionDays = configuration.GetValue<int>("Policies:RetentionDays");
string baseDirectory = configuration.GetValue<string>("Directories:Base");
string subDirectory = configuration.GetValue<string>("Directories:SubDirectory");
string fontName = configuration.GetValue<string>("PhotoText:Font");
// Define Kadampa brand colors
List<Color> brandColors = new List<Color>
                            {
                                ColorTranslator.FromHtml("#224486"), // Dark Blue
                                ColorTranslator.FromHtml("#A99886"), // Beige
                                ColorTranslator.FromHtml("#66B9C4"), // Light Blue
                                ColorTranslator.FromHtml("#358DCB"), // Medium Blue
                                ColorTranslator.FromHtml("#BE303C"), // Red
                                ColorTranslator.FromHtml("#48ADF4")  // Sky Blue
                            };
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

string webpageUrl = configuration.GetValue<string>("StartPage");
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

    var ogDescription = WebUtility.HtmlDecode(doc.DocumentNode.SelectSingleNode("//meta[@property='og:description']")?.GetAttributeValue("content", string.Empty));
    var title = WebUtility.HtmlDecode(doc.DocumentNode.SelectSingleNode("//meta[@property='og:title']")?.GetAttributeValue("content", string.Empty));
    var publishedTime = WebUtility.HtmlDecode(doc.DocumentNode.SelectSingleNode("//meta[@property='article:published_time']")?.GetAttributeValue("content", string.Empty));

    // Now, using LINQ to get all Images
    var imageNodes = doc.DocumentNode.SelectNodes("//*//img")?
        .Where(node =>
        !node.Attributes.Any(a => a.Value.Contains("pp-post-img")) &&
        (
        node.Attributes.Any(w => w.Name == "class" && w.Value.Contains("thumbnail")
        || w.Value.Contains("gallery-img")
        || w.Value.Contains("f1-photo-content")
        || w.Value.Contains("fl-photo-img"))
        )
        )
    .ToList();

    if (imageNodes == null || imageNodes.Count == 0)
    {
        logger.LogWarning($"No images found on page: {pageUrl}");
        continue;
    }

    // filter out images with certain text in the URL
    var imageUrls = new List<string>();
    foreach (var node in imageNodes)
    {
        string imageUrl = node.Attributes["src"].Value;
        string imageUrlLower = imageUrl.ToLower();

        if (
            imageUrl == "" ||
            imageUrlLower.Contains("150x") ||
            imageUrlLower.Contains("whatsapp-image") ||
            imageUrlLower.Contains("paperback") ||
            imageUrlLower.Contains("book") ||
            imageUrlLower.Contains("gen-") ||
            imageUrlLower.Contains("1024x") ||
            imageUrlLower.Contains("adobestock")
        )
        {
            continue;
        }

        imageUrls.Add(imageUrl);
    }

    Parallel.ForEach(imageUrls, imageUrl =>
    {

        try
        {
            //if (imageUrl.Contains("NKT-IKBU-New_Kadampa-2024-KMC-Fort-Collins-IMG_20231220_180758927.jpg"))
            //    System.Diagnostics.Debugger.Break();

            string fileName = Path.GetFileName(imageUrl);

            DateTime futureDate = new DateTime(9999, 12, 31);
            DateTime publishedDate = DateTime.ParseExact(publishedTime, "yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture).ToUniversalTime();
            TimeSpan dateDifference = futureDate - publishedDate;
            long reverseOrder = dateDifference.Days; // Unique for each day

            string identifier = reverseOrder.ToString("0000000"); // Ensures padding
            fileName = identifier + "_" + fileName; // Prepend to fileName


            //fileName = DateTime.Parse(publishedTime).ToString("yyyyMMdd_") + fileName;
            string savePath = Path.Combine(baseDirectory, fileName);

            // Download the image
            DownloadFile(imageUrl, savePath).Wait();

            if (!File.Exists(savePath)) { return; }

            // Check image dimensions
            byte[] imageBytes = File.ReadAllBytes(savePath);
            bool deleteImage = false;
            using (var memoryStream = new MemoryStream(imageBytes))
            using (var image = System.Drawing.Image.FromStream(memoryStream))
            {
                if (image.Width < 1024)
                {
                    // Set flag to delete image if width is less than 1024
                    deleteImage = true;
                }
                else
                {
                    logger.LogInformation($"Downloaded image: {fileName}");
                }
            }

            if (deleteImage)
            {
                File.Delete(savePath);
                logger.LogWarning($"Deleted image: {fileName} because it was smaller than 1024px");
            }
            else
            {
                if (configuration.GetValue<bool>("Directories:PhotoText"))
                {
                    // Add text to image
                    Bitmap newBitmap;
                    using (Bitmap bitmap = (Bitmap)System.Drawing.Image.FromFile(savePath))
                    {
                        using (Graphics graphics = Graphics.FromImage(bitmap))
                        {
                            string imageNameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
                            string textToAdd = $"{title}";
                            if (configuration.GetValue<bool>("PhotoText:DateInclude"))
                            {
                                textToAdd += configuration.GetValue<string>("PhotoText:DatePrefix");
                                textToAdd += $"{DateTime.Parse(publishedTime).ToString(configuration.GetValue<string>("PhotoText:DateFormat"))}";
                            }
                            //textToAdd += $"\n{ogDescription}";
                            if (configuration.GetValue<bool>("PhotoText:ImageFileName"))
                            {
                                textToAdd += $"\n{imageNameWithoutExtension}";
                            }

                            DrawTextOnImage(graphics, bitmap, textToAdd, fontName, brandColors, true);
                            DrawTextOnImage(graphics, bitmap, ogDescription, fontName, brandColors, false);
                        }
                        newBitmap = new Bitmap(bitmap);
                    }

                    newBitmap.Save(savePath); // Save the image file
                    newBitmap.Dispose();
                    File.SetCreationTime(savePath, DateTime.Parse(publishedTime));
                    File.SetLastWriteTime(savePath, DateTime.Parse(publishedTime));
                }

            }

        }
        catch (Exception ex)
        {
            // Logging error in downloading image
            logger.LogError($"Error downloading image: {imageUrl}. Error: {ex.Message}");
        }
    });

    //removing as youtube-dl is no longer maintained / may reviist
    //// Download video
    //try
    //{
    //    // Extract the part of the URL after the last slash
    //    string fileName = pageUrl.Substring(pageUrl.LastIndexOf("/") + 1);

    //    // Replace any characters that are not allowed in filenames
    //    fileName = fileName.Replace('[', '-').Replace('\\', '-').Replace('/', '-')
    //        .Replace(':', '-').Replace('*', '-').Replace('?', '-')
    //        .Replace('"', '-').Replace('<', '-').Replace('>', '-')
    //        .Replace('|', '-');

    //    // Add the directory and extension to the filename
    //    string outputPath = Path.Combine(baseDirectory, $"{fileName}.mp4");

    //    // Prepare the processStartInfo
    //    var processStartInfo = new ProcessStartInfo
    //    {
    //        FileName = "youtube-dl",
    //        Arguments = $"-f mp4 -o \"{outputPath}\" {pageUrl}",
    //        RedirectStandardOutput = true,
    //        RedirectStandardError = true,
    //        UseShellExecute = false,
    //        CreateNoWindow = true,
    //    };

    //    // Start the process
    //    var process = Process.Start(processStartInfo);

    //    // Wait for the process to finish
    //    process.WaitForExit();
    //}
    //catch (Exception ex)
    //{
    //    logger.LogError($"Error downloading video: {ex.Message}");
    //}

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
Color FindContrastingColor(Color baseColor, List<Color> brandColors)
{
    Color contrastingColor = brandColors[0];
    double maxDistance = 0;

    foreach (var brandColor in brandColors)
    {
        // Calculate the Euclidean distance in the RGB space
        double distance = Math.Sqrt(
            Math.Pow(brandColor.R - baseColor.R, 2) +
            Math.Pow(brandColor.G - baseColor.G, 2) +
            Math.Pow(brandColor.B - baseColor.B, 2));

        if (distance > maxDistance)
        {
            maxDistance = distance;
            contrastingColor = brandColor;
        }
    }

    return contrastingColor;
}
Color CalculateAverageColor(Bitmap bmp, int areaHeightPercent)
{
    int totalR = 0, totalG = 0, totalB = 0;
    int startY = 0;
    int endY = bmp.Height * areaHeightPercent / 100;

    for (int y = startY; y < endY; y++)
    {
        for (int x = 0; x < bmp.Width; x++)
        {
            Color pixel = bmp.GetPixel(x, y);
            totalR += pixel.R;
            totalG += pixel.G;
            totalB += pixel.B;
        }
    }

    int totalPixels = bmp.Width * (endY - startY);
    return Color.FromArgb(totalR / totalPixels, totalG / totalPixels, totalB / totalPixels);
}


async Task DownloadFile(string url, string outputPath)
{
    logger.LogInformation(url, outputPath);
    byte[] data = await client.GetByteArrayAsync(url);

    // Check if file already exists
    if (File.Exists(outputPath))
    {
        File.Delete(outputPath);
        //logger.LogWarning($"File already exists: {outputPath}");
        //return;
    }

    await File.WriteAllBytesAsync(outputPath, data);
}

async Task<string> DownloadHtmlContentAsync(string url)
{
    using var playwright = await Playwright.CreateAsync();

    // Try launching local Chrome with stealth-like args
    var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
    {
        Channel = "chrome", // or "msedge"
        Headless = true,
        IgnoreDefaultArgs = new[] { "--enable-automation" },
        Args = new[]
        {
            "--disable-blink-features=AutomationControlled"
        }
    });

    // Create a context that spoofs typical browser properties
    var context = await browser.NewContextAsync(new BrowserNewContextOptions
    {
        UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
                    "(KHTML, like Gecko) Chrome/110.0.5481.77 Safari/537.36",
        ViewportSize = new ViewportSize { Width = 1920, Height = 1080 }
        // you can also set deviceScaleFactor, locale, timezoneId, geolocation, etc.
    });

    // Hide 'webdriver' property
    await context.AddInitScriptAsync(@"() => {
        Object.defineProperty(navigator, 'webdriver', {
            get: () => undefined
        });
    }");

    var page = await context.NewPageAsync();

    await page.GotoAsync(url, new PageGotoOptions
    {
        WaitUntil = WaitUntilState.DOMContentLoaded,
        Timeout = 60000
    });

    // Wait for some element to confirm the page loaded
    await page.WaitForSelectorAsync("body", new PageWaitForSelectorOptions { Timeout = 10000 });

    // Or wait for a specific item that indicates content is fully loaded
    // await page.WaitForSelectorAsync("a[href^='https://kadampa.org/2025']");

    await page.WaitForTimeoutAsync(2000);

    string content = await page.ContentAsync();
    await browser.CloseAsync();
    return content;
}




// General function to handle text drawing
void DrawTextOnImage(Graphics graphics, Bitmap bitmap, string text, string fontName, List<Color> brandColors, bool isHeader)
{
    float targetTextWidth = bitmap.Width * 0.80f; // Adjusted for more margin
    int initialFontSize = isHeader ? 10 : 8; // Starting font size
    int maxFontSize = 60; // More reasonable maximum
    SizeF textSize;
    System.Drawing.Font arialFont;

    Color avgColor = isHeader ? CalculateAverageColor(bitmap, 20) : CalculateAverageColor(bitmap, 80);
    Color textColor = FindContrastingColor(avgColor, brandColors);

    int fontSize = initialFontSize;
    do
    {
        arialFont = new System.Drawing.Font(fontName, fontSize, GraphicsUnit.Pixel);
        textSize = graphics.MeasureString(text, arialFont, (int)targetTextWidth);
        fontSize++;
    } while (textSize.Width < targetTextWidth && fontSize <= maxFontSize);

    fontSize--;
    if (fontSize > 0)
    {
        using (arialFont = new System.Drawing.Font(fontName, fontSize, GraphicsUnit.Pixel))
        {
            textSize = graphics.MeasureString(text, arialFont, (int)targetTextWidth);
            float x = (bitmap.Width - textSize.Width) / 2;
            float y = isHeader ? Math.Max(10, (bitmap.Height * 0.05f)) : (bitmap.Height - textSize.Height - bitmap.Height * 0.02f); // Adjusted Y position for footer

            graphics.DrawString(text, arialFont, new SolidBrush(textColor), new RectangleF(x, y, targetTextWidth, textSize.Height));
        }
    }
}

