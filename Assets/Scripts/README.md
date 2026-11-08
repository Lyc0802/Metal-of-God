# 程式閱讀指引

這裡整理專案自寫程式的入口與分工。`Assets/Scripts` 是遊戲邏輯，
`Assets/Outline` 是描邊渲染，`Assets/assassin-character-model/Rigging` 是角色動畫及 Editor 製作工具。
Unity 套件、TextMesh Pro、教學範例與第三方資產不列入這套程式整理。

## 建議閱讀順序

| 想了解的功能 | 先看哪個腳本 | 分工 |
| --- | --- | --- |
| 玩家操作與鏡頭 | [PlayerController](Player/PlayerController.cs) | 輸入、UI 阻擋、選取、鎖定與鏡頭協調 |
| 移動與物理 | [PlayerMovement](Player/PlayerMovement.cs) | FixedUpdate 中的速度、跳躍、滑牆與慣性 |
| 揮劍與命中 | [PlayerCombat](Combat/PlayerCombat.cs) | 攻擊時序、有效命中窗口、傷害與擊退 |
| 角色動畫 | [PlayerCharacterAnimation](Player/PlayerCharacterAnimation.cs) | 讀取真實速度與攻擊進度，驅動 Animator |
| 金屬庫存 | [PlayerMetalInventory](Player/PlayerMetalInventory.cs) | MetalType、MetalReserve、開關與存量查詢 |
| 目標選取 | [PlayerMetalSelector](Player/PlayerMetalSelector.cs) | 射線遮擋、作用點及玩家中心的距離判定 |
| Iron / Steel | [PlayerMetalManipulator](Player/PlayerMetalManipulator.cs) | 推拉、重量差及物理控制交接 |
| Zinc / Brass 輸入 | [PlayerEnemyManipulator](Player/PlayerEnemyManipulator.cs) | 把玩家輸入傳給敵人 Action API |
| 敵人共用資料 | [Enemy](Combat/Enemy.cs) | 生命、金屬狀態、偵測來源及 Inspector 反應事件 |
| 敵人 AI | [TestEnemy](Combat/TestEnemy.cs) | 巡邏、追擊、攻擊、擊退及受金屬影響的行為 |
| Copper / Bronze | [共用狀態](Metals/IMetalAbilityStatus.cs)、[能力說明](Metals/CopperBronze.md) | 訊號登錄、範圍遮蔽、偵測與 X-Ray Outline |
| 一般金屬描邊 | [MetalDisplayer](Metals/MetalDisplayer.cs) | 顯示、選取與重量顏色 |
| Outline 渲染 | [OutlineRendererFeature](../Outline/OutlineRendererFeature.cs) | 一般描邊與 Bronze 穿牆描邊的獨立 Pass |
| Tin / Pewter | [PlayerTinAbility](Player/PlayerTinAbility.cs)、[PlayerPewterAbility](Player/PlayerPewterAbility.cs) | 感知設定與共用能力倍率 |
| 音效與特效 | [PlayerActionEffects](Player/PlayerActionEffects.cs)、[SwordSwingTrail](Player/SwordSwingTrail.cs)、[SoundEmitter](Combat/SoundEmitter.cs) | 消費遊戲狀態與事件，呈現聲音及視覺效果 |

## 一幀的主要流程

1. `PlayerController.Update` 先處理滑鼠捕捉與 UI，再讀取鎖定、視角、移動和能力輸入。
2. 選取使用更新後的鏡頭；有效能力輸入交給推拉或敵人操控元件處理。
3. `PlayerMovement.FixedUpdate` 更新接地與水平速度，最後處理面向及跳躍。
4. `PlayerCombat` 的攻擊協程掌握命中窗口；動畫讀取同一份 `SwingProgress`。
5. `PlayerCharacterAnimation` 只改視覺模型，`LateUpdate` 讓劍刃特效看到最終手部姿勢。
6. `BronzeDetection` 更新訊號與 Outline；`TestEnemy` 透過偵測 API 決定自己的 AI 狀態。

## 修改敵人受到 Zinc / Brass 影響時的反應

在敵人的 `Enemy → Metal Reactions` 裡，使用 `On Zinc Affected` 或 `On Brass Affected`：

1. 按 `+`，拖入掛有反應腳本的 GameObject。
2. 選擇對應的 `public void` 方法，例如 `TestEnemy.OnZinc()`。
3. 替換預設項目可以更換行為，新增項目則會依順序執行多個反應。

現有 TestEnemy Prefab 已接好預設事件。程式動態註冊仍可使用
`Enemy.RegisterAction` / `UnregisterAction`；它們會與 Inspector 事件一起執行。
`TryExecuteAction` 是玩家端共用入口，空白或停用的 Inspector 事件不會單獨視為成功施放。
效果的持續時間與恢復方式由個別敵人 AI 處理。

## 維護原則

- 使用四個空白縮排、大括號獨立一行，一行一個敘述；`.editorconfig` 保存共用格式。
- 名稱要表達資料用途。小型運算可用表達式；多階段計算使用具名變數或私有方法。
- 註解說明順序、限制及原因，例如物理控制交接與偵測遮蔽，避免逐行翻譯程式。
- 公開方法可能被其他腳本或 UnityEvent 使用；序列化欄位可能被場景、Prefab 或動畫引用。
  改名需一併處理相依引用，欄位改名時視需要使用 `FormerlySerializedAs`。
- `MetalType` 的順序對應已儲存的庫存資料，新增種類時要維持既有數值。
- 移動、戰鬥時序、動畫與特效分別由自己的元件維護，避免在視覺元件裡改寫物理速度。
- Copper 遮蔽只影響 Bronze 的能力訊號；普通視線及戰鬥仍使用 `CombatSight`。

## 驗證入口

- `Tools → Metal Abilities → Run Copper Bronze Checks`：實際 Play Mode 的 Outline、
  Copper/Bronze、AI、戰鬥及 Inspector 事件檢查；報告在 `Temp/CopperBronze/results.txt`。
- Editor 工具 `PlayerAnimationVerification.VerifyAssets()`：動畫曲線、循環接縫、骨架及步行/跑步差異。
- `PlayerAnimationVerification.BeginPlaySmoke()`：玩家動畫、輸入、戰鬥、速度混合與 FOV 整合檢查。
  影像輸出使用另外的入口，執行這些驗證不需要產生動態預覽。
- 編譯或程式修改後，確認 Unity Console 沒有新增錯誤，再進入 Play Mode。
