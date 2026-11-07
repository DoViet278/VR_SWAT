# Hệ thống Hand Pose

Dáng tay tạo sẵn cho từng vật cầm được, kiểu GunWorld VR: bàn tay ôm đúng grip với ngón tay
được tạo dáng riêng cho từng khẩu súng, ngón trỏ vẫn nhấp nhả theo cò trong lúc cầm.

## Các thành phần

| File | Vai trò |
|---|---|
| `Assets/Scripts/Hands/HandJointId.cs` | Thứ tự cố định của 16 khớp. **Không đổi thứ tự** — mọi pose đã lưu sẽ lệch khớp. |
| `Assets/Scripts/Hands/HandPose.cs` | ScriptableObject chứa dáng: 16 góc xoay + dáng ngón trỏ lúc bóp cò. |
| `Assets/Scripts/Hands/HandSkeleton.cs` | Dò và nhớ 16 xương trên model tay. Gắn lên gốc model tay. |
| `Assets/Scripts/Hands/HandPoseDriver.cs` | Ghi dáng lên xương trong `LateUpdate`, blend vào/ra. Gắn cạnh `HandSkeleton`. |
| `Assets/Scripts/Hands/HandPoseProvider.cs` | Gắn lên vật cầm được; nghe select và bảo driver áp dáng. |
| `Assets/Scripts/Hands/HandPosedGrabInteractable.cs` | `XRGrabInteractable` chọn điểm bám theo **tay** thay vì theo thứ tự grab. |
| `Assets/Scripts/Hands/HandPoseAuthoring.cs` | Component tạm, chỉ dùng lúc thiết kế. Gỡ trước khi build. |
| `Assets/Editor/HandPoseTool/HandPoseAuthoringEditor.cs` | Công cụ tạo pose trong Inspector (10 bước). |
| `Assets/Editor/HandPoseTool/HandPoseSetup.cs` | Hai menu thiết lập: gắn component lên hai bàn tay, và đổi interactable của súng. |
| `Assets/Editor/HandPoseTool/HandPoseRigOffsets.cs` | Nhớ độ lệch giữa bàn tay và điểm bám của interactor. Asset đi kèm nằm cùng thư mục. |
| `Assets/Editor/HandPoseTool/HandPoseCurl.cs` | Hai mốc cho slider cong ngón, lấy từ clip `*_hand_default_anim` / `*_hand_fist_anim`. |
| `Assets/Editor/HandPoseTool/HandPoseBindPoseCheck.cs` | Kiểm tra giả định của phép lật gương. |

## Thiết lập lần đầu

### 1. Model tay

Mở `BasicScene`, chạy **Tools → VR SWAT → Setup Hand Pose (gắn component lên hai bàn tay)**.

Tool tự tìm model tay dưới XR Origin (GameObject có Animator + skinned mesh có xương `b_l_` /
`b_r_`), gắn `HandSkeleton` với đúng Handedness, dò 16 xương, rồi gắn `HandPoseDriver`. Cuối cùng
nó in ra một bảng trạng thái: interactor có đặt Handedness chưa, `AnimateHandOnInput` đã nối cò
chưa, vật nào còn dùng `XRGrabInteractable` thường.

Phải chạy trong Editor chứ không sửa file `.unity` được: scene đang mở thì Unity giữ bản trong bộ
nhớ và sẽ ghi đè lúc save. Cùng lý do với `PhysicsHandsSetup`.

Trong scene hiện tại, `AnimateHandOnInput` đã có sẵn và đã nối `triggerValue` / `gripValue`, nhưng
nó nằm trên **GameObject cha** của model tay, không nằm cùng chỗ. `HandPoseDriver` tự tìm lên cha
nên không phải khai báo gì; muốn khai báo riêng thì điền `Trigger Action` trên driver.

### 2. Căn offset tay theo rig (làm một lần)

Mở `BasicScene`, chọn một vật có `HandPoseAuthoring`, bấm **"Căn cả hai tay"**.

Bước này đo xem model tay đứng lệch bao nhiêu so với điểm bám của interactor — trong dự án này
là 90° quanh trục Z. Không căn thì bàn tay xem trước sẽ lệch đúng chừng ấy so với lúc chơi.

Số đo được lưu vào `Assets/Editor/HandPoseTool/HandPoseRigOffsets.asset` (thư mục Editor nên không
vào build), kèm luôn prefab model tay để công cụ dựng bàn tay xem trước mà không cần scene có sẵn
tay.

