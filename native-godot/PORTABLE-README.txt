慢时光 · Windows便携运行包

双击 Start-Town.cmd 启动游戏，不需要安装Python、Godot或.NET SDK。
Engine、Runtime、Content、Scripts和.godot目录必须与启动文件放在一起。
可以把整个文件夹复制到其他Windows x64电脑运行。

鼠标右键拖拽环绕视角，WASD移动，滚轮缩放，M打开地图，F5保存。
经营管理中提供宠物、家庭、外观、照护、财务、档案、存档等页面。
在“存档”页导入旧版manor.db或导出JSON备份；导入前会自动备份当前新版存档。
原始SQLite数据库只读，不会被新版覆盖。
存档位置可在“经营管理 → 存档”查看；存档保存在用户目录，移动游戏文件夹不会自动转移存档。

该包包含运行时，体积大于单纯的游戏源码。请保留整个文件夹。
完整开发源码、重新打包脚本和迁移说明在native-godot工程中。
Godot许可证见Engine/GODOT-LICENSE.txt；.NET许可证见Runtime/LICENSE.txt。
