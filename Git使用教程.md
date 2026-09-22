# Git 使用教程

适合第一次使用 Git 的 Windows 用户。本教程使用 **GitHub Desktop 图形界面，直接提交到 `main` 分支**，无需输入命令。英文按钮后附中文解释，软件版本不同可能有少量文字差异。

项目：[YanqingXu/Hunter](https://github.com/YanqingXu/Hunter)。策划主要使用 `design/`，美术主要使用 `art/`。其他目录的职责见 [项目说明](README.md)。

第一次使用请从第二节开始；配置好以后，每天照着第一节操作即可。

## 一、每天照着做：六步速查

| 步骤 | 做什么 | 完成时看什么 |
| --- | --- | --- |
| 1. 开工前更新 | 打开 Desktop，确认 `Current Repository` 是 `Hunter`、`Current Branch` 是 `main`。没有未完成修改时，点击 `Fetch origin`；出现 `Pull origin` 就继续点它 | 更新结束，没有报错或待拉取提示，再打开工作文件 |
| 2. 修改并保存 | 在本地 Hunter 文件夹的 `design/` 或 `art/` 内完成工作，保存并关闭相关编辑软件 | 文件能正常重新打开，内容正确 |
| 3. 检查变更 | 回到 `Changes`，逐个检查文件路径、增删状态，只勾选本次要交付的文件 | 没有误删、临时文件或无关改动 |
| 4. 提交到本机 | 填写 `Summary`，点击 `Commit to main` | `History` 中出现本次提交；已提交的内容不再列在 `Changes` 中 |
| 5. 上传给团队 | 点击 `Push origin`；若提示远程有更新，按第六节处理 | 上传结束，没有报错或待推送提示 |
| 6. 确认交付 | 打开 GitHub 仓库网页，选择 `main`，查看本次文件及提交记录 | 能看到自己的提交；队友拉取后能拿到文件 |

**记住：保存文件 ≠ Commit；Commit ≠ Push。只有 Push 成功，队友才能获取这次交付。**

开工时如果 `Changes` 已经有文件，或显示还有待上传的提交，先核对是不是昨天未完成的工作。不要为了“清空界面”点击丢弃；按第六节处理后再更新。

## 二、第一次使用：把项目放到自己电脑上

### 1. 准备账号和仓库权限

1. 打开 [GitHub](https://github.com/)，注册或登录自己的账号，完成邮箱验证。
2. 将自己的 GitHub 用户名告诉项目负责人，由负责人邀请你加入仓库并授予写权限。
3. 打开邀请邮件或负责人提供的邀请链接，确认仓库是 `YanqingXu/Hunter`，接受邀请。
4. 用该账号打开 [Hunter 仓库](https://github.com/YanqingXu/Hunter)，确认能看到文件列表。

**成功标志：** 邀请已接受，能进入项目页面。能看见或下载项目不一定代表能上传，负责人还需要确认你有写权限。本教程也以仓库规则允许直接提交 `main` 为前提；遇到分支保护提示时交由负责人处理。

### 2. 安装并登录 GitHub Desktop

1. 从 [GitHub Desktop 官网](https://desktop.github.com/) 下载 Windows 安装程序并安装。
2. 打开软件，选择 `Sign in to GitHub.com`（登录 GitHub），按浏览器提示完成授权，再返回 Desktop。
3. 初次设置时填写提交作者的姓名和邮箱。姓名用团队能认出的名字，邮箱使用自己 GitHub 账号关联的邮箱；也可使用 GitHub 提供的隐私邮箱。
4. 后续需要核对作者信息时，在 Windows 菜单 `File → Options → Git` 中查看。

**成功标志：** Desktop 已登录你的账号，作者信息正确。本教程的按钮操作不需要另开命令行。

作者信息的设置方式见 [GitHub 官方说明](https://docs.github.com/en/desktop/configuring-and-customizing-github-desktop/configuring-git-for-github-desktop)。

### 3. 电脑上还没有项目：Clone 一次

1. 在 Desktop 点击 `File → Clone repository...`（克隆仓库）。
2. 选择 `URL` 标签，填写下面的项目地址：

   ```text
   https://github.com/YanqingXu/Hunter.git
   ```

3. 在 `Local Path`（本地路径）选择项目保存位置，例如 `D:\Projects\Hunter`。这是示例，可换成自己电脑上的目录；目标文件夹应为空或尚未创建，不要指向已有工作文件的文件夹。
4. 点击 `Clone`，等待下载结束。
5. 确认左上角 `Current Repository`（当前仓库）为 `Hunter`，上方 `Current Branch`（当前分支）为 `main`。
6. 点击 `Repository → Show in Explorer`（在资源管理器中显示），记住打开的文件夹，以后从这里打开和保存项目文件。

**成功标志：** 本地 Hunter 文件夹中能看到 `README.md`、`design`、`art` 等内容，Desktop 显示 `Hunter / main`。Clone 通常只做一次，日常更新使用 Pull。

也可从仓库网页的 `Code → Open with GitHub Desktop` 进入克隆窗口。不要使用 `Download ZIP` 代替克隆：ZIP 解压目录没有 Git 历史，不能直接照本教程同步。操作参考：[从 Desktop 克隆仓库](https://docs.github.com/en/desktop/adding-and-cloning-repositories/cloning-and-forking-repositories-from-github-desktop)、[从网页打开 Desktop](https://docs.github.com/en/desktop/adding-and-cloning-repositories/cloning-a-repository-from-github-to-github-desktop)。

### 4. 电脑上已有 Git 仓库：添加即可

如果之前已通过 Git 克隆项目，只是还没在 Desktop 中打开：

1. 点击 `File → Add local repository...`（添加本地仓库）。
2. 点击 `Choose...`，选择已有的 **Hunter 根文件夹**，不要只选里面的 `art` 或 `design`。
3. 点击 `Add repository`。
4. 确认当前仓库和分支；用 `Repository → View on GitHub` 核对打开的是 `YanqingXu/Hunter`。

**成功标志：** Desktop 能显示该仓库原有的历史和本地修改。添加仓库不会重新下载项目。[官方添加步骤](https://docs.github.com/en/desktop/adding-and-cloning-repositories/adding-a-repository-from-your-local-computer-to-github-desktop)

如果提示该目录不是 Git 仓库，不要点击创建新仓库。它可能是 ZIP 解压目录或普通文件夹：保留里面的工作，在另一个空位置按上一节克隆，再与负责人确认后复制需要交付的文件，避免旧文件整包覆盖新版本。

## 三、这些按钮分别是什么意思

把“仓库”理解为带有修改历史的项目文件夹。你电脑上的叫本地仓库，GitHub 上供大家同步的叫远程仓库。`main` 是本项目共同交付的主分支；`origin` 是远程仓库的默认简称。

| 界面文字 | 中文意思 | 什么时候用 |
| --- | --- | --- |
| `Clone` | 克隆：下载项目和历史，在本机建立仓库 | 第一次获取项目 |
| `Fetch origin` | 检查远程更新，获取提交信息；不会把队友改动合进当前工作文件 | 开工前、需要检查更新时 |
| `Pull origin` | 拉取：把远程的新提交合入当前本地分支，更新工作文件 | Fetch 后发现队友有更新时 |
| `Changes` | 尚未提交的文件变化 | 检查这次交付内容 |
| `Summary` / `Description` | 提交标题 / 详细说明 | 说明改了什么；标题必填，详情按需填写 |
| `Commit to main` | 将勾选的修改记录为本机的一次版本 | 文件检查完成后 |
| `Push origin` | 上传本地提交，让队友可以拉取 | Commit 后 |
| `History` | 当前分支的提交历史 | 查自己或队友改了什么 |
| `No local changes` | 没有尚未提交的文件变化 | 仅说明本地改动已提交或没有改动，不能证明已上传 |

例如：在 Excel 按保存，只是把内容写入硬盘；点击 Commit，才会产生可追溯的本地版本；点击 Push，才会把版本送到 GitHub。同步按钮的行为见 [官方同步说明](https://docs.github.com/en/desktop/working-with-your-remote-repository-on-github-or-github-enterprise/syncing-your-branch-in-github-desktop)。

## 四、跟着做一次真实交付

以下路径是示例，不是已经存在的项目文件。用本次真正需要交付的内容操作，不必向主分支上传练习文件。

### 1. 开工前先同步

1. 与相关成员约好这次修改哪些文件，尤其是 Excel、PSD 等文件。
2. 在 Desktop 确认 `Hunter / main`，本地没有尚未处理的修改或待上传提交。
3. 点击 `Fetch origin`。如果出现 `Pull origin`，点击它并等待完成；如果检查成功且没有新提交，就可以继续工作。
4. 更新完成后，再打开 Excel、绘图软件或其他编辑软件，避免软件中仍开着旧内容，保存时覆盖刚拉下来的新版本。

### 2. 策划示例：交付一份规则说明

1. 从 `Repository → Show in Explorer` 打开 Hunter。
2. 进入 `design` 文件夹，用记事本等文本编辑器创建 `撤离规则说明.md`，或编辑团队已有的规则文件。Markdown 是普通文本，也可以在记事本中编辑。
3. 写清这次确认的规则，例如撤离触发条件、倒计时时长和失败条件；保存并关闭编辑器。用记事本另存为时选择“所有文件”、UTF-8 编码，确认文件名没有变成 `.md.txt`。
4. 如果交付的是数值 Excel，就把约定的 `.xlsx` 源表放在 `design/` 对应位置，用同样的流程提交；不要手工修改工具导出的配置产物。
5. 回到 Desktop，在 `Changes` 中检查路径和内容。文本通常能看到修改前后的差异；Excel 需要在 Excel 中自行核对数据。

**成功标志：** 变更列表中出现预期的 `design/` 文件，内容是本次要交付的版本。

### 3. 美术示例：交付角色贴图

1. 开工前与客户端确认命名、尺寸、格式和交付位置。
2. 在 `art/` 下按团队约定整理源文件和导出资源。例如本次可以是 `art/characters/hunter_idle.png`；示例中的子文件夹需要时再创建，不代表统一目录规范已经制定。
3. 保存源文件、导出资源，关闭相关编辑软件；重新打开导出图片，检查尺寸、透明背景和内容。
4. 在 Desktop 核对每个文件的路径及大小，确保没有遗漏本次约定交付的源文件。大 PSD 等先按第七节处理。
5. 图片预览只作辅助；PSD、动画等文件不一定能显示内容差异，需要使用对应软件检查。

**成功标志：** `Changes` 中列出的就是本次交付资源，文件能正常打开。仅把源素材交到 `art/` 时，无需自己创建 Unity `.meta`；导入客户端工程由相关成员按约定完成。

### 4. 检查并 Commit

1. 在 `Changes` 中逐个核对新增、修改、删除的文件；删除也会被提交给全队，看到不认识的删除先停下来核实。
2. 只勾选本次交付需要的文件。同一交付需要一起使用的文件一起提交；无关工作分开提交。取消勾选只是暂不提交，不会删除文件，也不会自动忽略它。
3. 在 `Summary` 填写能让队友看懂的中文标题，例如 `策划：补充撤离倒计时和失败条件` 或 `美术：更新猎人待机贴图`；避免只写“更新”“改了”。
4. 在 `Description` 中按需补充影响、规格或待确认项，例如“贴图 256×256，透明背景；配套动画下次交付”。
5. 再看一次按钮是否为 `Commit to main`，点击后等待完成。

**成功标志：** `History` 中出现自己的提交标题。本次已提交的内容从 `Changes` 中消失；未勾选的改动仍然保留。具体界面可对照 [官方提交说明](https://docs.github.com/en/desktop/making-changes-in-a-branch/committing-and-reviewing-changes-to-your-project-in-github-desktop)。

### 5. Push 并确认团队能拿到

1. 点击上方 `Push origin`，等待完成。它会上传当前分支所有尚未上传的提交，不只是刚勾选过的几个文件。
2. 如果提示远程有新提交，按第六节先拉取；如果出现冲突，先处理冲突再继续。
3. 点击 `Repository → View on GitHub`，确认网址是 `YanqingXu/Hunter`，网页分支选择 `main`。
4. 打开本次提交的文件，查看最新内容和提交记录。Excel、PSD 等无法在线预览时，可核对文件名、大小和提交记录，必要时下载到仓库外检查。
5. 告知相关队友交付了什么、在哪个路径，让他们 `Fetch origin → Pull origin` 获取。

**成功标志：** Push 没有错误，网页的 `main` 分支能找到本次提交。即使别人随后又上传了内容，你的记录也应能在历史中找到。离线时可以保存和 Commit，联网后仍需 Push 才完成交付。[官方上传说明](https://docs.github.com/en/desktop/making-changes-in-a-branch/pushing-changes-to-github-from-github-desktop)

## 五、团队一起用时的约定

- **同一文件先协调再修改。** 尤其是 `.xlsx`、`.psd`、`.blend` 等，Git 通常无法像文本一样自动合并其中的内容。工作前约定修改人，交付后再交给下一位；不同工作可拆成不同文件。
- **始终修改仓库内的文件。** 桌面上的副本不会自动同步。需要从其他位置交付时，先更新仓库，再核对差异，只复制需要交付的文件。
- **提交前保存并关闭相关软件。** 不提交 `~$` 开头的 Office 锁文件、临时导出、自动备份、缓存或无关文件。若它们反复出现，请程序负责人完善忽略配置，不能假定当前仓库会自动排除所有临时文件。
- **目录按职责使用。** 策划源表和规则放 `design/`，美术源文件与交付资源放 `art/`。修改 `client/` 或工具产物前，与对应负责人确认。
- **小批次、说清楚。** 完成一项可交付工作就提交并上传。不要积攒几天改动后，用“最终版2”“最终最终版”反复另存来替代版本记录。
- **改名、移动、删除也是团队变更。** 先确认引用关系，不随意移动别人正在使用的文件。
- **Unity 资源与 `.meta` 配套。** 在客户端 Unity 工程中新增资源时，把 Unity 生成的对应 `.meta` 一起提交；已有 `.meta` 有改动也要核对。移动、改名、删除优先在 Unity 编辑器内操作，并检查资源及其 `.meta` 的配套变化；文件夹自身也可能有 `.meta`。不要把它当缓存删除，否则可能破坏场景、材质等引用。详见 [Unity 资源元数据说明](https://docs.unity3d.com/Manual/AssetMetadata.html)。

## 六、遇到问题先看这里

### 1. 修改了文件，Changes 却没有显示

先检查：是否在编辑软件中保存？是否选中了 `Hunter`？是否修改了 `Show in Explorer` 打开的那份文件？内容是否与原来完全相同？

再看 `History`，可能已经提交过。仍找不到时，将具体文件路径告诉程序负责人，检查文件是否被忽略。不要反复新建仓库或复制整个项目。

### 2. 已经 Commit，队友还是看不到

确认是否完成 `Push origin`，以及网页上是否查看正确仓库的 `main` 分支。`No local changes` 只表示没有未提交的修改，不等于已上传。网页已有提交时，让队友检查分支并 Fetch、Pull。

### 3. 推送时要求先 Fetch / Pull

通常是你工作期间有人先上传了提交：

1. 先保存并关闭相关编辑软件，检查 `Changes`。本教程的顺序是先把本次交付 Commit，再处理远程更新。
2. 若仍有未提交工作，先将重要文件复制到 **Hunter 文件夹以外** 备份。完整且可交付的修改可以检查后 Commit；尚未完成或不清楚如何处理的修改，请负责人协助，不要点丢弃来让 Pull 通过。
3. 没有未处理的本地修改后，按提示 `Fetch origin`，再点击普通的 `Pull origin`。
4. 无冲突时，检查拉取后文件是否正确，再 `Push origin`；有冲突时按下一节处理。

如果出现 `Pull origin with rebase`、`Force push origin` 或其他不在教程中的操作，先请负责人检查设置和状态。本流程使用普通 Pull，不靠强制推送覆盖远程内容。

### 4. 出现 Conflict / Merge conflict（冲突）

冲突表示 Git 无法自动决定如何组合两边的修改，不代表你的工作必然丢失。

1. 停止继续编辑冲突文件，记录报错、冲突文件路径和刚才的操作；保留当前仓库，不删除或重新克隆来“修复”。
2. 将现有相关文件复制到仓库外备份；冲突时工作文件可能包含冲突标记，不能把它当成完整的原始版本。本教程中 Pull 前已经 Commit 的版本可由负责人从历史提取。
3. 联系另一位修改者和程序负责人，说明双方分别改了什么。不要凭按钮名直接选择“保留我的”或“保留对方的”。
4. 文本由双方确认最终内容；Excel、PSD 等通常需要提取双方版本，在原软件中人工整合或选定一个完整版本。
5. 由负责人协助完成冲突处理和合并提交，检查文件能正常打开，再上传并通知相关成员。

### 5. 误删或误改了文件

| 当前状态 | 建议处理 |
| --- | --- |
| 尚未 Commit | 先备份仍需保留的修改，再让负责人协助从最后一次提交恢复指定文件；编辑软件的撤销或回收站也可能帮助恢复 |
| 已 Commit，尚未 Push | 保留仓库并指出错误提交，让负责人协助修正；不懂影响时不要重置历史 |
| 已 Push | 尽快告知受影响的人，通常用一次新的修正提交恢复正确内容，保留大家已经同步的历史 |

`Discard changes` 是丢弃尚未提交的改动，**不是取消勾选，也不是取消上传**。Git 只能可靠找回已记录的版本；尚未保存或提交的内容不保证能恢复。

### 6. 权限、登录或网络报错

| 现象 | 下一步 |
| --- | --- |
| 仓库页面打不开，或提示 `Repository not found` | 核对地址、当前登录账号和邀请是否已接受；私有仓库无权限时也可能如此提示 |
| 提示没有写权限，或要求创建 `Fork` | 停下，不创建个人副本来代替团队交付；请负责人检查写权限 |
| 提示受保护分支或仓库规则不允许推送 | 保留本地提交，将原始提示交给负责人确认工作流，不自行绕过规则 |
| 登录失效或认证失败 | 在 Desktop 中检查账号，按提示重新登录；不要把密码、验证码发给队友 |
| 下载、拉取或推送超时 | 检查网络和 GitHub 是否可访问，保留本地文件和提交，恢复后重试；网页没有确认前不视为已交付 |
| 提示文件过大 | 按第七节处理，保留源文件，不反复重试或仅改扩展名 |

求助时提供：报错截图或原文、当前仓库与分支、相关文件路径、最后点击了哪个按钮，以及是否已 Commit / Push。

## 七、美术大文件：先确认，再提交

GitHub 普通 Git 对单个文件有大小限制：**超过 50 MiB 会警告，超过 100 MiB 会被阻止上传**。Windows 文件属性中的“大小”可用于检查；100 MiB 是 104,857,600 字节。不要用网页上传来绕过限制，网页上传的单文件限制更小。详见 [GitHub 官方大文件说明](https://docs.github.com/en/repositories/working-with-files/managing-large-files/about-large-files-on-github)。

**本教程编写时，仓库没有用于声明 LFS 跟踪规则的 `.gitattributes`，不能默认 PSD 等大文件已由 Git LFS 管理。** Git LFS 是专门管理大文件内容的配套机制；安装 Desktop 并不代表项目所有美术文件都会自动走 LFS。

1. 大型源文件在首次 Commit 前，先告知程序负责人文件格式、大小和预计更新频率；超过 50 MiB 的文件先确认存储方案。
2. 由程序负责人统一配置 LFS 跟踪规则、确认存储与流量配额，并处理已有文件是否需要迁移；成员按负责人说明完成相应初始化。
3. 负责人确认配置已生效后，再按正常流程提交和上传；检查队友拉取后能拿到并打开真实资源。
4. 如果大文件已进入本地提交并被拒绝上传，保留原文件和仓库，交给负责人处理未推送历史。仅把文件删掉再做一次新提交，旧提交里的大文件仍可能导致上传失败。

本教程只说明操作和交接方式，不修改项目的 Git LFS、忽略规则或分支权限。即使启用 LFS，也仍需协调同一 PSD、Excel 等文件的修改；它不会自动合并这些文件里的内容。
