namespace AigcDetectorSharp.Core.Models;

public record DetectionResult(
    string Label,
    float Probability,
    List<ChunkResult> Chunks
);

public record ChunkResult(
    int Index,
    string Text,
    string Label,
    float Probability
);
