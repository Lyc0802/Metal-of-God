# Player and metals

ShaderTest is configured with all six player components, existing InputSystem_Actions and existing Camera (third-person offset 0,2,-4). RMB + mouse movement aims the camera. Tab holds the existing metal outline. RMB + scroll up pushes; down pulls. Release unlocks without changing velocity. No metal is automatically consumed.

Inspector defaults: movement speed 6, rotation 540 deg/s, jump velocity change 6, acceleration 20; player Weight 80, push/pull 25, Acceleration force mode, adhesion distance 1.5 and acceleration 30; selection distance 30 (existing MetalDisplayer registration range still limits availability); ring radius .15, offset .012. SelectionMask filters targets but all solid layers still occlude selection. GroundMask defaults to all layers, ignoring self and triggers.

MetalDisplayer remains the authoritative metal component and its existing weight is used. Cube has a dynamic Rigidbody. To configure a heavy wall, add Collider, MetalDisplayer and a kinematic Rigidbody; set MetalDisplayer.weight above 80. A child collider finds MetalDisplayer on its ancestors. Do not add a second weight component. RendererFeature is unchanged.

Movement adds acceleration only when input requests more speed in that direction; idle input does not brake momentum. Adjust Rigidbody damping/friction to taste. Adhesion cancels gravity only during active heavy-target Iron Pull within the surface-normal distance threshold. It does not zero velocity or damp preexisting tangential motion.

## Verification (Unity Play Mode, scripted component calls with Physics.Simulate)
- Inventory cap and consumption passed.
- Move + grounded jump: velocity (0,5.80,.40), grounded true.
- Camera center metal selection passed; solid nonmetal obstacle blocked it.
- Surface circle on cube passed.
- Lock retained mode and local point after reverse scroll.
- Light-metal push velocity (0,-.22,.50).
- Release preserved velocity exactly.
- Equal weights produced no metal acceleration.
- Heavy kinematic target Iron Pull near surface: player velocity (0,0,1.10), gravity canceled.
- Compilation and runtime console: no script errors at verification.

These are component/physics smoke tests, not automated physical mouse input or a long-duration wall stability test. Manually check RMB/scroll/Tab in the focused Game view, long-duration wall attachment, edge rejection and moving/rotating targets. Scene test changes were discarded on leaving Play mode; no test wall was persisted.
