Import Post processing stack v2 to project

----------------------------------

Quick start #1:
1) Open Knife.Player Controller/Scenes/SampleScene
2) Press playmode button

Quick start #2:
1) Open Knife.Player Controller/Scenes/Test
2) Press playmode button

Quick start #3:
1) Create new scene and delete default Main Camera
2) Create GameObject/3D Object/Plane
3) Setup plane Transform to zero position and rotation, scale to one
4) Drag Knife.Player Controller/Prefabs/Player and drop to scene view on plane
5) Drag:
	- Knife.Player Controller/Prefabs/Pistol_Pickupable
	- Knife.Player Controller/Prefabs/Pistol_Ammo_Pickuapble
	and drop to scene view on plane
	Also you can drag and drop other pickupable prefabs
6) Press playmode button
7) Now you can move, pickup weapons and shoot, but without UI and without upgrades
8) Disable playmode
9) Drag and drop Knife.Player Controller/Prefabs/UICanvas to hierarchy
10) Create GameObject/UI/Event System
All scripts in UICanvas will automatically find all need references to player.
But if you need, you can setup it manually, and there will no automatically overwrite
11) Press playmode button
Now you have all UI interfaces

----------------------------------

How works our asset.
There are PlayerController, that has 3 main pivots.
1 - Root PlayerController
2 - Neck Pivot
3 - Hands Pivot

----------------------------------

PlayerController is responsible for player movement, freeze (for pause, upgrade etc.) etc.

Look - options for mouse look
	Enabled - enabled or disabled
	X Sensivity and Y Sensivity - look sensivity of mouse by each axis
	Sensivity Multiplier - common sensivity mulitplier, controlled by other scripts, for example from rifle, to decrease sensivity in aiming through scopes
	Minimum X and Maximum X - X Axis camera rotation limits
	Smooth Time - rotation smooth time
	Clamp Vertical Rotation - clamping X axis camera rotation
	Axis X Name and Axis Y Name - axes in in Input Settings of mouse movement

Head Bob - head bob effect for camera when player moving
	Enabled - enabled or disabled
	Head Bob Weight - weight of headbob effect, controlled by PlayerController, when walking or running
	Head Bob Amount - max camera delta vector
	Head Bob Period - period of head bob effect
	Head Bob Curve X and Head Bob Curve Y - curves by each axis
Head bob blend curve - head bob blend curve

Forward and Strafe axis - axes for movement in Input Settings
Direction Reference - forward and strafe movings based upon this Transform forward axis
Crouch speed multiplier - player speed when crouch based on this multiplier
Run speed multiplier - player speed when running base on this multiplier
Run Increase Speed Time - maximum of run speed time
Run Speed Threshold - that parameter used in checks for speed when player running or walking
Run Increase Speed Curve - curve that changes speed when player running (for smooth acceleration)
Near blur sphere - that blur sphere is used for background creating with hands blur, used in game control info
Speed - default maximum speed of player
Ground Layer - layermask of ground for raycasting
Threshold - distance from ground for raycasting
Gravity - player gravity
Stick to ground - stick to ground force
Stand State and Crouch State - states for blend beetwen stand and crouch positions for camera and colliders
State change speed - stand and crouch speed change
State change curve - stand and crouch speed change curve
Max speed - maximum speed of player, used for headbob blending only
Weight smooth - speed of headbob blending
Jump speed - adding Y velocity on jump
Camera noise - reference to camera noise component
Idle noise - camera noise power in idle
Run noise - camera noise power in running
Player camera - reference to player camera
Control camera - rotating pivot (default is neck)
Hands head bob target - head bob target (default is hands)
Camera headbob weight - weight of camera headbob
Hands headbob weight - weight of hands headbob
Hands headbob multiplier - multiplier of hands headbob
Hands - reference to player hands component
PP controller - post processing controller (for scripting animation)
Default Dof Settings - default PP dof settings

----------------------------------

Player damage handler is responsible for damage react on player
Camera Damage animations - animations for camera when player damaged
Camera layer - Camera layer in animator
Player death animation - player death animation name
Hands animator - reference to player hands animator component
Hands - reference to player hands component
Controller - reference to playercontroller component
Default camera animation translater - reference to default camera transform translater component
Death camera animation translater - reference to death camera transform translater component
Camera state setupper - reference camera transform state setupper component

----------------------------------

Player health is responsible for player damage and die events
Invulnerable - is that health object is invulnerable?
Health - start and max health
Player grenade Damage Mul - damage multiplier from explosions

----------------------------------

Player/Neck/CameraPivot/MainCamera has different scripts.

Transform state setupper can save transform position and rotation and setup them when you need (for example after ressurection)
You can select what you need save by checking Use Position and Use Rotation
For save state on current transform, you need click by right mouse button on Transform State Setupper in inspector (or click on gear) and select save current state
For load state by script you need call LoadSavedState method
Also that component can calculate velocity of object, for that you need check Calculate Velocity
Velocity will be available by properties Velocity and AngularVelocity

----------------------------------

Transform Translater can translate other Transform positions and rotations directly or additively.
On camera that script responsible to translate transformations from empty object in hands pivot to animate camera from hands animator

