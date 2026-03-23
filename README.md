# Aigc Text Detector Sharp 
AIGC Text Detector Sharp 是一款检测文本是由人类撰写还是 AI 生成的工具，支持中文和英文。

本项目基于是 ICLR'24 Spotlight 论文 "Multiscale Positive-Unlabeled Detection of AI-Generated Texts" 的 C# + ONNX 非官方实现。

## 功能特性

- 支持中文和英文文本检测
- 本地运行，数据不外传
- 支持多种输入格式：纯文本、Markdown、DOCX、PDF
- 提供 CLI 和 Web API 两种使用方式
- 超过 512 tokens 的文本自动分块处理

## 项目结构

```
AIGC_detector_zhv3/
├── AigcDetectorSharp/          # CLI 工具
├── AigcDetectorSharp.Core/     # 核心库
├── AigcDetectorSharp.UI/       # 桌面/服务器 UI
├── model_zhv3/                 # 中文模型 (v3)
├── model_env3/                 # 英文模型 (v3)
├── app.py                      # Python 推理脚本
├── convert_to_onnx.py          # ONNX 模型转换
```

## 安装

### 依赖要求

- .NET 8.0+
- Python 3.8+ (如使用 Python 推理和转换)

### Python 依赖

```bash
pip install torch transformers onnxruntime
```

## 使用方法

### C# CLI 使用

```bash
# 检测中文文本
./publish/AigcDetectorSharp "待检测的中文文本"

# 检测英文文本
./publish/AigcDetectorSharp -m en "English text to detect"

# 从文件检测
./publish/AigcDetectorSharp -f /path/to/file.txt
```

### 服务器模式

```bash
# 启动 Web 服务器
./publish/AigcDetectorSharp.UI --server --port=5000
```

### API 调用

```bash
curl -X POST http://localhost:5000/api/detect \
  -H "Content-Type: application/json" \
  -d '{"text":"your text","model":"zh"}'
```

## 命令行选项

| 选项 | 说明 |
|------|------|
| `-m zh` | 中文模型 (默认) |
| `-m en` | 英文模型 |
| `-f <file>` | 从文件读取 (.txt, .md, .docx, .pdf) |
| `-p <dir>` | 自定义模型目录 |
| `--echo` | 输出原始文本 |
| `--server` | 启动 Web 服务器 |
| `--port=<n>` | 服务器端口 (默认: 5000) |

## 输出格式

```
<Label> <Probability>
```

- `Label`: `Human` 或 `AI`
- `Probability`: 置信度 (0.0–1.0)

## 模型版本

| 版本 | 中文模型 | 英文模型 | 说明 |
|------|----------|----------|------|
| v3 | AIGC_detector_zhv3 | AIGC_detector_env3 | 针对最新 LLMs |
| v2 | AIGC_detector_zhv2 | AIGC_detector_env2 | 增强版检测器 |
| v1 | AIGC_detector_zh | AIGC_detector_env | 基础版 |

## 许可证

MIT License

## 参考

- 论文: [Multiscale Positive-Unlabeled Detection of AI-Generated Texts](https://arxiv.org/abs/2305.18149)
- HuggingFace: [AIGC Text Detector](https://huggingface.co/spaces/yuchuantian/AIGC_text_detector)
- ModelScope: [AIGC Text Detector](https://modelscope.cn/studios/YuchuanTian/AIGC_text_detector)
- 原项目: [Github](https://github.com/YuchuanTian/AIGC_text_detector)
