# AudioClipExtractor（视频音频片段提取器）

一个免安装、单文件的 Windows 小工具：从视频中截取任意区间的音频并导出为 WAV。
基于 NAudio + Windows Media Foundation 解码，**无需 ffmpeg**，目标机器无需安装 .NET 运行时。

## 功能特性

- 支持常见视频/音频格式（MP4 / MKV / MOV / AVI / MP3 / AAC 等，由系统 Media Foundation 解码）
- 播放预览，进度条拖动定位
- 起点 / 终点标记，标记可直接拖动微调位置
- 「预览区间」先听后导
- 导出为 WAV（44.1 kHz / 16 bit / 单声道），带实时进度
- 亮色 / 暗色主题切换，深色标题栏
- 完整适配多显示器不同 DPI（PerMonitorV2），窗口可自由缩放、可最大化
- 支持拖拽文件到窗口

## 下载

前往 [Releases](https://github.com/doubsumi/AudioClipExtractor/releases) 下载最新 `AudioClipExtractor.exe`，双击即用（Windows 10 / 11 x64）。

## 使用方法

1. 点击「选择视频」，或直接把视频文件拖进窗口
2. 播放中点击「开始标记 / 结束标记」确定区间（也可以直接拖动进度条上的标记）
3. 点击「预览区间」试听效果
4. 点击「导出音频」选择保存位置（未标记时默认导出整段音频）

## 自行编译

需要 .NET 8 SDK：

```bat
git clone https://github.com/doubsumi/AudioClipExtractor.git
cd AudioClipExtractor
build.cmd
```

产物为自包含单文件 exe，输出在 `dist\AudioClipExtractor\`（该目录只会有一个 exe，可直接分发）。

## 技术栈

- .NET 8 + WinForms：PerMonitorV2 高 DPI、自绘现代化控件（圆角卡片 / 按钮 / 进度条）、手动 DPI 布局
- NAudio：MediaFoundationReader 解码、MediaFoundationResampler 重采样、WaveFileWriter 写出
- 发布：`PublishSingleFile` 自包含单文件（win-x64），图标由代码绘制成多尺寸 ico 内嵌

## License

[MIT](LICENSE)