----------------------------------

Transform Noise - can create noise of transforms and apply (if you need you can disable autoapply and apply manually from other script)
Noise Amount - noise amount of transform
Update Fixed Update - update noise in Update or in FixedUpdate
Position noises and rotation noises - different noises for calculating final noise

----------------------------------

Noise Applier - can apply one noise on different transforms additively or directly, and can be apply it in Update or in FixedUpdate
Has same parameter like Transform Noise - Update Fixed Update
Additive - apply noise directly or additively
Targets - noise targets

----------------------------------

Player Action is responsible for player interaction with world (action or pickupable object)
Raycast start point - raycast origin for check that we can action with something
Layer - layer for raycasting
Action distance - length of raycast
Player Inventory - reference to Player Inventory component

----------------------------------

Screen Shooter - press F10 to screenshot, it saves to Project Folder (not Assets)

----------------------------------

Player/Neck/HandsPivot/Hands has different scripts and hands animator that contains all animations.

PlayerHands is responsible for common hands control.
How it works.
PlayerHands component contains different hands serialized objects. These objects has each file for each class.
KnifeHands, PistolHands, ShotgunHands, RifleHands, GrenadeHands
All of them contains some settings.

Same settings:

Deploy Hide Key - key for deploy or hide that hands
Lock Control On Deploy - if enabled, control for weapon switching will be disabled while deploying
Deploy Parameter - animator parameter name that responsible for that hands is deployed
Hands Item - current hands item in inventory (creates and overwrite in hands script)
Deploy Anim - hands deploy animation name
Hide Anim - hands hide animation name
Hide cross fade - hide cross fade time for smooth transition beetwen previous animation

----------------------------------

Pistol settings:
Ammo block:
	- Ammo current - default current ammo, and ingame current ammo
	- Ammo Max Default - default mag max ammo
	- Ammo Max Long - long mag max ammo
	- Ammo Max - current max ammo (controls by other script)
	- Damage amount - damage per hit
Options (all of these controls by other script)
	- Central Aim - enables aiming by central
	- Back reload - enables other reload animation
	- Low Pose In Aiming - enables pose blending in aiming (for camera right positions)
	- Long Reload - enables long ammo reload animation
	- Auto - enables auto fire (not controls by other script)
	- Flashlight - enables that Pistol has flashlight
Low pose weight target - low pose aiming weight
Low pose wight blend speed - low pose aiming weight blend speed
Fire Collision Layer - layermask of hittable colliders
Fire point - fire raycast origin
Gun hit decal prefab - default hit decal
Hands lerp - LerpToRoot Component reference
Recoil Curve - Recoil smooth curve
Recoil amoun min and max - min max recoil offset
Recoil fade out time - recoil smooth fade out time
Aim Recoil Multiplier - recoil amount multiplier when aiming
Aiming noise - camera noise amount when aiming
Camera noise - camera noise component reference
Props body - props in hands
Props Collision - Coliision of props
Detach props force - detach props force on death
Detach props direction - detach props force direction on death (forward)

Animations block contains different animations names.
Damage block contains current hands damage animations names.

Common block contains common settings of hands, like muzzleflash(gunshot) prefab, livetime, references to other components and shells options.
Also in pistol that block contains two animator controller, for default aiming and central aiming.

Sounds block contains default pistol fire sound and fire sound with silencer.

Other block contains other options.

----------------------------------

Knife settings contains different animations names, damage settings etc.

----------------------------------

Shotgun and rifle settings:
Has same settings like pistol settings and other.

----------------------------------

Grenade settings contains different animations names and other options.

----------------------------------

Also Player Hands component has other options.
Injector - reference to Injector Controller component

* Mods block contains modifications ids for modify hands options (like central aiming, silencers, flashlights, reloads, fire mods etc.)

Other block has different small options like default fov, run fov, dof smooth speed etc.
Also threre are Sources massive. This is audio sources massive, that we want to use via PlaySFX(string soundName) method calling.

----------------------------------

Also that root (Player/Neck/HandsPivot/Hands) has other scripts:
Weapon Customization - has slots field, that contains all possible modifications slots (you can remove some slots if you don't need that upgrade slot)
Injector Controller - has some events (InjectinoStartEvent and InjectionEndEvent) if you want create some actions on that events calls
Player Inventory

----------------------------------

Blurry refractions shaders
2 elements - shader, and component

Command Buffer Blur Refraction component has two fields:
Blur Size - blur size
Amount - blur iterations
That blur screen copy image with command buffers
You can add that on any object (with renderer) that you want use refraction shader
Script create only one actual blurred screen image

FX/Glass/Stained BumpDistort (no grab) shader has different parameters
Distortion - distortion amount (normal map required)
Tint amount - tint color + texture blending with blurred image (0 - max blur, 1 - max color)
Tint Color - texture of tint
Blur Mask - blur mask texture (when you have 1.0 value in red channel you will have full mask)
Noise mask - noised blur mask texture (when you have 1.0 value in red channel you will have full mask) that sampled by world tiling
World scale - noise mask world tiling scale
Blur mask power - power of blur mask (0 - fully blured if tint amount is zero)
Normal map - normal map texture for refractions

----------------------------------

SphereRaycastCommand
This util can raycast by sphere with JobSystem (for faster raycasting), for example 625 raycasts by sphere.
With it you can raycast with more detail results, than OverlapSphere function, like real frag grenade explosion
How you can use that.

1) You need declare SphereRaycastCommand in your script.
2) Setup all parameters in script, and call Prepare method (only one time, otherwise you will have memory leaks)
3) Before cast call, may be you need update origin of cast
4) You need call cast method and send them callback function, that will be called after finish of raycasts
5) You need call Dispose method after all and in OnDestroy function

