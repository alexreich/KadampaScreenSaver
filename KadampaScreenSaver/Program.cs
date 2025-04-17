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
using KadampaScreenSaver;

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

// Create UrlLogger instance (store log file in baseDirectory for convenience)
string urlLogFile = Path.Combine(baseDirectory, "VisitedUrls.log");
UrlLogger urlLogger = new UrlLogger(urlLogFile);

// Cleanup old URL logs
urlLogger.Cleanup(30);

// Download the webpage
logger.LogInformation("Starting download of webpage");

// Extract page URLs from HTML
string htmlContent = await DownloadHtmlContentAsync(webpageUrl);
var pageUrls = Regex.Matches(htmlContent, "<a.*?href=[\"'](.*?)[\"']")
    .Cast<Match>()
    .Select(m => m.Groups[1].Value)
    // resolve relative → absolute
    .Select(href => {
        var u = new Uri(href, UriKind.RelativeOrAbsolute);
        return u.IsAbsoluteUri
            ? u
            : new Uri(new Uri(webpageUrl), href);
    })
    // skip anything we already logged
    .Where(u => !urlLogger.AlreadyVisited(u.AbsoluteUri))
    .Select(u => u.AbsoluteUri)
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

    // Record that we've visited this URL
    urlLogger.LogUrl(pageUrl);

    var (innerHtml, imageUrls) = await LoadContentAndImagesAsync(pageUrl);

    if (imageUrls == null || imageUrls.Count == 0)
    {
        logger.LogWarning($"No images found on page: {pageUrl}");
        continue;
    }

    // filter out images with certain text in the URL
    var filteredImageUrls = new List<string>();
    foreach (var imageUrl in imageUrls)
    {
        string imageUrlLower = imageUrl.ToLower();

        if (
            imageUrl == "" ||
            imageUrlLower.Contains("150x") ||
            imageUrlLower.Contains("whatsapp-image") ||
            imageUrlLower.Contains("paperback") ||
            imageUrlLower.Contains("book") ||
            imageUrlLower.Contains("gen-") ||
            imageUrlLower.Contains("1024x") ||
            imageUrlLower.Contains("adobestock") ||
            imageUrlLower.Contains("heic_")
        )
        {
            continue;
        }

        filteredImageUrls.Add(imageUrl);
    }
    var html = await LoadContentAndImagesAsync(pageUrl);
    var doc = new HtmlDocument();
    doc.LoadHtml(html.htmlContent);

    var ogDescription = doc.DocumentNode.SelectSingleNode("//meta[@property='og:description']")?.GetAttributeValue("content", string.Empty);
    var title = CleanText(doc.DocumentNode.SelectSingleNode("//meta[@property='og:title']")?.GetAttributeValue("content", string.Empty));
    var publishedTime = doc.DocumentNode.SelectSingleNode("//meta[@property='article:published_time']")?.GetAttributeValue("content", string.Empty);

    Parallel.ForEach(filteredImageUrls, imageUrl =>
    {
        try
        {
            string fileName = Path.GetFileName(imageUrl);

            DateTime futureDate = new DateTime(9999, 12, 31);
            DateTime publishedDate = DateTime.UtcNow; // Use current date as published date
            TimeSpan dateDifference = futureDate - publishedDate;
            long reverseOrder = dateDifference.Days; // Unique for each day

            string identifier = reverseOrder.ToString("0000000"); // Ensures padding
            fileName = identifier + "_" + fileName; // Prepend to fileName

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
                                textToAdd += $"{DateTime.UtcNow.ToString(configuration.GetValue<string>("PhotoText:DateFormat"))}";
                            }
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
                    File.SetCreationTime(savePath, DateTime.UtcNow);
                    File.SetLastWriteTime(savePath, DateTime.UtcNow);
                }
            }
        }
        catch (Exception ex)
        {
            // Logging error in downloading image
            logger.LogError($"Error downloading image: {imageUrl}. Error: {ex.Message}");
        }
    });

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
        !new[] { ".jpg", ".jpeg", ".gif", ".bmp", ".mp4", ".log" }.Contains(fileInfo.Extension))
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

// At the very end, cleanup again (optional)
urlLogger.Cleanup(30);

