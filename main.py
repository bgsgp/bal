import platform
import random
import time
import json
import os
import sys
import subprocess
from pathlib import Path
from PySide6.QtWidgets import (
    QApplication, QMainWindow, QWidget, QVBoxLayout, QHBoxLayout,
    QPushButton, QLabel, QFrame, QListWidget,
    QListWidgetItem, QLineEdit, QMessageBox, QDialog,
    QDialogButtonBox, QFormLayout, QSpinBox, QRadioButton,
    QCheckBox, QGroupBox, QScrollArea, QGridLayout, QSlider,
    QFileDialog
)
from PySide6.QtMultimediaWidgets import QVideoWidget
from PySide6.QtGui import (
    QFont, QPalette, QColor, QLinearGradient, QBrush,
    QPainter, QPen, QIcon, QPixmap, QPainterPath,
    QMouseEvent, QKeyEvent
)
from PySide6.QtCore import (
    Qt, QThread, Signal, QPropertyAnimation, QEasingCurve,
    QPoint, QRect, QTimer, QEvent, QUrl, QSize,
    QElapsedTimer
)
from PySide6.QtMultimedia import (
    QMediaPlayer, QAudioOutput
)
from PySide6.QtGui import QDesktopServices

# ===================== 获取程序基础路径 =====================
def get_base_path():
    """获取程序基础路径：开发时为脚本所在目录，打包后为可执行文件所在目录"""
    if getattr(sys, 'frozen', False):
        # 打包后，sys.executable 是可执行文件的完整路径
        return Path(sys.executable).parent
    else:
        # 开发时，返回脚本所在目录
        return Path(__file__).parent

BASE_PATH = get_base_path()

# ===================== 彻底禁用硬件加速 =====================
os.environ["QT_FFMPEG_HWACCEL"] = "none"
os.environ["FFMPEG_DISABLE_HWACCELS"] = "1"
os.environ["QT_OPENGL"] = "software"
os.environ["QT_MEDIA_BACKEND"] = "ffmpeg"
os.environ["QT_DISABLE_HW_TEXTURES_CONVERSION"] = "1"
os.environ["QT_FFMPEG_DECODING_HW_DEVICE_TYPES"] = ""
os.environ["SDL_VIDEODRIVER"] = "dummy"
os.environ["FFMPEG_HWDEVICE"] = ""

if platform.system() == "Windows":
    os.environ["DXVA2_DISABLE"] = "1"
    os.environ["D3D11VA_DISABLE"] = "1"


class ColorScheme:
    """颜色方案管理类"""
    LIGHT = {
        "primary": QColor(135, 206, 235),
        "secondary": QColor(255, 182, 193),
        "accent": QColor(152, 251, 152),
        "background": QColor(240, 248, 255),
        "text": QColor(70, 130, 180),
        "white": QColor(255, 255, 255),
        "shadow": QColor(173, 216, 230),
        "reward": QColor(34, 139, 34),
        "punishment": QColor(220, 20, 60)
    }

    DARK = {
        "primary": QColor(70, 130, 180),
        "secondary": QColor(255, 105, 180),
        "accent": QColor(107, 142, 35),
        "background": QColor(25, 25, 40),
        "text": QColor(240, 248, 255),
        "white": QColor(45, 45, 65),
        "shadow": QColor(50, 50, 70),
        "reward": QColor(144, 238, 144),
        "punishment": QColor(255, 106, 106)
    }


