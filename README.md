<div align="center">

# AudioClipExtractor Pro

**视频 / 音频片段提取 + AI 音源分离的一体化 Windows 桌面工具**

从任意媒体文件中截取音频片段、逐轨预览多音轨，并可将音频分离为人声 / 伴奏或四轨乐器 stems —— 全部在本地完成，无需 ffmpeg。

![.NET 8](https://img.shields.io/badge/.NET-8-512BD4?logo=dotnet&logoColor=white)
![Platform](https://img.shields.io/badge/Platform-Windows%2010%20%2F%2011%20x64-0078D4?logo=windows&logoColor=white)
![UI](https://img.shields.io/badge/UI-WinForms%20%E8%87%AA%E7%BB%98%E7%8E%B0%E4%BB%A3%E5%8C%96-2EA44F)
![License](https://img.shields.io/badge/License-MIT-blue)

</div>

---

## 与 main 版的关系

| 分支 | 定位 |
| --- | --- |
| `main` | 稳定版：片段提取、标记、预览、导出 |
| `pro`（本分支） | 在 main 全部能力之上，新增 **AI 音源分离** 与 **多音轨管理** |

## 功能特性

### 片段提取（继承自 main）

- 支持常见视频 / 音频格式（MP4 / MKV / MOV / AVI / MP3 / AAC / FLAC / OGG 等，由系统 Media Foundation 解码）
- 播放预览、进度条拖动定位；起点 / 终点标记可直接拖动，支持 ← / → 微调（Shift / Ctrl 加速）
- 「预览区间」先听后导；未标记时默认导出整段
- 导出为 WAV（44.1 kHz / 16 bit / 单声道），带实时进度
- 亮色 / 暗色主题切换、深色标题栏；PerMonitorV2 完整适配多显示器不同 DPI，窗口可自由缩放
- 拖拽文件到窗口直接加载

### AI 音源分离（Pro 新增）

- **Demucs · 4 轨**：基于 htdemucs 的 4 轨分离（drums / bass / other / vocals，界面中依次显示为「音轨1 ~ 音轨4」），ONNX 本地推理
- **MDX-Net · 2 轨**：分离为 人声 / 伴奏（UVR-MDX-NET-Inst_HQ_3）
- 模型获取：
  - **MDX-Net**：首次使用时自动从 HuggingFace / 国内镜像下载（约 64 MB），SHA-256 校验后本地缓存，之后离线可用
  - **Demucs**：需先在本机运行一次 `python tools\export_demucs.py` 导出 ONNX 模型（脚本会自动写入模型目录），之后离线可用
- 分离结果按「媒体文件 + 音轨 + 模型」自动缓存，重复打开秒出
- 分离产生的每条 stem 作为独立音轨加入列表，可直接预览、标记、导出

### 多音轨管理（Pro 新增）

- 自动枚举媒体文件中的多条音轨（如多语言配音），逐条展示采样率 / 声道信息
- 音轨列表内每行可单独选中、单独预览
- 「文件管理」菜单一键打开模型目录 / 缓存目录 / 清理缓存

## 下载

前往 [Releases](https://github.com/doubsumi/AudioClipExtractor/releases) 下载最新 `AudioClipExtractor-*-pro.exe`，双击即用（Windows 10 / 11 x64，自包含单文件，无需安装 .NET 运行时）。

> 首次使用 MDX-Net 分离时会自动下载模型（约 64 MB，含国内镜像加速），下载完成后永久离线可用；Demucs 模型需先运行一次 `tools\export_demucs.py` 在本机生成（见「功能特性」）。模型与分离缓存可通过窗口右上角「文件管理」菜单管理。

## 使用方法

1. 点击「选择文件」，或直接把视频 / 音频文件拖进窗口
2. （可选）在音轨列表中选中目标音轨；点击「分离音轨」选择引擎（Demucs 4 轨 / MDX-Net 2 轨）获得 stems
3. 播放中点击「开始标记 / 结束标记」确定区间（也可以直接拖动进度条上的标记把手）
4. 点击「预览区间」试听效果
5. 点击「导出音频」选择保存位置

## 自行编译

需要 .NET 8 SDK：

```bat
git clone -b pro https://github.com/doubsumi/AudioClipExtractor.git
cd AudioClipExtractor
build.cmd
```

产物为自包含单文件 exe，输出在 `dist\AudioClipExtractor\`。

## 目录结构

```
├─ MainForm.cs          # 主窗口：手动 DPI 布局、播放控制、标记、导出、分离流程编排
├─ ModernControls.cs    # 自绘现代化控件：圆角卡片 / 按钮 / 进度条 / 滚动音轨列表
├─ MediaTracks.cs       # 媒体音轨枚举与解码（Media Foundation 按轨读取）
├─ Separation/          # AI 音源分离
│  ├─ DemucsSeparator   # Demucs 4 轨 ONNX 推理
│  ├─ MdxSeparator      # MDX-Net 2 轨 ONNX 推理
│  ├─ MdxStft / Fft     # STFT / iSTFT 与 FFT 实现
│  ├─ ModelStore        # 模型下载（多镜像 + SHA-256 校验）与本地缓存
│  └─ StemCache         # 分离结果缓存（按文件 + 音轨 + 模型分目录）
└─ tools/export_demucs.py  # 本机导出 Demucs ONNX 模型的辅助脚本
```

## 技术栈

- **.NET 8 + WinForms**：PerMonitorV2 高 DPI、自绘现代化控件、手动 DPI 布局
- **NAudio**：MediaFoundation 解码 / 重采样 / WAV 写出
- **Microsoft.ML.OnnxRuntime**：本地 ONNX 推理，音源分离全程离线计算
- **发布**：`PublishSingleFile` 自包含单文件（win-x64）

## License

[MIT](LICENSE)
