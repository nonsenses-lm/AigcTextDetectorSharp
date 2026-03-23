#!/usr/bin/env python3
"""AIGC文本检测器 - CLI推理脚本"""

import argparse
import torch
from pathlib import Path
from transformers import BertForSequenceClassification, BertTokenizer
from transformers import RobertaForSequenceClassification, RobertaTokenizer


def load_model(model_dir: str):
    """从本地目录加载模型和tokenizer"""
    path = Path(model_dir)
    config_file = path / "config.json"
    
    # 根据config判断模型类型
    import json
    with open(config_file) as f:
        config = json.load(f)
    
    if config.get("model_type") == "roberta":
        tokenizer = RobertaTokenizer.from_pretrained(str(path))
        model = RobertaForSequenceClassification.from_pretrained(str(path))
    else:
        tokenizer = BertTokenizer.from_pretrained(str(path))
        model = BertForSequenceClassification.from_pretrained(str(path))
    
    model.eval()
    return tokenizer, model


def predict(text: str, tokenizer, model) -> tuple[str, float]:
    """预测文本是人类撰写还是AI生成"""
    with torch.no_grad():
        inputs = tokenizer(text, return_tensors='pt', max_length=512, truncation=True)
        outputs = model(**inputs)
        probs = outputs.logits[0].softmax(0)
        label_id = probs.argmax().item()
        score = probs.max().item()
    
    return ("Human" if label_id == 0 else "AI"), score


def main():
    parser = argparse.ArgumentParser(description="AIGC文本检测器")
    parser.add_argument("text", nargs="?", help="待检测文本")
    parser.add_argument("-f", "--file", help="从文件读取文本")
    parser.add_argument("-m", "--model", default="model_zhv3", help="模型目录 (默认: model_zhv3)")
    args = parser.parse_args()

    # 加载模型
    print(f"加载模型: {args.model}", flush=True)
    tokenizer, model = load_model(args.model)

    # 获取文本
    if args.file:
        text = Path(args.file).read_text().strip()
    elif args.text:
        text = args.text
    else:
        # 交互模式
        print("输入文本检测，quit 退出\n")
        while True:
            try:
                text = input("> ").strip()
            except (EOFError, KeyboardInterrupt):
                break
            if not text or text.lower() == "quit":
                break
            label, score = predict(text, tokenizer, model)
            print(f"{label} {score:.4f}")
        return

    # 单次检测
    label, score = predict(text, tokenizer, model)
    print(f"{label} {score:.4f}")


if __name__ == "__main__":
    main()