class SignatureWidget(QWidget):
    """签名绘制组件（透明层）"""
    draw_stopped = Signal()
    skip_clicked = Signal()

    def __init__(self, parent=None):
        super().__init__(parent)
        self.setMouseTracking(True)
        self.drawing = False
        self.last_point = QPoint()
        self.pen_color = QColor("#90FEC8")
        self.pen_width = 3
        self.path = QPainterPath()

        self.press_pos = QPoint()
        self.press_time = QElapsedTimer()

        self.stop_timer = QTimer()
        self.stop_timer.setInterval(3000)
        self.stop_timer.timeout.connect(self.on_draw_stopped)

        self.setAttribute(Qt.WA_TranslucentBackground)
        self.setStyleSheet("background: transparent;")

    def paintEvent(self, event):
        painter = QPainter(self)
        painter.setRenderHint(QPainter.Antialiasing)
        painter.setPen(QPen(self.pen_color, self.pen_width,
                            Qt.SolidLine, Qt.RoundCap, Qt.RoundJoin))
        painter.drawPath(self.path)

    def mousePressEvent(self, event):
        if event.button() == Qt.LeftButton:
            self.press_pos = event.position().toPoint()
            self.press_time.start()
            self.drawing = True
            self.path.moveTo(event.position().toPoint())
            self.last_point = event.position().toPoint()
            self.stop_timer.stop()
            self.update()

    def mouseMoveEvent(self, event):
        if self.drawing and (event.buttons() & Qt.LeftButton):
            self.path.lineTo(event.position().toPoint())
            self.last_point = event.position().toPoint()
            self.update()
            self.stop_timer.start()

    def mouseReleaseEvent(self, event):
        if event.button() == Qt.LeftButton:
            elapsed = self.press_time.elapsed()
            current_pos = event.position().toPoint()
            dist = (current_pos - self.press_pos).manhattanLength()

            if dist < 5 and elapsed < 300:
                self.skip_clicked.emit()
                self.path = QPainterPath()
                self.update()
            else:
                self.drawing = False
                self.stop_timer.start()

    def on_draw_stopped(self):
        self.stop_timer.stop()
        self.draw_stopped.emit()

    def clear_signature(self):
        self.path = QPainterPath()
        self.update()