Yêu cầu: `NearFarInteractor` phải đặt **Handedness** đúng Left/Right (không để None), và model
tay phải đã có `HandSkeleton`. Không đo được lúc đang Play — `PhysicsHands` đã tháo bàn tay ra
khỏi Controller nên số đo vô nghĩa; tool tự chặn.

### 3. Vật cầm được

Với mỗi khẩu súng (`Rifle h`, `Shotgun`, `Pistol`): chọn trong Hierarchy rồi chạy
**Tools → VR SWAT → Đổi sang Hand Posed Grab Interactable (vật đang chọn)**.

Tool chép toàn bộ giá trị đã set sang component mới (`SelectMode`, `MovementType`,
`Attach Ease In Time`, `Throw On Detach`, danh sách grab transformer, event), tắt
**Use Dynamic Attach**, và trỏ lại mọi chỗ trong scene đang tham chiếu component cũ. Undo được.

Phải đi qua một GameObject tạm: `XRGrabInteractable` có `[DisallowMultipleComponent]` nên không
thể để component cũ và mới cùng tồn tại để chép trực tiếp, mà cũng không chép được từ component
đã bị xoá.

Trừ khi trên cùng GameObject có component khác `[RequireComponent]` cái cũ — `GunRecoilController`
chính là ca đó. Unity không cho xoá component đang bị phụ thuộc, nên với mấy vật này tool đổi thẳng
`m_Script` sang class mới: giá trị khớp theo tên field nên giữ nguyên hết, và không có tham chiếu
nào phải trỏ lại vì vẫn đúng instance cũ. Hộp thoại nói rõ nó đã đi đường nào; đi đường này thì
`Use Dynamic Attach` có thể phải tự tay bỏ tick.

Còn lại làm tay: gán `Left Hand Attach` / `Right Hand Attach` — nhưng không cần gán trước, bước
"Dựng bàn tay xem trước" sẽ tự tạo và tự gán nếu còn trống.

Với `RifleMagazine`, `ShotgunShell`, `VRGrenade`: chỉ cần gắn `HandPoseProvider` lên **prefab** —
instance spawn lúc chơi tự có dáng.

## Quy trình tạo một dáng tay

1. Chọn vật trong Hierarchy → **Add Component → VR SWAT → Hand Pose Authoring**.
2. Chọn **Tay**, tick **Là tay đỡ** nếu đây là dáng cho bàn tay thứ hai.
3. Bấm **Tạo pose asset mới** (lưu vào `Assets/Hands/Poses/`).
4. Bấm **Dựng bàn tay xem trước**. Một bàn tay hiện ra, gắn vào điểm bám.
5. **Di chuyển / xoay điểm bám** để đưa cả bàn tay vào đúng chỗ cầm.
   Đây là chỗ then chốt: điểm bám chính là thứ lúc chơi súng sẽ snap vào, nên khi bàn tay
   trông đúng thì điểm bám cũng đã đúng. Có nút **Chọn điểm bám trong Hierarchy** cho nhanh.
6. Dùng **slider cong ngón** để ra dáng thô, rồi chọn từng xương trong Hierarchy và xoay
   để chỉnh chi tiết. Kéo slider ghi lại cả ngón đó, nên kéo slider **trước** rồi chỉnh tay sau.
7. Bấm **Lưu dáng vào asset**.
8. Cong ngón trỏ vào sát cò → bấm **Ghi dáng ngón trỏ lúc bóp cò**.
9. Bấm **Gán pose này vào HandPoseProvider của vật** (tự thêm `HandPoseProvider` nếu chưa có).
10. Bấm **Xoá bàn tay xem trước rồi gỡ component này**.

Tay còn lại: tạo pose mới, bấm **Lật gương từ pose tay kia**, rồi chỉnh lại cho khớp.
Phép lật là chép thẳng góc xoay (hai model tay dùng chung bind pose); công cụ tự kiểm tra
giả định đó và cảnh báo nếu sai. Có thể chạy tay qua **Tools → VR SWAT → Kiểm tra bind pose hai tay**.

Bàn tay xem trước mang `HideFlags.DontSaveInEditor`, nên quên xoá cũng không bị lưu vào scene.

### Slider cong ngón lấy dáng nắm từ đâu

Hai mốc của slider là clip animation của chính model tay: `l_hand_default_anim` cho đầu mở và
`l_hand_fist_anim` cho đầu nắm (tương tự `r_`). Dáng nắm do người làm model nặn ra nên mỗi khớp
quay quanh đúng trục của nó.

