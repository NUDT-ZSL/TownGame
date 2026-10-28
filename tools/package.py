"""仅打包可分发源码，排除存档、缓存和机器相关的依赖安装。"""
from pathlib import Path
from zipfile import ZipFile, ZIP_DEFLATED

ROOT = Path(__file__).resolve().parents[1]


def main():
    sources = sorted(ROOT.glob("*.py")) + sorted((ROOT/"tests").glob("*.py")) + sorted((ROOT/"tools").glob("*.py"))
    listing = ["# 慢时光宠物经营小镇 v5 · 完整分文件源码", "", "环境：Python 3.10+，唯一第三方依赖 pygame。", "", "```powershell", "python -m pip install -r requirements.txt", "python main.py", "```", "", "每个代码块对应一个独立文件，按标题路径保存。详细玩法与设计见 README.md 和开发说明.md。", ""]
    for source in sources:
        listing.extend([f"## {source.relative_to(ROOT).as_posix()}", "", "```python", source.read_text(encoding="utf-8").rstrip(), "```", ""])
    listing.extend(["## requirements.txt", "", "```text", (ROOT/"requirements.txt").read_text(encoding="utf-8").rstrip(), "```", ""])
    (ROOT/"SOURCE_CODE.md").write_text("\n".join(listing), encoding="utf-8")
    files = sources + [ROOT/name for name in ("README.md", "开发说明.md", "requirements.txt", ".gitignore", "SOURCE_CODE.md", "assets/README.md")]
    files += sorted((ROOT/"docs").glob("*.png"))
    files += sorted((ROOT/"docs").glob("*.json"))
    target = ROOT/"pet_manor_source.zip"
    with ZipFile(target, "w", ZIP_DEFLATED) as archive:
        for file in files:
            archive.write(file, Path("pet_manor")/file.relative_to(ROOT))
    print(f"Exported {len(files)} files -> {target}")


if __name__ == "__main__":
    main()