class VideoPlayerWindow(QMainWindow):
    """全屏视频播放窗口（不再包含BGM，由主窗口统一管理）"""
    video_finished = Signal()

    def __init__(self, parent=None, star_level=1):
        super().__init__(parent)
        self.setWindowFlags(Qt.Window | Qt.FramelessWindowHint)
        self.showFullScreen()

        self.star_level = star_level
        self.video_type = "Special" if star_level == 3 else "Simple"
        # 视频路径基于基础路径构建
        self.resource_path = BASE_PATH / "resources" / self.video_type.lower()
        self.current_phase = "start"
        self.skip_video_played = False

        # 视频播放器
        self.audio_output = QAudioOutput()
        self.player = QMediaPlayer()
        self.player.setAudioOutput(self.audio_output)
        if hasattr(self.player, 'setBufferMode'):
            try:
                buffer_mode = QMediaPlayer.BufferMode.Buffered
                self.player.setBufferMode(buffer_mode)
            except AttributeError:
                try:
                    self.player.setBufferMode(1)
                except Exception:
                    pass

        self.video_widget = QVideoWidget()
        self.video_widget.setStyleSheet("background-color: black;")
        self.player.setVideoOutput(self.video_widget)

        # 中央部件与覆盖层
        central_widget = QWidget()
        self.setCentralWidget(central_widget)
        main_layout = QVBoxLayout(central_widget)
        main_layout.setContentsMargins(0, 0, 0, 0)

        self.overlay_widget = QWidget()
        self.overlay_layout = QVBoxLayout(self.overlay_widget)
        self.overlay_layout.setContentsMargins(0, 0, 0, 0)
        self.overlay_layout.addWidget(self.video_widget)

        self.signature_widget = SignatureWidget(self.overlay_widget)
        self.signature_widget.setVisible(False)
        self.signature_widget.draw_stopped.connect(self.auto_play_open_after_signature)
        self.signature_widget.skip_clicked.connect(self.play_skip_video)
        self.overlay_layout.addWidget(self.signature_widget)
        self.signature_widget.raise_()

        self.skip_button = QPushButton("跳过 >>", self.overlay_widget)
        self.skip_button.setStyleSheet("""
            QPushButton {
                background: rgba(0,0,0,0.6);
                color: white;
                border: none;
                border-radius: 8px;
                padding: 8px 15px;
                font-size: 16px;
            }
            QPushButton:hover {
                background: rgba(0,0,0,0.8);
            }
        """)
        self.skip_button.setFixedSize(80, 40)
        self.skip_button.clicked.connect(self.play_skip_video)
        self.skip_button.setVisible(False)

        main_layout.addWidget(self.overlay_widget)

        self.player.mediaStatusChanged.connect(self.on_media_status_changed)
        self.player.errorOccurred.connect(self.on_media_error)
        self.player.sourceChanged.connect(self.on_source_changed)

        self.check_video_files()
        self.play_start_video()

    def check_video_files(self):
        required_videos = ["Start.mp4", "Skip.mp4", "Open.mp4"]
        missing = []
        for vid in required_videos:
            if not (self.resource_path / vid).exists():
                missing.append(vid)
        if missing:
            print(f"【警告】{self.video_type} 目录缺失视频文件：{', '.join(missing)}")

    def resizeEvent(self, event):
        super().resizeEvent(event)
        if hasattr(self, 'skip_button') and self.skip_button.isVisible():
            self.skip_button.move(self.width() - self.skip_button.width() - 10, 10)

    def play_start_video(self):
        self.current_phase = "start"
        self.skip_video_played = False
        video_path = self.resource_path / "Start.mp4"

        self.player.stop()
        self.player.setSource(QUrl())

        if video_path.exists():
            video_url = QUrl.fromLocalFile(str(video_path.resolve()))
            self.player.setSource(video_url)
            QTimer.singleShot(200, self.player.play)
            self.skip_button.setVisible(True)
            QTimer.singleShot(100, self.update_skip_button_position)
        else:
            print("Start.mp4 不存在，直接显示签名模板")
            self.show_signature_template()

    def play_skip_video(self):
        if self.skip_video_played:
            return
        self.skip_video_played = True

        self.current_phase = "skip"
        self.skip_button.setVisible(False)
        self.signature_widget.setVisible(False)
        self.signature_widget.stop_timer.stop()

        video_path = self.resource_path / "Skip.mp4"
        self.player.stop()
        self.player.setSource(QUrl())

        if video_path.exists():
            print("开始播放 Skip.mp4")
            video_url = QUrl.fromLocalFile(str(video_path.resolve()))
            self.player.setSource(video_url)
            QTimer.singleShot(200, self.player.play)
        else:
            print("Skip.mp4 不存在，模拟延迟1.5秒后播放 Open")
            QTimer.singleShot(1500, self.play_open_video)

    def play_open_video(self):
        self.current_phase = "open"
        self.skip_button.setVisible(False)
        self.signature_widget.setVisible(False)
        self.signature_widget.stop_timer.stop()

        video_path = self.resource_path / "Open.mp4"
        self.player.stop()
        self.player.setSource(QUrl())

        if video_path.exists():
            print("开始播放 Open.mp4")
            video_url = QUrl.fromLocalFile(str(video_path.resolve()))
            self.player.setSource(video_url)
            QTimer.singleShot(200, self.player.play)
        else:
            print("Open.mp4 不存在，结束播放流程")
            self.on_video_complete()

    def auto_play_open_after_signature(self):
        self.skip_video_played = True
        self.play_open_video()

    def show_signature_template(self):
        self.signature_widget.clear_signature()
        self.signature_widget.setVisible(True)
        self.signature_widget.stop_timer.start()

    def on_media_status_changed(self, status):
        if status == QMediaPlayer.MediaStatus.EndOfMedia:
            print(f"【播放完成】当前阶段：{self.current_phase}")
            if self.current_phase == "start":
                self.player.pause()
                if self.player.duration() > 0:
                    self.player.setPosition(self.player.duration() - 1)
                self.show_signature_template()
                self.skip_button.setVisible(False)
            elif self.current_phase == "skip":
                QTimer.singleShot(500, self.play_open_video)
            elif self.current_phase == "open":
                self.on_video_complete()
        elif status == QMediaPlayer.MediaStatus.LoadedMedia:
            print(f"【视频加载完成】当前阶段：{self.current_phase}")

    def on_media_error(self, error, error_string):
        print(f"【播放错误】阶段：{self.current_phase} | 错误：{error_string}")
        if self.current_phase == "start":
            self.show_signature_template()
            self.skip_button.setVisible(False)
        elif self.current_phase == "skip":
            QTimer.singleShot(1500, self.play_open_video)
        else:
            self.on_video_complete()

    def on_source_changed(self):
        print(f"【源切换完成】当前阶段：{self.current_phase}")

    def update_skip_button_position(self):
        if self.skip_button.isVisible():
            self.skip_button.move(self.width() - self.skip_button.width() - 10, 10)

    def on_video_complete(self):
        self.player.stop()
        self.video_finished.emit()
        self.close()

    def keyPressEvent(self, event):
        if event.key() == Qt.Key_Escape:
            self.on_video_complete()


class SilentLotteryThread(QThread):
    """后台静默抽选线程"""
    lottery_result = Signal(str, int)
    error = Signal(str)

    def __init__(self, levels):
        super().__init__()
        self.levels = levels

    def run(self):
        try:
            all_items = []
            for level in self.levels:
                all_items.extend(level.get('items', []))

            if not all_items:
                self.error.emit("没有可抽选的项目！")
                return

            rand_num = random.uniform(0, 100)
            cumulative_prob = 0
            selected_level = None

            for level in self.levels:
                cumulative_prob += level.get("probability", 0)
                if rand_num <= cumulative_prob:
                    selected_level = level
                    break

            if selected_level:
                result_item = random.choice(selected_level['items'])
                self.lottery_result.emit(result_item, selected_level['star'])
            else:
                result_item = random.choice(all_items)
                self.lottery_result.emit(result_item, 1)

        except Exception as e:
            self.error.emit(f"抽选出错：{str(e)}")