Không tìm thấy clip (đổi sang model tay khác) thì công cụ suy trục gập từ hình học: trục vuông góc
với thân xương và với pháp tuyến lòng bàn tay, chiều được thử cả hai rồi chọn chiều nào đưa đầu
ngón lại gần lòng bàn tay hơn. Thô hơn, và Inspector sẽ nói rõ là đang ở đường dự phòng.

## Những chỗ cần biết

**Tay thứ hai trên súng hai tay chưa bám vào foregrip.**
`XRGeneralGrabTransformer` không hề đọc điểm bám phụ của vật — nó neo súng vào tay cầm chính
rồi chỉ *hướng* súng theo tay thứ hai. Nghĩa là dáng tay trái sẽ đúng về hình, nhưng bàn tay
vẫn nằm ở vị trí controller thật, trôi dọc nòng tuỳ người chơi để tay ở đâu. Muốn tay dính vào
foregrip phải thêm một component kéo riêng phần *hiển thị* của bàn tay về điểm bám — chưa làm.

Có một cái bẫy liên quan đã được xử lý: XRI bật dynamic attach cho tay thứ hai **bất kể**
Use Dynamic Attach có tắt hay không, miễn là `secondaryAttachTransform` còn trống
(`XRGrabInteractable.GetAttachTransform`). Nên `HandPosedGrabInteractable` luôn đặt sẵn điểm bám
của bên tay còn trống vào `secondaryAttachTransform`, và xếp lại hai slot mỗi lần danh sách tay
đang cầm thay đổi (kể cả khi tay chính nhả ra và tay còn lại lên làm tay chính).

**Collider ngón tay bị tắt trong lúc áp dáng.**
Capsule collider nằm thẳng trên xương ngón, nên khi ngón quặp quanh báng chúng lún vào thân
súng mỗi frame; súng là kinematic lúc cầm nên chưa thấy gì, nhưng vừa thả là nó thành dynamic
trong lúc còn lún sâu và bị hất bay. `HandPoseDriver` tắt chúng khi đang pose và bật lại sau
khi thả 0.2s (`Disable Colliders While Posed`).

Chỉ tắt **16 collider trên xương ngón**, không tắt 8 collider lòng bàn tay: 8 cái đó là phần
thân thể của bàn tay, `PhysicsHands` dựa vào chúng để tay bị tường chặn lại. Tắt hết 24 cái là
bàn tay xuyên qua mọi thứ suốt thời gian đang cầm súng.

Và vì `Physics.IgnoreCollision` **không bền** — Unity xoá cờ của một collider ngay khi collider
đó bị disable rồi enable — driver gọi `PhysicsHands.RefreshSelfCollisionIgnores()` sau khi bật
lại. Không gọi thì ngay sau lần cầm súng đầu tiên, ngón tay bắt đầu đập vào CharacterController
của chính người chơi và đập vào bàn tay kia. (`HeldItemCollisionFilter` tự quét lại 0.5s/lần
trong lúc đang cầm nên phía vật đang cầm không cần làm gì thêm.)

**Animator bị ép sang `AlwaysAnimate`.**
Prefab tay để `CullUpdateTransforms`; khi mesh bị cull, Animator ngừng ghi xương nên driver
sẽ đọc lại chính output của mình ở frame trước, thành vòng lặp khiến tay kẹt dáng vĩnh viễn.

**Recoil đẩy súng khỏi tay.**
`GunRecoilController` dịch chuyển súng so với tay khi bắn. Với dáng tay cố định, cú giật sẽ
đọc thành "súng trượt trong tay". Biên độ hiện tại (≤2.5cm) gần như không thấy; nếu khó chịu
thì chuyển cú giật sang một GameObject con chỉ chứa phần hiển thị.

**Hai bộ xương trùng tên trên model tay.**
Model tay Oculus có bộ da `hands:b_l_index1` và, treo ngay dưới mỗi xương đó, một
`coll_hands:b_l_index1` chỉ để chứa capsule collider. Hai bộ cùng đuôi tên, gán sai là tạo dáng
cho bộ collider còn mesh không nhúc nhích. `HandSkeleton.Resolve` loại bộ `coll_`, loại đầu mút
`*_ignore`, và ưu tiên xương nằm trong danh sách `bones` của SkinnedMeshRenderer.
