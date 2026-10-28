"""启动：python main.py。--smoke 供无显示设备的自动化启动验证使用。"""
import argparse
import os
import sys
from pathlib import Path

os.environ.setdefault("PYGAME_HIDE_SUPPORT_PROMPT", "1")


def _setup_local_runtime():
    """仅当当前环境未安装 pygame 时，才回退到项目内 .runtime 目录。

    .runtime 里的扩展模块与解释器版本绑定（例如 cp313 的 .pyd），无条件加入
    sys.path 会让 Python 3.11 等环境加载到不兼容的 pygame，报
    `No module named 'pygame.base'`。pip 已安装 pygame 时一律优先使用环境中的版本。
    """
    local_runtime = Path(__file__).resolve().parent / ".runtime"
    if not local_runtime.is_dir():
        return
    try:
        import pygame  # noqa: F401
    except ModuleNotFoundError:
        sys.path.insert(0, str(local_runtime))


_setup_local_runtime()


def main():
    parser = argparse.ArgumentParser(description="慢时光宠物庄园")
    parser.add_argument("--smoke", action="store_true", help="无窗口渲染三帧并退出")
    parser.add_argument("--db", help="指定独立存档路径，默认项目根目录 manor.db")
    args = parser.parse_args()
    if args.smoke:
        os.environ["SDL_VIDEODRIVER"] = "dummy"
        os.environ["SDL_AUDIODRIVER"] = "dummy"
    try:
        from db import Database
        from game import Game
        from interface import ManorApp
        database = Database(args.db) if args.db else Database()
        game = Game(database)
        ManorApp(game).run(frame_limit=3 if args.smoke else None)
    except ModuleNotFoundError as exc:
        if exc.name == "pygame":
            print("缺少依赖，请先执行：python -m pip install -r requirements.txt")
            return 1
        if (exc.name or "").startswith("pygame."):
            print("项目内 .runtime 中的 pygame 与当前 Python 版本不匹配，"
                  "请先执行：python -m pip install -r requirements.txt", file=sys.stderr)
            return 1
        raise
    except (RuntimeError, OSError) as exc:
        print(f"庄园无法继续运行：{exc}\n存档保存在数据库中，请检查磁盘与文件权限。", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