class LotteryThread(QThread):
    """普通模式抽选线程（滚动效果）"""
    update_result = Signal(str)
    lottery_finished = Signal(str, int)
    error = Signal(str)

    def __init__(self, levels):
        super().__init__()
        self.levels = levels
        self._is_running = True

    def run(self):
        self._is_running = True
        elapsed_time = 0
        start_time = time.time()

        all_items = []
        for level in self.levels:
            all_items.extend(level.get('items', []))

        if not all_items:
            self.error.emit("没有可抽选的项目！")
            return

        while self._is_running and elapsed_time < 1.5:
            elapsed_time = time.time() - start_time
            selected = random.choice(all_items)
            self.update_result.emit(selected)
            self.msleep(100)

        while self._is_running and elapsed_time < 3.0:
            elapsed_time = time.time() - start_time
            selected = random.choice(all_items)
            self.update_result.emit(selected)
            self.msleep(200 + int(elapsed_time * 100))

        if self._is_running:
            rand_num = random.uniform(0, 100)
            cumulative_prob = 0
            selected_level = None

            for level in self.levels:
                cumulative_prob += level['probability']
                if rand_num <= cumulative_prob:
                    selected_level = level
                    break

            if selected_level:
                result_item = random.choice(selected_level['items'])
                self.lottery_finished.emit(result_item, selected_level['star'])
            else:
                result_item = random.choice(all_items)
                self.lottery_finished.emit(result_item, 1)

    def stop(self):
        self._is_running = False


class GradientFrame(QFrame):
    """渐变背景框架"""
    def __init__(self, color_scheme=None, parent=None):
        super().__init__(parent)
        self.color_scheme = color_scheme or ColorScheme.LIGHT

    def paintEvent(self, event):
        super().paintEvent(event)
        painter = QPainter(self)
        gradient = QLinearGradient(0, 0, 0, self.height())
        gradient.setColorAt(0, self.color_scheme["background"])
        gradient.setColorAt(1, self.color_scheme["white"])
        painter.fillRect(self.rect(), gradient)


class RoundedFrame(QFrame):
    """圆角框架"""
    def __init__(self, radius=15, parent=None, color_scheme=None):
        super().__init__(parent)
        self.radius = radius
        self.setAutoFillBackground(True)
        self.color_scheme = color_scheme or ColorScheme.LIGHT

    def set_color_scheme(self, scheme):
        self.color_scheme = scheme
        self.update()

    def paintEvent(self, event):
        painter = QPainter(self)
        painter.setRenderHint(QPainter.Antialiasing)
        bg_color = self.color_scheme["white"]
        painter.setBrush(QBrush(bg_color))
        painter.setPen(QPen(self.color_scheme["shadow"], 2))
        painter.drawRoundedRect(self.rect(), self.radius, self.radius)


class LotteryDisplay(QLabel):
    """抽选结果显示标签"""
    def __init__(self, parent=None, color_scheme=None):
        super().__init__(parent)
        self.setAlignment(Qt.AlignCenter)
        self.color_scheme = color_scheme or ColorScheme.LIGHT
        self.update_style()
        self.setMinimumHeight(100)

    def set_color_scheme(self, scheme):
        self.color_scheme = scheme
        self.update_style()

    def update_style(self):
        r, g, b = self.color_scheme["white"].getRgb()[:3]
        self.setStyleSheet(f"""
            QLabel {{
                font-family: 'Microsoft YaHei', sans-serif;
                font-size: 24px;
                font-weight: bold;
                color: {self.color_scheme["text"].name()};
                background-color: rgba({r},{g},{b},0.9);
                border: 2px solid {self.color_scheme["primary"].name()};
                border-radius: 15px;
                padding: 20px;
                margin: 10px;
            }}
        """)

    def set_result(self, text):
        self.setText(text)
        animation = QPropertyAnimation(self, b"pos")
        animation.setDuration(200)
        animation.setStartValue(self.pos() + QPoint(0, -10))
        animation.setEndValue(self.pos())
        animation.setEasingCurve(QEasingCurve.OutBounce)
        animation.start()


