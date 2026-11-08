# Combat particle and sound feedback

已整合並儲存至 `Assets/Scenes/MetalPushPullTest.unity`。場景中的 `CombatVFXManager` 已指定三個 Prefab 與三個 AudioClip，不需要再手動連接 References。

## 互動事件

| 效果 | 實際觸發來源 | 防止誤觸 |
| --- | --- | --- |
| MetalImpactVFX | PlayerMetalManipulator 實際驅動 MetalDisplayer 的 Rigidbody，隨後 MetalImpactFeedback.OnCollisionEnter 撞到存活 Enemy | 預設最低相對速度 2.5 m/s；每敵人 0.3 秒冷卻；不處理 CollisionStay；未操作金屬不觸發 |
| SwordHitVFX | PlayerCombat 原有攻擊範圍／角度／視線判定通過，TakeDamage 後 CurrentHealth 確實下降 | 沿用每次揮劍的 hitEnemies 去重；揮空、零傷害不播放 |
| EmotionalInfluenceVFX | PlayerEnemyManipulator 通過 Inventory／距離／每次按住只執行一次等條件，且 Enemy.TryExecuteAction 回傳成功 | 無註冊 Action、關閉／耗盡金屬、停用 AI、AI 拒絕 Action 均不播放 |

專案沒有獨立 MetalObject 類別；現有金屬物件由 MetalDisplayer 與 MetalSurface 表示。本次未新增金屬碰撞傷害系統。金屬放開後仍保留玩家操作資格；速度低於 0.2 m/s 且持續靜止 0.75 秒後清除，避免長久靜止後的普通碰撞被誤認。

## 外觀與聲音

- Metal：橘黃放射火花、少量灰色碎屑、接觸面向外擴大的衝擊波，最長約 0.5 秒。依撞擊速度改變大小、音量與 Pitch。
- Sword：白色斬光、紅色放射粒子、短斬擊殘影，最長約 0.32 秒。
- Emotion：擴散能量環與向上漂浮的光點。Zinc 紅橘、速度 1.2、Pitch 1.12；Brass 青藍、速度 0.85、Pitch 0.88；粒子約 0.58–0.82 秒。AI 持續時間由 TestEnemy 原有欄位決定，與粒子及右鍵放開無關。
- 所有材質均使用專案原有 URP Particles/Unlit 材質為基礎，沒有更動 Renderer Features。
- 所有聲音都是本專案原創數學合成，沒有外部錄音、素材或授權依賴，可以隨專案使用、修改與散布。

音檔：`Audio/MetalImpact.wav`（0.65 秒低沉金屬模態撞擊）、`Audio/SwordHit.wav`（0.36 秒清脆高頻劍擊）、`Audio/EmotionalInfluence.wav`（0.9 秒精神脈衝）。均為 mono、44.1 kHz、16-bit PCM。可用 `Assets/Scripts/Combat/Editor/synthesize_combat_audio.py` 重新產生。

CombatVFXManager 在世界座標建立短暫 Prefab，使用 3D AudioSource.PlayOneShot；聲音也接入既有 SoundEmitter，讓 Tin 的聽覺感知維持正常。粒子與音效共用事件與位置。所有粒子及音效結束後自動 Destroy，並不在 Enemy 或 Player 底下永久累積。

## Inspector

- 場景 `CombatVFXManager`：每種效果的 Prefab、Clip、Volume、Pitch、Max Distance、Size；ZincColor、BrassColor、ZincSpeed、BrassSpeed、ZincPitch、BrassPitch。
- 各 Prefab 子 ParticleSystem：Main 的 Start Color／Size／Speed／Lifetime；Emission 的 Burst Count；Shape、Color over Lifetime、Size over Lifetime 可獨立調整。Emotional 的 Start Color 會在播放時由 Manager 的 Zinc／Brass 顏色覆寫。
- 金屬 Rigidbody 上 `MetalImpactFeedback`：Minimum Impact Speed、Per Enemy Cooldown、Rest Reset Delay。測試場景可移動的金屬已加入；未來新金屬在第一次被玩家真正推拉時也會自動加入此元件。
- 原有 Enemy 的 On Zinc Affected／On Brass Affected 仍然保留 TestEnemy.OnZinc／OnBrass 的 Inspector 註冊。沒有加入 VFX UnityEvent listener，避免把單純呼叫事件誤認成成功。

原有 `RegisterAction(Action)` 與 Inspector void 方法保持相容。可能拒絕執行的自訂 Action 必須在回呼內呼叫 `enemy.ReportActionResult(false)`，成功時呼叫 `enemy.ReportActionResult(true)`。TestEnemy 已整合這個結果契約；舊式不回報結果的 void Action 維持「有效方法已執行代表成功」的原有語意。VFX 不讀取或修改 AI State。

## 驗證與重跑

Unity 選單：`Tools > Combat Feedback > Run Interaction Checks`。測試會進入 Play Mode，在現有測試場景建立暫時的測試 Player／Enemy／金屬，實際呼叫原有 Sword Attack、Iron／Steel 輸入、PhysX 碰撞及 Inspector Zinc／Brass Action。結束後退出 Play Mode，測試用物件與修改不會儲存到場景。

詳細結果寫入 `Temp/CombatFeedback/results.txt`；交付的結果副本在 `Documentation/CombatFeedback/VerificationResults.txt`。外觀預覽在同目錄的 `Preview.png`。可使用 `Tools > Combat Feedback > Render Previews` 重新產生各組快照。

`Tools > Combat Feedback > Build and Integrate` 可重新建立預設材質、Prefab 與場景 References；這會重設特效的預設外觀，已自行調整 Prefab 後請勿任意重建。

已自動檢查粒子實際發射、AudioSource.isPlaying 與 3D 設定、有效／無效互動、去重、冷卻及自動清理。音色與喇叭／耳機上的最終主觀音量仍建議由人實際試玩確認；目前沒有缺少的必要資源或未指定 Reference。

本次實測：Combat Feedback 44 項檢查全部通過；既有 Copper／Bronze／Action／戰鬥回歸檢查 45 項全部通過（結果存於 `Documentation/CombatFeedback/CopperBronzeRegressionResults.txt`）。Unity Console 無 Compilation Errors。完成後已退出 Play Mode，測試場景已儲存且沒有未儲存變更。
