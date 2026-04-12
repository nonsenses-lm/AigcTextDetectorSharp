using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Tokenizers.DotNet;
using AigcDetectorSharp.Core.Models;
using System.Runtime.InteropServices;
using System.Text;

namespace AigcDetectorSharp.Core.Services;

public class DetectorService : IDisposable
{
    private const int MaxTokens = 512;
    private readonly InferenceSession _session;
    private readonly Tokenizer _tokenizer;
    private readonly bool _isEnglish;
    private readonly string[] _separators;

    public DetectorService(string modelDir, string separator = "\n", int? intraOpNumThreads = null, int? interOpNumThreads = null)
    {
        var onnxFile = Directory.GetFiles(modelDir, "*.onnx").First();
        var tokenizerFile = Path.Combine(modelDir, "tokenizer_export", "tokenizer.json");

        var sessionOptions = new SessionOptions();
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            sessionOptions.IntraOpNumThreads = intraOpNumThreads ?? Environment.ProcessorCount - 1;
            sessionOptions.InterOpNumThreads = interOpNumThreads ?? 1;
        }
        else
        {
            sessionOptions.IntraOpNumThreads = intraOpNumThreads ?? 1;
            sessionOptions.InterOpNumThreads = interOpNumThreads ?? 1;
        }

        _session = new InferenceSession(onnxFile, sessionOptions);
        _tokenizer = new Tokenizer(vocabPath: tokenizerFile);
        _isEnglish = modelDir.Contains("en");
        _separators = separator.Split('|');
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

        // 按分隔符切分
        var lines = text.Split(_separators, StringSplitOptions.RemoveEmptyEntries);
        var chunks = new List<string>();
        var currentChunk = new StringBuilder();
        int currentTokens = 0;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line)) continue;
            var lineTokens = _tokenizer.Encode(line).Count();

            // 行超过MaxTokens，需要进一步拆分
            if (lineTokens > MaxTokens)
            {
                // 先保存当前chunk
                if (currentChunk.Length > 0)
                {
                    chunks.Add(currentChunk.ToString().Trim());
                    currentChunk.Clear();
                    currentTokens = 0;
                }
                // 按句号拆分长行
                foreach (var subChunk in SplitLongLine(line))
                {
                    chunks.Add(subChunk.Trim());
                }
                continue;
            }

            if (currentTokens + lineTokens > MaxTokens && currentChunk.Length > 0)
            {
                chunks.Add(currentChunk.ToString().Trim());
                currentChunk.Clear();
                currentTokens = 0;
            }
            if (currentChunk.Length > 0)
                currentChunk.AppendLine();
            currentChunk.Append(line);
            currentTokens += lineTokens;
        }
        if (currentChunk.Length > 0)
            chunks.Add(currentChunk.ToString().Trim());

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

    private List<string> SplitLongLine(string line)
    {
        var result = new List<string>();

        // 按中英文句号分割
        var sentences = new List<string>();
        var current = new StringBuilder();

        for (int i = 0; i < line.Length; i++)
        {
            current.Append(line[i]);
            if (line[i] == '。' || line[i] == '.')
            {
                var sentence = current.ToString();
                if (!string.IsNullOrWhiteSpace(sentence))
                    sentences.Add(sentence);
                current.Clear();
            }
        }
        if (current.Length > 0)
            sentences.Add(current.ToString());

        // 如果没有句号，直接按token拆分
        if (sentences.Count == 1 && _tokenizer.Encode(line).Count() > MaxTokens)
        {
            return SplitByTokens(line);
        }

        // 合并句子，确保不超过MaxTokens
        var chunk = new StringBuilder();
        int chunkTokens = 0;

        foreach (var sentence in sentences)
        {
            var sentenceTokens = _tokenizer.Encode(sentence).Count();

            // 单句超过MaxTokens，进一步拆分
            if (sentenceTokens > MaxTokens)
            {
                if (chunk.Length > 0)
                {
                    result.Add(chunk.ToString());
                    chunk.Clear();
                    chunkTokens = 0;
                }
                result.AddRange(SplitByTokens(sentence));
                continue;
            }

            if (chunkTokens + sentenceTokens > MaxTokens && chunk.Length > 0)
            {
                result.Add(chunk.ToString());
                chunk.Clear();
                chunkTokens = 0;
            }

            chunk.Append(sentence);
            chunkTokens += sentenceTokens;
        }

        if (chunk.Length > 0)
            result.Add(chunk.ToString());

        return result;
    }

    private List<string> SplitByTokens(string text)
    {
        var result = new List<string>();
        var tokens = _tokenizer.Encode(text).ToArray();

        int start = 0;
        while (start < tokens.Length)
        {
            int end = Math.Min(start + MaxTokens, tokens.Length);
            var chunkText = _tokenizer.Decode(tokens.Skip(start).Take(end - start).ToArray());
            result.Add(chunkText);
            start = end;
        }

        return result;
    }

    public void Dispose()
    {
        _session?.Dispose();
        _tokenizer?.Dispose();
    }
}
