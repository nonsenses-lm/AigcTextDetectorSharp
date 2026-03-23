using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Tokenizers.DotNet;
using AigcDetectorSharp.Core.Models;
using System.Text;

namespace AigcDetectorSharp.Core.Services;

public class DetectorService : IDisposable
{
    private const int MaxTokens = 512;
    private readonly InferenceSession _session;
    private readonly Tokenizer _tokenizer;
    private readonly bool _isEnglish;

    public DetectorService(string modelDir)
    {
        var onnxFile = Directory.GetFiles(modelDir, "*.onnx").First();
        var tokenizerFile = Path.Combine(modelDir, "tokenizer_export", "tokenizer.json");

        var sessionOptions = new SessionOptions();
        sessionOptions.IntraOpNumThreads = 1;
        sessionOptions.InterOpNumThreads = 1;

        _session = new InferenceSession(onnxFile, sessionOptions);
        _tokenizer = new Tokenizer(vocabPath: tokenizerFile);
        _isEnglish = modelDir.Contains("en");
    }

    public string ModelName => _isEnglish ? "en" : "zh";

    public DetectionResult Detect(string text)
    {
        var allIds = _tokenizer.Encode(text).Select(id => (long)id).ToArray();

        if (allIds.Length <= MaxTokens)
        {
            var inputIds = allIds.Take(MaxTokens).ToArray();
            var probs = PredictRaw(inputIds);
            var label = probs[0] >= 0.5 ? "Human" : "AI";
            var prob = probs[0] >= 0.5 ? probs[0] : 1 - probs[0];
            return new DetectionResult(label, prob, new List<ChunkResult>
            {
                new(1, text, label, prob)
            });
        }

        // 按行切分
        var lines = text.Split('\n');
        var chunks = new List<string>();
        var currentChunk = new StringBuilder();
        int currentTokens = 0;

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var lineTokens = _tokenizer.Encode(line).Count();
            if (currentTokens + lineTokens > MaxTokens && currentChunk.Length > 0)
            {
                chunks.Add(currentChunk.ToString());
                currentChunk.Clear();
                currentTokens = 0;
            }
            currentChunk.AppendLine(line);
            currentTokens += lineTokens;
        }
        if (currentChunk.Length > 0)
            chunks.Add(currentChunk.ToString());

        // 预测各chunk
        var humanProbs = new List<float>();
        var chunkResults = new List<ChunkResult>();

        for (int i = 0; i < chunks.Count; i++)
        {
            var chunkText = chunks[i];
            var inputIds = _tokenizer.Encode(chunkText).Select(id => (long)id).Take(MaxTokens).ToArray();
            var probs = PredictRaw(inputIds);
            humanProbs.Add(probs[0]);

            var label = probs[0] >= 0.5 ? "Human" : "AI";
            var prob = probs[0] >= 0.5 ? probs[0] : 1 - probs[0];
            chunkResults.Add(new ChunkResult(i + 1, chunkText.Trim(), label, prob));
        }

        // 对数几率平均（去除最高最低值）
        var logOdds = humanProbs.Select(p => Math.Log(p / (1 - p + 1e-10))).OrderBy(x => x).ToList();
        IEnumerable<double> trimmed = logOdds.Count >= 3
            ? logOdds.Skip(1).Take(logOdds.Count - 2)
            : logOdds;
        var avgLogOdds = trimmed.Average();
        var finalProb = (float)(1.0 / (1.0 + Math.Exp(-avgLogOdds)));

        var finalLabel = finalProb > 0.5 ? "Human" : "AI";
        var finalScore = finalProb > 0.5 ? finalProb : 1 - finalProb;

        return new DetectionResult(finalLabel, finalScore, chunkResults);
    }

    private float[] PredictRaw(long[] inputIds)
    {
        var seqLen = inputIds.Length;
        var attentionMask = Enumerable.Repeat(1L, seqLen).ToArray();

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input_ids", new DenseTensor<long>(inputIds, [1, seqLen])),
            NamedOnnxValue.CreateFromTensor("attention_mask", new DenseTensor<long>(attentionMask, [1, seqLen]))
        };

        if (!_isEnglish)
        {
            var tokenTypeIds = new long[seqLen];
            inputs.Add(NamedOnnxValue.CreateFromTensor("token_type_ids", new DenseTensor<long>(tokenTypeIds, [1, seqLen])));
        }

        var logits = _session.Run(inputs).First().AsEnumerable<float>().ToArray();
        var maxLogit = logits.Max();
        var exps = logits.Select(x => Math.Exp(x - maxLogit)).ToArray();
        var sumExps = exps.Sum();
        return exps.Select(x => (float)(x / sumExps)).ToArray();
    }

    public void Dispose()
    {
        _session?.Dispose();
        _tokenizer?.Dispose();
    }
}
