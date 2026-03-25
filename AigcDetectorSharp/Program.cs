using McMaster.Extensions.CommandLineUtils;
using AigcDetectorSharp.Core.Services;

[Command("aigc-detector", Description = "AIGC Text Detector - Detect whether text is human-written or AI-generated")]
[HelpOption("-h|--help")]
class Program
{
    [Option("-f|--file", "Read text from file (.txt, .md, .docx, .pdf)", CommandOptionType.SingleValue)]
    public string? FilePath { get; set; }

    [Option("-m|--model", "Model: zh (Chinese, default) or en (English)", CommandOptionType.SingleValue)]
    public string? Model { get; set; }

    [Option("-p|--path", "Custom model directory (must contain .onnx and tokenizer_export/)", CommandOptionType.SingleValue)]
    public string? ModelPath { get; set; }

    [Option("--echo", "Echo source text", CommandOptionType.NoValue)]
    public bool Echo { get; set; }

    [Option("-s|--separator", "Chunk separator (default: \\n, use | for multiple)", CommandOptionType.SingleValue)]
    public string? Separator { get; set; }

    [Argument(0, "text", "Text to detect (optional)")]
    public string? Text { get; set; }

    static int Main(string[] args) => CommandLineApplication.Execute<Program>(args);

    private void OnExecute()
    {
        var baseDir = AppContext.BaseDirectory;
        var modelDir = GetModelDir(baseDir);
        var separator = Separator ?? "\n";

        using var detector = new DetectorService(modelDir, separator);

        string? text = null;
        if (FilePath != null)
            text = FileService.ReadFile(FilePath);
        else if (Text != null)
            text = Text;

        if (text != null)
        {
            var result = detector.Detect(text);
            if (Echo)
            {
                Console.WriteLine("=== Source ===");
                Console.WriteLine(text);
                Console.WriteLine("=== Result ===");
            }
            Console.WriteLine($"{result.Label} {result.Probability:F4}");
            return;
        }

        Console.WriteLine($"AIGC Detector [{detector.ModelName.ToUpper()}] - Type text to detect, 'quit' to exit\n");
        while (true)
        {
            Console.Write("> ");
            var input = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(input) || input.Equals("quit", StringComparison.OrdinalIgnoreCase))
                break;

            var result = detector.Detect(input);
            Console.WriteLine($"{result.Label} {result.Probability:P2}");
        }
    }

    private string GetModelDir(string baseDir)
    {
        if (!string.IsNullOrEmpty(ModelPath))
            return ModelPath;

        var modelKey = (Model ?? "zh").ToLower();
        var dirName = modelKey == "en" ? "model_env3" : "model_zhv3";

        var dir = new DirectoryInfo(baseDir);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, dirName);
            if (Directory.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException($"Model directory not found: {dirName}");
    }
}
