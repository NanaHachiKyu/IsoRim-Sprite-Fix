# IsoRim Sprite Fix（贴图武器修复）

RimWorld 1.7（Waylith 引擎）用的修复 mod：让 IsoRim 的纸片小人身上的**武器、装备、其它 mod 的自绘部件**，
以及**立牌建筑里的内容**，正确地跟随卡片立起、随视角转动。

> 本 mod 只修 2D。如果你装了启用 3D 小人的 mod（SYS 相关），3D 小人依旧会有问题 —— 纸片小人就该配纸片武器。

## 功能

- **武器贴图**：待机时按原版 `DrawCarriedWeapon` 规则重算朝向，修复「移动时枪乱跑」「背身不遮挡武器」。
- **额外部件**：自动检测卡片小人身上未被 IsoRim 变换的绘制调用（光环、护盾、装饰等），列进设置，勾选后按卡片方式绘制。
  每个方法可单独调 **层级（renderQueue）/ 平面左右 / 平面上下 / 深度**，数值可直接点击输入。
- **建筑立牌**：被装进立牌建筑里的东西（穿刺桩上的人、尸体、血迹、尖刺）跟着卡片一起立起来。
  同样逐个方法可控，默认值已按穿刺桩实测调好。
- **游戏内入口**：地图视角右下角按钮栏多一个图标，点开就是设置窗口 —— 可拖动、可拉伸、不暂停游戏、记住位置。
  选项菜单里的「模组设置」也能进，内容一样。
- **内置模型包**：原先单独安装的 MyModelPack 已并入本 mod，不再需要额外安装；用 IsoRim 的模型绑定器重新导出后，
  下次启动会自动同步进来。

## 依赖

- **Harmony**（brrainz.harmony）
- **IsoRim Waylith**（Peren.IsoRim17）

建议在 loadOrder 里排在 IsoRim 之后。

## 已知问题

- 装有 Show Me Your Hands 时，开始射击后旋转视角小手位置不对（暂不修，基本无伤大雅）。
- 更多绮罗勇者的法杖会以非常滑稽的形式拿在手里。
- 跨楼层（IsoRimZ）：上层楼的附加部件位置仍在修正中。

## 兼容性

已确认可处理的：Show Me Your Hands、NewRatkinPlus、Epona Dynastic Rise、Miho、SQUISHY Ranged、
Isekai RPG Leveling（飞升光环）、Isekai Dungeons（神性光环）、CeleTech、VEF 护盾等。

其它 mod 的贴图还粘在地上？打开设置里的「自动检测其它 mod 的自绘部件」，装备一次那个物品/站在那里，
回到设置勾上新出现的「发现的绘制方法」，然后可以把检测关掉（省性能）。

## 致谢

- **IsoRim Waylith**（Peren）—— 卡片渲染与 API
- **Visceral Anatomy - Gore & Regeneration**（TanyaVonHart）—— 穿刺桩适配的参考对象
- 以及提出问题的各位玩家

## 许可

代码与文档：见 LICENSE（如未提供，保留所有权利）。
`Models/` 里的模型由作者用 IsoRim 模型绑定器制作，不随本仓库分发。