class SettingsDialog(QDialog):
    """设置对话框（仅保留编辑配置文件）"""
    def __init__(self, parent=None, color_scheme=None):
        super().__init__(parent)
        self.setWindowTitle("抽选器设置")
        self.setMinimumSize(500, 400)
        self.parent = parent
        self.color_scheme = color_scheme or ColorScheme.LIGHT

        # 配置文件路径基于基础路径
        self.obj_path = BASE_PATH / "obj.json"
        self.ensure_obj_file_exists()

        r, g, b = self.color_scheme["background"].getRgb()[:3]
        self.setStyleSheet(f"""
            QDialog {{
                background-color: rgb({r},{g},{b});
                color: {self.color_scheme["text"].name()};
            }}
            QGroupBox {{
                font-family: 'Microsoft YaHei', sans-serif;
                font-size: 16px;
                font-weight: bold;
                color: {self.color_scheme["text"].name()};
                border: 2px solid {self.color_scheme["primary"].name()};
                border-radius: 10px;
                margin-top: 10px;
                padding-top: 10px;
            }}
            QPushButton {{
                font-family: 'Microsoft YaHei', sans-serif;
                background-color: {self.color_scheme["primary"].name()};
                color: white;
                border: none;
                border-radius: 8px;
                padding: 8px 15px;
                margin: 5px;
                font-size: 14px;
            }}
            QPushButton:hover {{
                background-color: {self.color_scheme["text"].name()};
            }}
        """)

        main_layout = QVBoxLayout(self)

        config_group = QGroupBox("配置文件管理")
        config_layout = QVBoxLayout(config_group)

        self.edit_config_btn = QPushButton("📝 编辑json配置文件")
        self.edit_config_btn.clicked.connect(self.open_obj_file)
        config_layout.addWidget(self.edit_config_btn)

        path_label = QLabel(f"配置文件路径：\n{str(self.obj_path)}")
        path_label.setWordWrap(True)
        config_layout.addWidget(path_label)

        help_label = QLabel("""
配置文件格式说明：
{
  "levels": [
    {
      "star": 1,
      "probability": 78.5,
      "items": ["奖项1", "奖项2"]
    }
  ]
}
注意：所有级别概率总和建议为100%！
        """)
        help_label.setWordWrap(True)
        config_layout.addWidget(help_label)

        main_layout.addWidget(config_group)

        mode_group = QGroupBox("抽选模式设置")
        mode_layout = QVBoxLayout(mode_group)
        self.full_mode_check = QCheckBox("完全模式")
        if hasattr(parent, 'full_mode'):
            self.full_mode_check.setChecked(parent.full_mode)
        mode_layout.addWidget(self.full_mode_check)
        main_layout.addWidget(mode_group)

        button_box = QDialogButtonBox(QDialogButtonBox.Ok)
        button_box.accepted.connect(self.on_accept)
        main_layout.addWidget(button_box)

    def ensure_obj_file_exists(self):
        if not self.obj_path.exists():
            default_data = {
                "levels": [
                    {
                        "star": 1,
                        "probability": 78.5,
                        "items": ["获得100金币", "获得经验值+50", "跳过一次作业"]
                    },
                    {
                        "star": 2,
                        "probability": 18.5,
                        "items": ["打扫教室卫生", "写额外数学作业"]
                    },
                    {
                        "star": 3,
                        "probability": 3.0,
                        "items": ["操场跑圈5圈", "背完整本语文课本重点"]
                    }
                ]
            }
            try:
                with open(self.obj_path, 'w', encoding='utf-8') as f:
                    json.dump(default_data, f, ensure_ascii=False, indent=2)
                QMessageBox.information(self, "提示", f"已创建默认配置文件：{self.obj_path}")
            except Exception as e:
                QMessageBox.warning(self, "警告", f"创建配置文件失败：{str(e)}")

    def open_obj_file(self):
        try:
            url = QUrl.fromLocalFile(str(self.obj_path))
            if not QDesktopServices.openUrl(url):
                raise Exception("QDesktopServices.openUrl 失败")
            QMessageBox.information(self, "成功", "已用系统默认编辑器打开配置文件！")
        except Exception as e:
            QMessageBox.warning(self, "错误", f"打开文件失败：{str(e)}\n请手动打开：{self.obj_path}")

    def on_accept(self):
        if self.parent:
            new_mode = self.full_mode_check.isChecked()
            self.parent.set_full_mode(new_mode)
        self.accept()