You can open Knife.Player Controller/Scenes/RaycastTest scene, run that scene and press Spacebar.
After that you can see gizmos lines (in scene view!) that are hit objects.
Example script - SphereRaycastCommandTest

----------------------------------

Weapon Customization tutorial.
How WeaponCustomization works?
There are:
1) WeaponCustomization main component is responsible for change modifications in slots. And contains all modification slots (excepts overrides)
2) Modifications slots (child objects in hands) is responsible to modifications switching

Modification Slot contains all modifications of that slot.
Each modification contains what need to enabled/disabled or changed.
For example you can enable or disable some renderers or gameobjects, or you can change materials of mesh renderers to other.

Modules:
GameObjects that will be enabled when modification will be attached, and disabled when deattached

ModulesRenderers:
MeshRenderers that will be enabled when modification will be attached, and disabled when deattached

Material Changer:
One material changer, that can change some material of selected renderers.
If material is null, so that material index will not be changed on renderers. So you may change only second material of meshrenderers or change all materials on meshrenderers.

Material Changers:
Array of material changer.
Works same as Material Changer, But in this way you may change on one meshrenderer only second material, and on other meshrenderer you may change third material and etc.

Mod ID:
This is modid, that you can identify in scripts how mod is attached. For example enable some features on weapon hands when %MODID% is attached, or disable when deattached.

Override Slots:
If you select other modification slots in that field, so when that modification will be attached, override slots will be disabled.
For example when you attach picatini to pistol, you can't attach small collimator scope one the bolt.

Giving Slots:
If you select other modification slots in that field, so when that modification will be attached, giving slots will be enabled.
For example when you attach picatini to pistol, you will get possibility attach big collimator scope one the bolt.

Mod Icon:
Icon in UI

Custom Data:
You can attach any data to modification, and when that modification will be attached, you can send that data (for example Light of Flashlight) to weapon hands

How to add your own Modification Slot.
See video tutorial on asset store page or YouTube or use these instuctions.
If you want add new modification to existing Modification Slot you should extend Possible Modifications array and start from 11 paragraph

1) Setup your models or materials in project.
If it is model, set parent of models to weapons or other object that you need attach.
2) Create GameObject/Create Empty
3) Set parent it to weapon and rename to MyModificationSlot
4) Create GameObject/3D Object/Sphere
5) Set parent it to MyModificationSlot and scale to smaller size
6) Add component Modification Slot to MyModificationSlot
7) Set UIPivot field of Modification slot as Sphere that you will created
8) You need decide that can be that modification slot is empty, or not
9) Set Default Mod "-1"
10) Set Possible Modifications size 1
11) Setup Modification fields that described above
If you need switch some meshes, you need setup Modules or ModulesRenderers
If you need switch materials, you need setup Material Changer or Material Changers
If you need add or override other slots, you need setup Override Slots or Giving Slots
12) Setup ModID. It need to be unique of all created modifications. You can open Modifications Editor.
Window/Knife/Modifications Editor
There are you can see all ENABLED modification slots.
And if you will be have wrong id (that already exists in other modification slot) you will take message something like:
"Other modification has equals modificator id. Slot GameObject Name: ModSlotBarrel ModId: 0"
If you not got any message, so you are setup unique ModId.

If you can enable some features in weapons (for example central aiming or aiming via scopes) you need:
1) Go to PlayerHands script
2) Define modifications IDs that will enable features
For example:
/*
	public int MyWeaponCentralAimingModID = 10;
*/
3) Find modificationEnd() function
4) Disable all weapon features
For example:
/*
	myWeapon.EnableCentralAiming(false);
	myWeapon.EnableFeature1(false);
	myWeapon.EnableFeature2(false);
	// and etc...
*/

5) Create loop by all attached modifications and enable features
For example:
/*
	foreach (Modificator m in Customization.AttachedModificators)
    {
		if(m.ModId == MyWeaponCentralAimingModID)
		{
			myWeapon.EnableCentralAiming(true);
		}
		// and etc...
	}
*/

6) Create functions in weapon hands
For example:
/*
	private bool isCentralAiming = false;

	public void EnableCentralAiming(bool value)
	{
		isCentralAiming = value;
	}
*/

7) Use this features in weapon hands script
For example:

/*
	private void toAim()
	{
		if(isCentralAiming)
		{
			handsAnimator.Play("CentralAim");
		} else
		{
			handsAnimator.Play("DefaultAim")
		}
	}
*/

How to add your own weapon:
See video tutorial on asset store page or YouTube.