async Task<(string htmlContent, List<string> imageUrls)> LoadContentAndImagesAsync(string url)
{
    using var playwright = await Playwright.CreateAsync();
    var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
    {
        Channel = "msedge",
        Headless = true,
        IgnoreDefaultArgs = new[] { "--enable-automation" },
        Args = new[] { "--disable-blink-features=AutomationControlled" }
    });

    var context = await browser.NewContextAsync(new BrowserNewContextOptions
    {
        UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
                    "(KHTML, like Gecko) Chrome/110.0.5481.77 Safari/537.36",
        ViewportSize = new ViewportSize { Width = 1920, Height = 1080 }
    });

    await context.AddInitScriptAsync(@"
        () => {
            Object.defineProperty(navigator, 'webdriver', {
                get: () => undefined
            });
        }
    ");

    var page = await context.NewPageAsync();
    await page.GotoAsync(url, new PageGotoOptions
    {
        WaitUntil = WaitUntilState.NetworkIdle,
        Timeout = 60000
    });

    await page.WaitForSelectorAsync("body", new PageWaitForSelectorOptions { Timeout = 10000 });
    await page.WaitForTimeoutAsync(2000);

    string content = await page.ContentAsync();

    // Optionally wait for images
    await page.WaitForSelectorAsync("img", new PageWaitForSelectorOptions
    {
        Timeout = 10000,
        State = WaitForSelectorState.Attached
    });
    var images = await page.EvaluateAsync<string[]>(
        "Array.from(document.querySelectorAll('img')).map(img => img.src)"
    );

    await browser.CloseAsync();
    return (content, images.Where(src => !string.IsNullOrWhiteSpace(src)).Distinct().ToList());
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
Color CalculateAverageColor(Bitmap bmp, int startYPercent, int endYPercent)
{
    int height = bmp.Height;
    int startY = height * startYPercent / 100;
    int endY = height * endYPercent / 100;

    long totalR = 0, totalG = 0, totalB = 0;
    long pixelCount = 0;

    for (int y = startY; y < endY; y++)
    {
        for (int x = 0; x < bmp.Width; x++)
        {
            Color c = bmp.GetPixel(x, y);
            totalR += c.R;
            totalG += c.G;
            totalB += c.B;
            pixelCount++;
        }
    }

    int avgR = (int)(totalR / pixelCount);
    int avgG = (int)(totalG / pixelCount);
    int avgB = (int)(totalB / pixelCount);

    return Color.FromArgb(avgR, avgG, avgB);
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
        Channel = "msedge", // or "msedge"
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

// Convert 0–255 sRGB => linear space => L
double ToRelativeLuminance(Color c)
{
    // sRGB => linear
    double Rsrgb = c.R / 255.0;
    double Gsrgb = c.G / 255.0;
    double Bsrgb = c.B / 255.0;

    double R = (Rsrgb <= 0.03928) ? (Rsrgb / 12.92) : Math.Pow((Rsrgb + 0.055) / 1.055, 2.4);
    double G = (Gsrgb <= 0.03928) ? (Gsrgb / 12.92) : Math.Pow((Gsrgb + 0.055) / 1.055, 2.4);
    double B = (Bsrgb <= 0.03928) ? (Bsrgb / 12.92) : Math.Pow((Bsrgb + 0.055) / 1.055, 2.4);

    // Per W3C formula: L = 0.2126*R + 0.7152*G + 0.0722*B
    return 0.2126 * R + 0.7152 * G + 0.0722 * B;
}
Color FindBestTextColor(Color background, List<Color> brandColors)
{
    // First, find which brand color yields the highest ratio
    Color bestBrand = brandColors[0];
    double bestRatio = 0.0;

    foreach (var brandColor in brandColors)
    {
        double ratio = GetContrastRatio(brandColor, background);
        if (ratio > bestRatio)
        {
            bestRatio = ratio;
            bestBrand = brandColor;
        }
    }

    // If that brand color is still too low, check black/white
    // (You can choose your own threshold, e.g. 4.5 or 3.0.)
    const double minReadableRatio = 3.0;

    if (bestRatio >= minReadableRatio)
    {
        return bestBrand; // good enough
    }

    // Try black & white
    double blackRatio = GetContrastRatio(Color.Black, background);
    double whiteRatio = GetContrastRatio(Color.White, background);

    if (blackRatio > whiteRatio)
    {
        if (blackRatio >= minReadableRatio) return Color.Black;
        return bestBrand; // fallback to brand color if black is also too low
    }
    else
    {
        if (whiteRatio >= minReadableRatio) return Color.White;
        return bestBrand; // fallback
    }
}

// ratio = (L1 + 0.05) / (L2 + 0.05), where L1 >= L2
double GetContrastRatio(Color foreground, Color background)
{
    double fLum = ToRelativeLuminance(foreground);
    double bLum = ToRelativeLuminance(background);

    double lighter = Math.Max(fLum, bLum);
    double darker = Math.Min(fLum, bLum);

    return (lighter + 0.05) / (darker + 0.05);
}

// General function to handle text drawing
void DrawTextOnImage(Graphics graphics, Bitmap bitmap, string text,
                     string fontName, List<Color> brandColors, bool isHeader)
{
    // 1) Sample the background, pick a good foreground color
    int startPercent = isHeader ? 0 : 90;
    int endPercent = isHeader ? 10 : 100;
    Color backgroundAvg = CalculateAverageColor(bitmap, startPercent, endPercent);
    Color textColor = FindBestTextColor(backgroundAvg, brandColors);

    // 2) Figure out our layout box
    float boxTop = bitmap.Height * startPercent / 100f;
    float boxHeight = bitmap.Height * (endPercent - startPercent) / 100f;
    float boxWidth = bitmap.Width * 0.80f;          // 80% of the image width
    float boxLeft = (bitmap.Width - boxWidth) / 2; // centered

    // 3) Find the largest font size that fits both width and height
    int initialSize = isHeader ? 16 : 12;
    int bestSize = initialSize;
    const int maxSize = 72;

    for (int size = initialSize; size <= maxSize; size++)
    {
        using var testFont = new System.Drawing.Font(fontName, size, GraphicsUnit.Pixel);

        // measure with wrapping at boxWidth
        SizeF measured = graphics.MeasureString(
            text,
            testFont,
            (int)boxWidth,
            StringFormat.GenericDefault
        );

        if (measured.Width > boxWidth ||
            measured.Height > boxHeight)
        {
            // we’ve exceeded the box in either dimension,
            // so stick with the last size that fit
            break;
        }

        bestSize = size;
    }

    // 4) Draw it for real
    using var finalFont = new System.Drawing.Font(fontName, bestSize, GraphicsUnit.Pixel);
    var sf = StringFormat.GenericDefault;
    var layout = new RectangleF(boxLeft,
                                isHeader
                                  ? boxTop + 5              // a little padding
                                  : boxTop + (boxHeight - graphics.MeasureString(text, finalFont, (int)boxWidth).Height) - 5,
                                boxWidth,
                                boxHeight);

    using var brush = new SolidBrush(textColor);
    graphics.DrawString(text, finalFont, brush, layout, sf);
}


// Add the CleanText helper method
static string CleanText(string input)
{
    if (string.IsNullOrEmpty(input))
        return string.Empty;

    // Decode HTML entities
    string deEntitized = HtmlEntity.DeEntitize(input);

    // Remove any remaining HTML tags
    var doc = new HtmlDocument();
    doc.LoadHtml(deEntitized);
    return doc.DocumentNode.InnerText;
}
public class UrlLogger
{
    private readonly string _filePath;
    private readonly HashSet<string> _visited;

    public UrlLogger(string filePath)
    {
        _filePath = filePath;
        if (!File.Exists(_filePath))
            File.WriteAllText(_filePath, "");

        _visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Load & normalize every line we wrote previously
        foreach (var line in File.ReadAllLines(_filePath))
        {
            var parts = line.Split('\t', 2);
            if (parts.Length != 2) continue;

            // parts[1] is the URL we logged (already normalized when written)
            var norm = parts[1].Trim();
            _visited.Add(norm);
        }
    }

    /// <summary>
    /// True if we've already visited this URL (in normalized form).
    /// </summary>
    public bool AlreadyVisited(string url)
        => _visited.Contains(NormalizeUrl(url));

    /// <summary>
    /// Adds a timestamped entry of the normalized URL, if not already there.
    /// </summary>
    public void LogUrl(string url)
    {
        var norm = NormalizeUrl(url);
        if (_visited.Add(norm))
        {
            var timestamp = DateTime.UtcNow.ToString("O"); // ISO‑8601
            File.AppendAllText(_filePath, $"{timestamp}\t{norm}{Environment.NewLine}");
        }
    }

    /// <summary>
    /// Drops any logged URLs older than retentionDays, both on disk and in memory.
    /// </summary>
    public void Cleanup(int retentionDays)
    {
        if (!File.Exists(_filePath)) return;
        var cutoff = DateTime.UtcNow.AddDays(-retentionDays);

        var keptLines = new List<string>();
        foreach (var line in File.ReadAllLines(_filePath))
        {
            var parts = line.Split('\t', 2);
            if (parts.Length != 2) continue;
            if (DateTime.TryParse(parts[0], null, DateTimeStyles.RoundtripKind, out var time)
                && time >= cutoff)
            {
                keptLines.Add(line);
            }
        }

        // Rewrite only the kept entries
        File.WriteAllLines(_filePath, keptLines);

        // Refresh the in‑memory set
        _visited.Clear();
        foreach (var line in keptLines)
        {
            var urlPart = line.Split('\t', 2)[1].Trim();
            _visited.Add(urlPart);
        }
    }

    /// <summary>
    /// Normalizes a URL by lower‑casing, trimming trailing slash on the path,
    /// and preserving the query string.
    /// </summary>
    private string NormalizeUrl(string raw)
    {
        try
        {
            var uri = new Uri(raw);
            var path = uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
            var query = uri.Query; // e.g. "?page=2" or ""
            return (path + query).ToLowerInvariant();
        }
        catch
        {
            // If it's not a valid URI, just trim+lowercase it
            return raw.Trim().ToLowerInvariant();
        }
    }
}

