"""把 Demucs v4 (htdemucs) 导出为 ONNX（波形 -> 4 轨波形）。

思路：
- ONNX 不支持复数类型，torch.stft/istft 也无法导出，因此把 STFT / iSTFT 改成
  「实部/虚部」实现，并用两级 64×64 蝶形矩阵乘法代替 4096 点 FFT（基矩阵很小，速度快）。
- 模型 forward 中的 cac 分支等价于「复数谱拆成 re/im 两个通道」，因此整图输入输出均是实数。
- 输入固定为 1×2×343980（44.1kHz、7.8s 训练段长），与 demucs apply_model 的分块一致。

用法：python tools/export_demucs.py [输出路径]
"""

import math
import os
import sys
import time

os.environ.setdefault("HF_HUB_OFFLINE", "1")
os.environ.setdefault("HF_HUB_DISABLE_TELEMETRY", "1")

import numpy as np
import torch
import torch.nn as nn
import torch.nn.functional as F

NFFT = 4096
HOP = 1024
P = 64                 # 两级分解尺寸 4096 = 64 × 64
SAMPLE_RATE = 44100
SEGMENT = 7.8
SEG_LEN = int(SEGMENT * SAMPLE_RATE)   # 343980


def _plan(dtype, device):
    k = torch.arange(P, dtype=dtype, device=device)
    ang = 2 * math.pi * torch.outer(k, k) / P
    c64, s64 = torch.cos(ang), torch.sin(ang)
    ang2 = 2 * math.pi * torch.outer(k, k) / NFFT
    tc, ts = torch.cos(ang2), torch.sin(ang2)
    win = 0.5 - 0.5 * torch.cos(2 * math.pi * torch.arange(NFFT, dtype=dtype, device=device) / NFFT)
    return c64, s64, tc, ts, win


