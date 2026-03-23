#!/usr/bin/env python3
"""
AIGC Detector ONNX转换脚本
将PyTorch模型转换为ONNX格式
"""

import argparse
import torch
import os
import json


def get_model_type(model_dir):
    config_path = os.path.join(model_dir, "config.json")
    if os.path.exists(config_path):
        with open(config_path) as f:
            config = json.load(f)
        model_type = config.get("model_type", "bert")
        return model_type
    return "bert"


def convert_to_onnx(model_dir, output_path):
    model_type = get_model_type(model_dir)
    
    print("=" * 50)
    print(f"AIGC Detector ONNX转换工具 ({model_type.upper()})")
    print("=" * 50)
    
    if model_type == "roberta":
        from transformers import RobertaForSequenceClassification, RobertaTokenizer
        tokenizer = RobertaTokenizer.from_pretrained(model_dir)
        model = RobertaForSequenceClassification.from_pretrained(model_dir)
        input_names = ["input_ids", "attention_mask"]
        sample_text = "hello world"
    else:
        from transformers import BertForSequenceClassification, BertTokenizer
        tokenizer = BertTokenizer.from_pretrained(model_dir)
        model = BertForSequenceClassification.from_pretrained(model_dir)
        input_names = ["input_ids", "attention_mask", "token_type_ids"]
        sample_text = "测试文本"
    
    model.eval()
    
    print(f"\n[1/4] 加载模型: {model_dir}")
    print(f"      模型类型: {model_type.upper()}")
    
    print("\n[2/4] 创建示例输入...")
    sample_text = "hello world" if model_type == "roberta" else "测试文本"
    inputs = tokenizer(sample_text, return_tensors='pt')
    print(f"      示例文本: {sample_text}")
    print(f"      Input IDs shape: {inputs['input_ids'].shape}")
    
    if model_type == "roberta":
        args_tuple = (inputs['input_ids'], inputs['attention_mask'])
    else:
        args_tuple = (inputs['input_ids'], inputs['attention_mask'], inputs['token_type_ids'])
    
    print("\n[3/4] 验证PyTorch推理...")
    with torch.no_grad():
        pytorch_output = model(**inputs)
        pytorch_logits = pytorch_output.logits[0].numpy()
    
    print(f"      PyTorch logits: {pytorch_logits.tolist()}")
    
    print("\n[4/4] 导出ONNX模型...")
    os.makedirs(os.path.dirname(output_path) if os.path.dirname(output_path) else '.', exist_ok=True)
    
    torch.onnx.export(
        model,
        args_tuple,
        output_path,
        input_names=input_names,
        output_names=['logits'],
        do_constant_folding=True
    )
    
    if os.path.exists(output_path + ".data"):
        print("      合并外部数据...")
        import onnx
        onnx_model = onnx.load(output_path, load_external_data=True)
        onnx.save(onnx_model, output_path)
        os.remove(output_path + ".data")
    
    print(f"      ONNX模型已保存: {output_path}")
    
    print("\n[验证] ONNX模型...")
    try:
        import onnx
        import numpy as np
        import onnxruntime as ort
        
        onnx.checker.check_model(onnx.load(output_path))
        print("      ONNX模型结构验证通过!")
        
        session = ort.InferenceSession(output_path, providers=['CPUExecutionProvider'])
        
        onnx_inputs = {
            'input_ids': inputs['input_ids'].numpy(),
            'attention_mask': inputs['attention_mask'].numpy()
        }
        if model_type == "bert":
            onnx_inputs['token_type_ids'] = inputs['token_type_ids'].numpy()
        
        onnx_logits = session.run(None, onnx_inputs)[0][0]
        
        diff = np.abs(pytorch_logits - onnx_logits).max()
        print(f"      PyTorch logits: {pytorch_logits.tolist()}")
        print(f"      ONNX logits: {onnx_logits.tolist()}")
        print(f"      最大差异: {diff:.6f}")
        print(f"      {'✓ 转换成功!' if diff < 1e-4 else '⚠ 差异较大，请检查'}")
        
    except Exception as e:
        print(f"      验证跳过: {e}")
    
    print("\n" + "=" * 50)
    print(f"文件大小: {os.path.getsize(output_path) / (1024*1024):.2f} MB")
    print("=" * 50)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="AIGC Detector ONNX转换工具")
    parser.add_argument("-m", "--model", default=".", help="模型目录 (默认: 当前目录)")
    parser.add_argument("-o", "--output", help="输出ONNX文件路径")
    args = parser.parse_args()
    
    model_dir = args.model
    model_type = get_model_type(model_dir)
    
    if args.output:
        output_path = args.output
    else:
        if model_type == "roberta":
            output_path = os.path.join(model_dir, "AIGC_detector_env3.onnx")
        else:
            output_path = os.path.join(model_dir, "AIGC_detector_zhv3.onnx")
    
    convert_to_onnx(model_dir, output_path)
