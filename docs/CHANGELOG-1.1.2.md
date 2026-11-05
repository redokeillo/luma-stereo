# V1.1.2

- 左右频谱色块接近圆形，保留轻微声响应轮廓变化与内部多色流动。
- psz 签名移至仪表盘底部。
- 缓冲竖条由 5 Hz 数据更新改为跟随 WPF 屏幕渲染刷新，加入 75 ms 平滑响应；实际帧率受屏幕和负载影响。托盘中取消渲染订阅，FFT 不额外提频。
- 最大化/还原时重新应用背景。最大化下增加缓存的系统静态壁纸磨砂层，避免系统亚克力退化为纯色。关闭透明时也关闭壁纸层。
- 壁纸层使用系统提供的静态图片，居中填充；不显示后方应用、动态壁纸视频，也不完全复刻多屏拼接布局。没有可读壁纸时使用系统背景。
- 独立目录、配置、自启项；首次读取 V1.1.1 配置，旧版不改动。音频引擎保持不变。

实现参考：[Microsoft DWM 背景类型](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwm_systembackdrop_type)、[系统壁纸读取接口](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-systemparametersinfow)。

- 总音量改为可点击拖动的横条与细竖线把手；默认直接修改 Windows 默认播放设备音量，关闭跟随后调节应用总音量，左右相对音量比例保持不变。