class Stft(nn.Module):
    """与 demucs HTDemucs._spec / _ispec 数值等价的实数版 STFT/iSTFT。"""

    def __init__(self, dtype=torch.float32):
        super().__init__()
        c64, s64, tc, ts, win = _plan(dtype, torch.device("cpu"))
        self.register_buffer("c64", c64)
        self.register_buffer("s64", s64)
        self.register_buffer("tc", tc)
        self.register_buffer("ts", ts)
        self.register_buffer("win", win)
        self.register_buffer("c32", c64[:32].contiguous())
        self.register_buffer("s32", s64[:32].contiguous())
        w2 = (win * win)
        # 重叠相加的窗平方包络（常数）
        tt = SAMP_LEN_FRAMES + 4
        env = torch.zeros((tt + 3) * HOP, dtype=dtype)
        for i in range(tt):
            env[i * HOP:i * HOP + NFFT] += w2
        self.register_buffer("env", env)

    def analyze(self, x):
        """x: (B, C, N) -> re, im 各 (B, C, 2048, T)"""
        B, C, N = x.shape
        le = (N + HOP - 1) // HOP
        pad = HOP // 2 * 3
        y = F.pad(x, (pad, pad + le * HOP - N), mode="reflect")
        frames = y.unfold(-1, NFFT, HOP)                      # (B, C, T, 4096)
        T = frames.shape[2]
        fr = frames.reshape(-1, NFFT)
        X = (fr * self.win).reshape(-1, P, P)                 # [n2, n1]
        are = torch.matmul(self.c64, X)
        aim = -torch.matmul(self.s64, X)
        atre, atim = are.transpose(-1, -2), aim.transpose(-1, -2)
        bre = atre * self.tc + atim * self.ts
        bim = atim * self.tc - atre * self.ts
        ore = torch.matmul(self.c32, bre) + torch.matmul(self.s32, bim)
        oim = torch.matmul(self.c32, bim) - torch.matmul(self.s32, bre)
        scale = 1.0 / math.sqrt(NFFT)
        re = ore.reshape(B, C, T, 32, 64).permute(0, 1, 3, 4, 2).reshape(B, C, 2048, T)
        im = oim.reshape(B, C, T, 32, 64).permute(0, 1, 3, 4, 2).reshape(B, C, 2048, T)
        return re * scale, im * scale

    def synthesize(self, re, im):
        """re, im: (M, 2048, T) -> (M, N)，与 demucs._ispec(length=SEG_LEN) 等价"""
        M, Fq, T = re.shape
        re = F.pad(re, (2, 2))                    # 帧方向两侧补零帧（与 _ispec 的 F.pad(z, (2,2)) 一致）
        im = F.pad(im, (2, 2))
        TT = re.shape[-1]
        # 先转成 [帧, 频点] 布局，再在频点维做共轭镜像补齐到 4096 点
        re = re.permute(0, 2, 1).contiguous()      # (M, TT, 2048)
        im = im.permute(0, 2, 1).contiguous()
        zero = torch.zeros_like(re[..., :1])
        full_re = torch.cat([re, zero, re[..., 1:].flip(-1)], dim=-1)      # (M, TT, 4096)
        full_im = torch.cat([im, zero, -im[..., 1:].flip(-1)], dim=-1)
        zr = full_re.reshape(-1, P, P)
        zi = full_im.reshape(-1, P, P)
        ere = torch.matmul(self.c64, zr) - torch.matmul(self.s64, zi)
        eim = torch.matmul(self.c64, zi) + torch.matmul(self.s64, zr)
        dre = ere * self.tc - eim * self.ts
        dim = ere * self.ts + eim * self.tc
        xr = torch.matmul(self.c64, dre.transpose(-1, -2)) - torch.matmul(self.s64, dim.transpose(-1, -2))
        xr = xr.reshape(-1, NFFT) * (math.sqrt(NFFT) / NFFT) * self.win   # (M*TT, 4096) 帧
        # 重叠相加：4 个 1024 块错位求和
        f4 = xr.reshape(M, TT, 4, HOP)
        out = None
        for q in range(4):
            s = F.pad(f4[:, :, q, :], (0, 0, q, 3 - q))
            out = s if out is None else out + s
        out = out.reshape(M, -1)
        out = out / self.env[None, :out.shape[1]]
        total = out.shape[-1] - 2 * (NFFT // 2)
        out = out[:, NFFT // 2: NFFT // 2 + total]
        pad = HOP // 2 * 3
        return out[:, pad:pad + SEG_LEN]


SAMP_LEN_FRAMES = math.ceil(SEG_LEN / HOP)   # 336


class DemucsOnnx(nn.Module):
    """HTDemucs.forward 的实数等价改写（跳过 complex 相关操作）。"""

    def __init__(self, m, stft):
        super().__init__()
        self.m = m
        self.stft = stft

    def forward(self, mix):
        m = self.m
        length = mix.shape[-1]
        zre, zim = self.stft.analyze(mix)                      # (B,2,2048,T)
        x = torch.stack((zre, zim), dim=2).reshape(mix.shape[0], -1, zre.shape[2], zre.shape[3])
        B, C, Fq, T = x.shape

        n = float(C * Fq * T)
        mean = x.sum(dim=(1, 2, 3), keepdim=True) / n
        std = torch.sqrt(((x - mean) ** 2).sum(dim=(1, 2, 3), keepdim=True) / (n - 1))
        x = (x - mean) / (1e-5 + std)

        xt = mix
        nt = float(mix.shape[1] * length)
        meant = xt.sum(dim=(1, 2), keepdim=True) / nt
        stdt = torch.sqrt(((xt - meant) ** 2).sum(dim=(1, 2), keepdim=True) / (nt - 1))
        xt = (xt - meant) / (1e-5 + stdt)

        saved, saved_t, lengths, lengths_t = [], [], [], []
        for idx, encode in enumerate(m.encoder):
            lengths.append(x.shape[-1])
            inject = None
            if idx < len(m.tencoder):
                lengths_t.append(xt.shape[-1])
                tenc = m.tencoder[idx]
                xt = tenc(xt)
                if not tenc.empty:
                    saved_t.append(xt)
                else:
                    inject = xt
            x = encode(x, inject)
            if idx == 0 and m.freq_emb is not None:
                frs = torch.arange(x.shape[-2], device=x.device)
                emb = m.freq_emb(frs).t()[None, :, :, None].expand_as(x)
                x = x + m.freq_emb_scale * emb
            saved.append(x)

        if m.crosstransformer:
            if m.bottom_channels:
                b, c0, f, t = x.shape
                x = x.reshape(b, c0, f * t)
                x = m.channel_upsampler(x)
                x = x.reshape(b, -1, f, t)
                xt = m.channel_upsampler_t(xt)
            x, xt = m.crosstransformer(x, xt)
            if m.bottom_channels:
                b, c1, f, t = x.shape
                x = x.reshape(b, c1, f * t)
                x = m.channel_downsampler(x)
                x = x.reshape(b, -1, f, t)
                xt = m.channel_downsampler_t(xt)

        for idx, decode in enumerate(m.decoder):
            skip = saved.pop(-1)
            x, pre = decode(x, skip, lengths.pop(-1))
            offset = m.depth - len(m.tdecoder)
            if idx >= offset:
                tdec = m.tdecoder[idx - offset]
                length_t = lengths_t.pop(-1)
                if tdec.empty:
                    pre = pre[:, :, 0]
                    xt, _ = tdec(pre, None, length_t)
                else:
                    skip = saved_t.pop(-1)
                    xt, _ = tdec(xt, skip, length_t)

        S = len(m.sources)
        x = x.view(B, S, -1, Fq, T)
        x = x * std[:, None] + mean[:, None]
        # 每轨 2 声道 × (re, im)
        x = x.view(B, S, m.audio_channels, 2, Fq, T)
        re, im = x[:, :, :, 0], x[:, :, :, 1]                 # (B,S,C,F,T)
        xf = self.stft.synthesize(re.reshape(-1, Fq, T), im.reshape(-1, Fq, T))
        xf = xf.reshape(B, S, m.audio_channels, length)
        xt = xt.view(B, S, m.audio_channels, length)
        xt = xt * stdt[:, None] + meant[:, None]
        return (xf + xt).reshape(B, S * m.audio_channels, length)


def main():
    # 默认输出到应用的本地模型目录，C# 端直接从 %LOCALAPPDATA%\AudioClipExtractor\models 加载
    default_dir = os.path.join(
        os.environ.get("LOCALAPPDATA") or os.path.expanduser("~"),
        "AudioClipExtractor", "models")
    out_path = sys.argv[1] if len(sys.argv) > 1 else os.path.join(default_dir, "htdemucs.onnx")

    from demucs.pretrained import get_model
    model = get_model("htdemucs")
    m = model.models[0].eval()

    stft = Stft().eval()
    wrap = DemucsOnnx(m, stft).eval()

    # 1) STFT / iSTFT 数值校验
    torch.manual_seed(0)
    x = torch.randn(1, 2, SEG_LEN)
    with torch.no_grad():
        ref = m(x)
        z = m._spec(x)
        re, im = stft.analyze(x)
        torch.testing.assert_close(re, z.real, atol=1e-4, rtol=1e-4)
        torch.testing.assert_close(im, z.imag, atol=1e-4, rtol=1e-4)
        print("STFT 对比通过")

        out = wrap(x)
        # 端到端对比：走完整 PyTorch 前向（含 complex 分支）
        err = (out.reshape(ref.shape) - ref).abs()
        print("整图最大误差", err.max().item(), "参考 rms", ref.abs().mean().item())

        t0 = time.time()
        wrap(x)
        print("PyTorch CPU 单块耗时 %.2fs" % (time.time() - t0))

    os.makedirs(os.path.dirname(out_path), exist_ok=True)
    t0 = time.time()
    torch.onnx.export(wrap, (x,), out_path, dynamo=True, opset_version=18,
                      input_names=["mix"], output_names=["stems"])
    print("导出完成 %.1fs  大小 %.1fMB" % (time.time() - t0, os.path.getsize(out_path) / 1e6))

    # 2) onnxruntime 与 PyTorch 对比
    import onnxruntime as ort
    so = ort.SessionOptions()
    so.intra_op_num_threads = max(1, os.cpu_count() or 1)
    sess = ort.InferenceSession(out_path, so, providers=["CPUExecutionProvider"])
    t0 = time.time()
    y = sess.run(None, {"mix": x.numpy()})[0]
    print("ORT CPU 单块耗时 %.2fs" % (time.time() - t0))
    out_np = out.detach().numpy()
    print("ORT 与 PyTorch 最大误差", float(np.abs(y - out_np).max()))


if __name__ == "__main__":
    main()
