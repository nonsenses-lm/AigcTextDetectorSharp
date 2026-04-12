using System.Diagnostics;
using AigcDetectorSharp.Core.Services;
using Microsoft.Web.WebView2.WinForms;

namespace AigcDetectorSharp.UI;

class Program
{
    private static DetectorService? _detectorZh;
    private static DetectorService? _detectorEn;
    private static WebApplication? _app;

    [STAThread]
    static void Main(string[] args)
    {
        var baseDir = AppContext.BaseDirectory;
        InitDetectors(baseDir);

        var port = 5000;
        var portArg = args.FirstOrDefault(a => a.StartsWith("--port="));
        if (portArg != null)
            port = int.Parse(portArg.Split('=')[1]);

        var host = OperatingSystem.IsWindows() ? "127.0.0.1" : "0.0.0.0";
        var hostArg = args.FirstOrDefault(a => a.StartsWith("--host="));
        if (hostArg != null)
            host = hostArg.Split('=')[1];

        Task.Run(() => RunServer(host, port, args));
        var displayHost = host == "0.0.0.0" ? "localhost" : host;
        Console.WriteLine($"AIGC Detector Server running at http://{displayHost}:{port}");

        if (!args.Contains("--server"))
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            var form = new Form
            {
                Text = "AIGC Detector - nonSenses ÎÞ¼£",
                Width = 1024,
                Height = 768,
                Icon = new Icon("./wwwroot/favicon.ico")
            };

            var webView = new WebView2
            {
                Dock = DockStyle.Fill
            };
            form.Controls.Add(webView);

            form.Load += async (sender, args) =>
            {
                await webView.EnsureCoreWebView2Async(null);
                webView.CoreWebView2.Navigate($"http://127.0.0.1:{port}");
            };

            Application.Run(form);
        }
        else
        {
            try
            {
                Console.WriteLine("Opening browser...");
                Process.Start(new ProcessStartInfo
                {
                    FileName = $"http://{displayHost}:{port}",
                    UseShellExecute = true
                });
            }
            catch
            {
                Console.WriteLine("Could not open browser automatically. Please open the URL manually.");
            }

        }

        _detectorZh?.Dispose();
        _detectorEn?.Dispose();
    }

    static async Task RunServer(string host, int port, string[]? appArgs = null)
    {
        Environment.SetEnvironmentVariable("ASPNETCORE_URLS", $"http://{host}:{port}");

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = appArgs ?? Array.Empty<string>(),
            WebRootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot")
        });

        builder.Logging.ClearProviders();

        _app = builder.Build();

        // Serve static files from wwwroot
        _app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(
                Path.Combine(AppContext.BaseDirectory, "wwwroot"))
        });

        // Default route
        _app.MapGet("/", async context =>
        {
            context.Response.Redirect("/index.html");
        });

        // API endpoint for detection
        _app.MapPost("/api/detect", async context =>
        {
            try
            {
                var request = await context.Request.ReadFromJsonAsync<DetectRequest>();
                if (request == null || string.IsNullOrEmpty(request.Text))
                {
                    context.Response.StatusCode = 400;
                    await context.Response.WriteAsJsonAsync(new { error = "Text is required" });
                    return;
                }

                var model = request.Model ?? "zh";
                var detector = model == "en" ? _detectorEn : _detectorZh;

                if (detector == null)
                {
                    context.Response.StatusCode = 400;
                    await context.Response.WriteAsJsonAsync(new { error = "Model not loaded" });
                    return;
                }

                var result = detector.Detect(request.Text);
                await context.Response.WriteAsJsonAsync(new
                {
                    label = result.Label,
                    probability = result.Probability,
                    chunks = result.Chunks.Select(c => new
                    {
                        index = c.Index,
                        text = c.Text,
                        label = c.Label,
                        probability = c.Probability
                    }),
                    model = model,
                    timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                });
            }
            catch (Exception ex)
            {
                context.Response.StatusCode = 500;
                await context.Response.WriteAsJsonAsync(new { error = ex.Message });
            }
        });

        // API endpoint for file reading by path
        _app.MapPost("/api/readFile", async context =>
        {
            try
            {
                var request = await context.Request.ReadFromJsonAsync<FileRequest>();
                if (request == null || string.IsNullOrEmpty(request.Path))
                {
                    context.Response.StatusCode = 400;
                    await context.Response.WriteAsJsonAsync(new { error = "Path is required" });
                    return;
                }

                if (File.Exists(request.Path) && FileService.IsSupportedFile(request.Path))
                {
                    var text = FileService.ReadFile(request.Path);
                    await context.Response.WriteAsJsonAsync(new { text, path = request.Path });
                }
                else
                {
                    context.Response.StatusCode = 400;
                    await context.Response.WriteAsJsonAsync(new { error = "Unsupported file format" });
                }
            }
            catch (Exception ex)
            {
                context.Response.StatusCode = 500;
                await context.Response.WriteAsJsonAsync(new { error = ex.Message });
            }
        });

        // API endpoint for file upload
        _app.MapPost("/api/upload", async context =>
        {
            try
            {
                if (!context.Request.HasFormContentType)
                {
                    context.Response.StatusCode = 400;
                    await context.Response.WriteAsJsonAsync(new { error = "Expected multipart form" });
                    return;
                }

                var form = await context.Request.ReadFormAsync();
                var file = form.Files.FirstOrDefault();

                if (file == null || file.Length == 0)
                {
                    context.Response.StatusCode = 400;
                    await context.Response.WriteAsJsonAsync(new { error = "No file uploaded" });
                    return;
                }

                var ext = Path.GetExtension(file.FileName).ToLower();
                if (!new[] { ".txt", ".md", ".docx", ".pdf" }.Contains(ext))
                {
                    context.Response.StatusCode = 400;
                    await context.Response.WriteAsJsonAsync(new { error = "Unsupported file format. Supported: .txt, .md, .docx, .pdf" });
                    return;
                }

                // Save to temp file and read
                var tempPath = Path.GetTempFileName() + ext;
                try
                {
                    using (var stream = File.Create(tempPath))
                    {
                        await file.CopyToAsync(stream);
                    }
                    var text = FileService.ReadFile(tempPath);
                    await context.Response.WriteAsJsonAsync(new { text, name = file.FileName });
                }
                finally
                {
                    if (File.Exists(tempPath))
                        File.Delete(tempPath);
                }
            }
            catch (Exception ex)
            {
                context.Response.StatusCode = 500;
                await context.Response.WriteAsJsonAsync(new { error = ex.Message });
            }
        });

        // API endpoint to exit application
        _app.MapPost("/api/exit", async context =>
        {
            await context.Response.WriteAsJsonAsync(new { message = "Shutting down..." });
            
            // Stop the server after sending response
            _ = Task.Delay(500).ContinueWith(_ =>
            {
                _app?.StopAsync();
                Environment.Exit(0);
            });
        });

        await _app.RunAsync();
    }

    static void InitDetectors(string baseDir)
    {
        var dir = new DirectoryInfo(baseDir);
        while (dir != null)
        {
            var zhPath = Path.Combine(dir.FullName, "model_zhv3");
            var enPath = Path.Combine(dir.FullName, "model_env3");

            if (Directory.Exists(zhPath) && _detectorZh == null)
                _detectorZh = new DetectorService(zhPath);
            if (Directory.Exists(enPath) && _detectorEn == null)
                _detectorEn = new DetectorService(enPath);

            if (_detectorZh != null && _detectorEn != null)
                break;

            dir = dir.Parent;
        }
    }

    record DetectRequest(string Text, string? Model);
    record FileRequest(string Path);
}