class MainWindow(QMainWindow):
    """主窗口（整合完全模式视频播放，全局BGM循环）"""
    def __init__(self):
        super().__init__()
        self.setWindowTitle("曜麟·天衡")
        self.setGeometry(100, 100, 800, 600)

        self.current_scheme = self.detect_system_theme()
        self.color_scheme = ColorScheme.LIGHT if self.current_scheme == "light" else ColorScheme.DARK
        self.full_mode = False
        self.levels = []
        self.load_obj_data()

        self.cached_result = ""
        self.cached_star = 1
        self.video_window = None

        # 全局BGM播放器（用于完全模式）
        self.bgm_player = QMediaPlayer()
        self.bgm_audio_output = QAudioOutput()
        self.bgm_player.setAudioOutput(self.bgm_audio_output)
        self.bgm_audio_output.setVolume(0.5)
        self.bgm_player.mediaStatusChanged.connect(self.on_bgm_status_changed)
        self.bgm_player.errorOccurred.connect(self.on_bgm_error)

        self.init_ui()

        # ================== 设置窗口图标 ==================
        icon_path = BASE_PATH / "resources" / "icon.ico"
        if icon_path.exists():
            self.setWindowIcon(QIcon(str(icon_path)))
        else:
            print("⚠️ 图标文件不存在:", icon_path)

    def detect_system_theme(self):
        """检测系统主题（简写，保持原逻辑）"""
        try:
            sys_plat = platform.system()
            if sys_plat == "Windows":
                try:
                    import winreg
                    key = winreg.OpenKey(
                        winreg.HKEY_CURRENT_USER,
                        r"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"
                    )
                    val, _ = winreg.QueryValueEx(key, "AppsUseLightTheme")
                    winreg.CloseKey(key)
                    return "light" if val == 1 else "dark"
                except Exception:
                    pass
            if sys_plat == "Darwin":
                try:
                    out = subprocess.check_output(
                        ["defaults", "read", "-g", "AppleInterfaceStyle"],
                        stderr=subprocess.STDOUT
                    )
                    return "dark" if out.strip().lower() == b"dark" else "light"
                except subprocess.CalledProcessError:
                    return "light"
                except Exception:
                    pass
            if sys_plat == "Linux":
                try:
                    out = subprocess.check_output(
                        ["gsettings", "get",
                         "org.gnome.desktop.interface",
                         "color-scheme"],
                        stderr=subprocess.STDOUT
                    )
                    s = out.decode().lower()
                    if "dark" in s:
                        return "dark"
                    if "light" in s:
                        return "light"
                except Exception:
                    pass
            palette = QApplication.palette()
            window_color = palette.color(QPalette.Window)
            return "dark" if window_color.lightness() < 128 else "light"
        except Exception:
            return "light"

    def load_obj_data(self):
        obj_path = BASE_PATH / "obj.json"
        if obj_path.exists():
            try:
                with open(obj_path, 'r', encoding='utf-8') as f:
                    data = json.load(f)
                    self.levels = data.get("levels", [])
                    total_prob = sum(level.get("probability", 0) for level in self.levels)
                    if abs(total_prob - 100) > 0.1:
                        QMessageBox.warning(self, "提示", f"概率总和为{total_prob:.1f}%（建议100%），请检查配置文件！")
            except Exception as e:
                QMessageBox.warning(self, "警告", f"加载配置失败：{str(e)}\n使用默认配置")
                self.levels = [
                    {"star": 1, "probability": 78.3, "items": ["获得100金币", "获得经验值"]},
                    {"star": 2, "probability": 18.5, "items": ["获得稀有道具", "打扫教室"]},
                    {"star": 3, "probability": 3.0, "items": ["获得限定皮肤", "跑圈惩罚"]}
                ]
        else:
            QMessageBox.warning(self, "警告", "未找到obj.json配置文件！")
            self.levels = []

    def init_ui(self):
        central_widget = QWidget()
        self.setCentralWidget(central_widget)
        main_layout = QVBoxLayout(central_widget)

        # 标题图片（缩小尺寸）
        title_label = QLabel()
        img_path = BASE_PATH / "resources" / "img" / "title.png"
        pix = QPixmap(str(img_path))
        if not pix.isNull():
            scaled_pix = pix.scaled(400, 200, Qt.KeepAspectRatio, Qt.SmoothTransformation)
            title_label.setPixmap(scaled_pix)
        else:
            title_label.setText("奖励/惩罚抽选器")
        title_label.setAlignment(Qt.AlignCenter)
        r, g, b = self.color_scheme["text"].getRgb()[:3]
        title_label.setStyleSheet(f"""
            QLabel {{
                font-family: 'Microsoft YaHei', sans-serif;
                font-size: 32px;
                font-weight: bold;
                color: rgb({r},{g},{b});
                margin: 20px 0;
            }}
        """)
        main_layout.addWidget(title_label)

        self.mode_label = QLabel("当前模式：普通模式")
        self.mode_label.setAlignment(Qt.AlignCenter)
        self.update_mode_display()
        main_layout.addWidget(self.mode_label)

        self.result_display = LotteryDisplay(color_scheme=self.color_scheme)
        self.result_display.set_result("点击开始抽选")
        main_layout.addWidget(self.result_display)

        button_layout = QHBoxLayout()
        self.start_button = QPushButton("开始抽选")
        self.stop_button = QPushButton("停止抽选")
        self.settings_button = QPushButton("设置")

        self.stop_button.setEnabled(False)
        self.update_button_styles()

        self.start_button.clicked.connect(self.start_lottery)
        self.stop_button.clicked.connect(self.stop_lottery)
        self.settings_button.clicked.connect(self.open_settings)

        button_layout.addWidget(self.start_button)
        button_layout.addWidget(self.stop_button)
        button_layout.addWidget(self.settings_button)
        main_layout.addLayout(button_layout)

        separator = QFrame()
        separator.setFrameShape(QFrame.HLine)
        separator.setStyleSheet(f"background-color: {self.color_scheme['primary'].name()}; height: 2px;")
        main_layout.addWidget(separator)

        self.main_panel = RoundedFrame(radius=15, color_scheme=self.color_scheme)
        main_layout.addWidget(self.main_panel, stretch=1)

        self.set_background()

    def update_mode_display(self):
        mode_text = "当前模式：完全模式" if self.full_mode else "当前模式：普通模式"
        self.mode_label.setText(mode_text)
        r, g, b = self.color_scheme["white"].getRgb()[:3]
        self.mode_label.setStyleSheet(f"""
            QLabel {{
                font-family: 'Microsoft YaHei', sans-serif;
                font-size: 16px;
                color: {self.color_scheme["text"].name()};
                background-color: rgba({r},{g},{b},0.8);
                border-radius: 8px;
                padding: 8px;
                margin: 5px;
            }}
        """)

    def update_button_styles(self):
        base_style = f"""
            font-family: 'Microsoft YaHei', sans-serif;
            font-size: 18px;
            font-weight: bold;
            border: none;
            border-radius: 15px;
            padding: 15px 30px;
            margin: 10px;
            color: white;
        """
        self.start_button.setStyleSheet(f"""
            QPushButton {{
                {base_style}
                background-color: {self.color_scheme["primary"].name()};
            }}
            QPushButton:hover {{
                background-color: {self.color_scheme["text"].name()};
            }}
            QPushButton:disabled {{
                background-color: {self.color_scheme["shadow"].name()};
                opacity: 0.7;
            }}
        """)
        self.stop_button.setStyleSheet(f"""
            QPushButton {{
                {base_style}
                background-color: {self.color_scheme["secondary"].name()};
            }}
            QPushButton:hover {{
                background-color: {self.color_scheme["text"].name()};
            }}
            QPushButton:disabled {{
                background-color: {self.color_scheme["shadow"].name()};
                opacity: 0.7;
            }}
        """)
        self.settings_button.setStyleSheet(f"""
            QPushButton {{
                {base_style}
                background-color: {self.color_scheme["accent"].name()};
            }}
            QPushButton:hover {{
                background-color: {self.color_scheme["text"].name()};
            }}
        """)

    def set_background(self):
        palette = QPalette()
        gradient = QLinearGradient(0, 0, 0, self.height())
        gradient.setColorAt(0, self.color_scheme["background"])
        gradient.setColorAt(1, self.color_scheme["white"])
        palette.setBrush(QPalette.Window, QBrush(gradient))
        self.setPalette(palette)

    def open_settings(self):
        dialog = SettingsDialog(self, color_scheme=self.color_scheme)
        dialog.exec()

    def set_full_mode(self, enabled):
        """设置完全模式状态，并控制BGM播放/停止"""
        if self.full_mode == enabled:
            return
        self.full_mode = enabled
        self.update_mode_display()
        if enabled:
            self.start_bgm()
        else:
            self.stop_bgm()

    def start_bgm(self):
        """开始循环播放背景音乐（查找可用文件）"""
        bgm_candidates = [
            BASE_PATH / "resources" / "music" / "bgm.m4s",
            BASE_PATH / "resources" / "music" / "bgm.mp3",
            # 保留原绝对路径作为备选（若用户有特殊需求）
            Path(r"C:\Users\LJL\Desktop\Resources\Music\bgm.m4s"),
        ]
        bgm_path = None
        for p in bgm_candidates:
            if p.exists():
                bgm_path = p
                break

        if bgm_path is None:
            print("⚠️ 未找到 BGM 文件，无法播放背景音乐")
            return

        try:
            bgm_url = QUrl.fromLocalFile(str(bgm_path.resolve()))
            self.bgm_player.setSource(bgm_url)
            self.bgm_player.play()
            print(f"✅ 全局 BGM 开始播放: {bgm_path}")
        except Exception as e:
            print(f"❌ 全局 BGM 播放失败: {e}")

    def stop_bgm(self):
        """停止背景音乐"""
        self.bgm_player.stop()
        print("⏹️ 全局 BGM 已停止")

    def on_bgm_status_changed(self, status):
        """实现循环播放：当播放完毕时重新开始"""
        if status == QMediaPlayer.MediaStatus.EndOfMedia:
            self.bgm_player.play()
            print("🔄 BGM 循环播放")

    def on_bgm_error(self, error, error_string):
        print(f"⚠️ BGM 播放错误: {error_string}")

    def start_lottery(self):
        if not self.levels or sum(len(level.get('items', [])) for level in self.levels) == 0:
            QMessageBox.warning(self, "警告", "没有可抽选的项目！请先编辑配置文件。")
            return

        self.start_button.setEnabled(False)

        if self.full_mode:
            self.start_silent_lottery()
        else:
            self.start_normal_lottery()

    def start_silent_lottery(self):
        self.result_display.set_result("正在抽取结果...")
        self.silent_lottery_thread = SilentLotteryThread(self.levels)
        self.silent_lottery_thread.lottery_result.connect(self.on_silent_lottery_finished)
        self.silent_lottery_thread.error.connect(self.on_lottery_error)
        self.silent_lottery_thread.start()

    def on_silent_lottery_finished(self, result, star_level):
        self.cached_result = result
        self.cached_star = star_level
        self.play_lottery_video(star_level)

    def play_lottery_video(self, star_level):
        if self.video_window is not None:
            try:
                self.video_window.close()
                self.video_window.deleteLater()
            except:
                pass
            self.video_window = None

        self.result_display.set_result("正在播放动画...")
        self.video_window = VideoPlayerWindow(self, star_level)
        self.video_window.video_finished.connect(self.on_video_finished)
        self.video_window.show()

    def on_video_finished(self):
        self.result_display.set_result(f"抽选结果：{self.cached_result} (⭐{'⭐'*(self.cached_star-1)})")
        self.start_button.setEnabled(True)
        if self.video_window is not None:
            self.video_window.deleteLater()
            self.video_window = None

    def start_normal_lottery(self):
        self.stop_button.setEnabled(True)
        self.lottery_thread = LotteryThread(self.levels)
        self.lottery_thread.update_result.connect(self.update_lottery_display)
        self.lottery_thread.lottery_finished.connect(self.on_normal_lottery_finished)
        self.lottery_thread.error.connect(self.on_lottery_error)
        self.lottery_thread.finished.connect(self.lottery_thread_finished)
        self.lottery_thread.start()

    def update_lottery_display(self, text):
        self.result_display.set_result(text)

    def on_normal_lottery_finished(self, result, star_level):
        self.result_display.set_result(f"抽选结果：{result} (⭐{'⭐'*(star_level-1)})")

    def stop_lottery(self):
        if hasattr(self, 'lottery_thread') and self.lottery_thread.isRunning():
            self.lottery_thread.stop()
            self.lottery_thread.quit()
            self.lottery_thread.wait()
            self.lottery_thread.update_result.disconnect()
            self.lottery_thread.lottery_finished.disconnect()
            self.lottery_thread.error.disconnect()

    def on_lottery_error(self, error_msg):
        QMessageBox.warning(self, "错误", error_msg)
        self.start_button.setEnabled(True)
        self.stop_button.setEnabled(False)

    def lottery_thread_finished(self):
        self.start_button.setEnabled(True)
        self.stop_button.setEnabled(False)

    def closeEvent(self, event):
        """程序关闭时停止BGM"""
        self.stop_bgm()
        event.accept()


if __name__ == "__main__":
    app = QApplication(sys.argv)
    app.setFont(QFont("Microsoft YaHei", 10))

    window = MainWindow()
    window.show()

    sys.exit(app.exec())