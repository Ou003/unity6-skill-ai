# 官方技能库更新方法

Official/ 是固定版本的快照, 不随 Unity 官方仓库自动更新。
需要升级时由维护者在本机重跑构建脚本, 再提交到本仓库。

```text
1 下载上游仓库压缩包并解压 (在包目录之外的临时目录操作)
   curl -L -o skills-main.zip https://codeload.github.com/Unity-Technologies/skills/zip/refs/heads/main
   python -c "import zipfile; zipfile.ZipFile('skills-main.zip').extractall('.')"

2 用 API 取当前 main 的 commit sha 与日期
   https://api.github.com/repos/Unity-Technologies/skills/commits/main

3 编辑 tools/build_official_skills.py 顶部的
   UPSTREAM_COMMIT / UPSTREAM_COMMIT_DATE, 必要时补充 KEYWORDS / TIERS

4 运行
   python tools/build_official_skills.py skills-main

5 运行 python tools/gen_meta.py 补齐 .meta, 再用 Unity 6 跑一次无头自测
   Unity.exe -batchmode -nographics -quit -projectPath <验证工程> \
     -executeMethod Unity6SkillAI.Editor.BatchSelfTest.Run

6 更新 CHANGELOG.md 与 docs/OFFICIAL-SKILLS.md, 本地 commit
```

注意: 脚本只复制 SKILL.md 主文件, 不会把官方仓库的 .cs 示例带进包内。
