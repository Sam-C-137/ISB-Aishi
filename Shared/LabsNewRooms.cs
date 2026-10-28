using System;
using System.Collections.Generic;
using System.Linq;

#if ISB_LABS_CLIENT
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using EFT;
using EFT.Interactive;
using EFT.UI;
using EFT.InventoryLogic;
using Comfort.Common;
using BepInEx.Logging;
using Newtonsoft.Json.Linq;
using SPT.Common.Http;
using HarmonyLib;
using System.IO;
using System.Collections;
using System.Reflection;
using Audio.SpatialSystem;
#endif

#if ISB_LABS_SERVER
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Helpers.Server;
using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Routers;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Services.InRaid;
using SPTarkov.Server.Core.Servers;
using Microsoft.Extensions.Logging;
using System.Collections;
using System.Text.Json.Nodes;
using HarmonyLib;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Services.Modding.Custom;
using SPTarkov.Server.Core.Utils;
#endif

namespace Aishi.LabsExpansion
{
    public static class LabsNewRoomsNetworkIdentity
    {
        public const int MaximumId=2700;
        public static int Allocate(string identity,IEnumerable<string> planned,IEnumerable<int> nativeIds,IReadOnlyDictionary<string,int> remote=null)
        {
            if(remote!=null&&remote.TryGetValue(identity,out var received))
            {
                if(received<0||received>MaximumId)throw new InvalidOperationException("Labs expansion network files do not match the host.");
                return received;
            }
            var occupied=new HashSet<int>(nativeIds);
            if(remote!=null)foreach(var value in remote.Values)occupied.Add(value);
            int candidate=0;
            foreach(var key in planned.Concat(new[]{identity}).Distinct().OrderBy(k=>k,StringComparer.Ordinal))
            {
                if(remote!=null&&remote.ContainsKey(key))continue;
                while(candidate<=MaximumId&&occupied.Contains(candidate))candidate++;
                if(candidate>MaximumId)throw new InvalidOperationException("No native interactive network IDs remain for the Labs expansion.");
                if(key==identity)return candidate;
                occupied.Add(candidate++);
            }
            throw new InvalidOperationException("Labs interactive identity was not allocated.");
        }
    }

    public static class LabsNewRoomsWindowCoverPolicy
    {
        public const string Scene = "Laboratory_DesignMain";
        public const string Parent = "For_doors (closed)";

        public static bool Matches(string scene, string parent, string name, string renderer, float x, float y, float z)
        {
            if (scene != Scene || parent != Parent || renderer != "lab_poster_A_3_LOD0")
                return false;
            float expectedX, expectedZ;
            if (name == "lab_poster_A_3 (2)") { expectedX = -113.771f; expectedZ = -429.5102f; }
            else if (name == "lab_poster_A_3 (3)") { expectedX = -115.6f; expectedZ = -439.6026f; }
            else return false;
            float dx = x - expectedX, dy = y - 1.602f, dz = z - expectedZ;
            return dx * dx + dy * dy + dz * dz <= 0.04f;
        }
    }

#if ISB_LABS_CLIENT
    public sealed class LabsNewRoomsRuntime : MonoBehaviour
    {
        private static Harmony harmony;
        private static ManualLogSource log;
        private static string assetFolder;
        private static LabsNewRoomsRuntime active;
        private JObject layout;
        private JObject state;
        private readonly List<GameObject> roots = new List<GameObject>();
        private readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();
        private readonly List<Renderer[]> renderers = new List<Renderer[]>();
        private readonly List<BoundingSphere> spheres = new List<BoundingSphere>();
        private readonly List<Action> unregister = new List<Action>();
        private readonly List<RoomLamp> roomLamps = new List<RoomLamp>();
        private static readonly int LampEmissionColor=Shader.PropertyToID("_EmissionColor"),LampEmissiveColor=Shader.PropertyToID("_EmissiveColor"),LampPower=Shader.PropertyToID("_EmissionPower"),LampVisibility=Shader.PropertyToID("_EmissionVisibility"),LampTint=Shader.PropertyToID("_Color");
        private sealed class RoomLampSurface
        {
            internal Renderer Renderer;
            internal int Slot;
            internal bool Tint;
            internal Color NormalTint;
            internal MaterialPropertyBlock Block=new MaterialPropertyBlock();
        }
        private sealed class RoomLamp
        {
            internal Light Light;
            internal CullingLightObject Culling;
            internal float Intensity;
            internal readonly List<RoomLampSurface> Surfaces=new List<RoomLampSurface>();
        }
        private readonly Dictionary<string, GameObject> models = new Dictionary<string, GameObject>();
        private readonly Dictionary<string, Texture2D> textures = new Dictionary<string, Texture2D>();
        private readonly Dictionary<string,string> nativeStorageTemplates=new Dictionary<string,string>();
        private CullingGroup culling;
        private Mesh board;
        private string boardCode;
        private JToken boardWriting;
        private JObject boardGlyphs;
        private float keypadResultUntil;
        private bool keypadGranted;
        private AudioSource keypadAudio;
        private readonly Dictionary<string,AudioClip> keypadClips=new Dictionary<string,AudioClip>();
        private Door room04;
        private Door room06;
        private float nextState;
        private float nextCamera;
        private bool polling;
        private bool keypad;
        private bool busy;
        private string entry = "";
        private string feedback = "";
        private CursorLockMode cursorLock;
        private bool cursorVisible;
        private Vector3 readerPosition;
        private bool initialState=true;
        private bool controlsPrepared;
        private readonly Dictionary<string,string> failures=new Dictionary<string,string>();
        private readonly List<LabsRoomAmbientArea> ambientAreas=new List<LabsRoomAmbientArea>();
        private bool ambientPrepared;
        private float nextAmbient;
        private float nextAmbientPrepare;
        private float nextControls;
        private KeycardDoor cardReader;
        private KeycardDoor codeReader;
        private bool room06Opened;
        private bool relocking;
        private Vector3 activeKeypadPosition;

        private static Type ReaderPowerType => AccessTools.TypeByName("ISBSSOFExtra.LabsKeycardLockdown");
        private static bool ReaderBlocked(WorldInteractiveObject reader) => reader!=null&&AccessTools.Method(ReaderPowerType,"IsBlocked")?.Invoke(null,new object[]{reader}) is bool blocked&&blocked;
        private static void RegisterPowerReader(KeycardDoor reader) => AccessTools.Method(ReaderPowerType,"RegisterExternalReader")?.Invoke(null,new object[]{reader,true});
        private static void UnregisterPowerReader(KeycardDoor reader) => AccessTools.Method(ReaderPowerType,"UnregisterExternalReader")?.Invoke(null,new object[]{reader});

        public static void Initialize(ManualLogSource logger)
        {
            if(harmony!=null) return;
            log=logger;
            assetFolder=System.IO.Path.Combine(System.IO.Path.GetDirectoryName(typeof(LabsNewRoomsRuntime).Assembly.Location),"MiniLabsAssets");
            harmony=new Harmony("com.samc137.aishi.labs-new-rooms.client");
            harmony.Patch(AccessTools.Method(typeof(GameWorld),nameof(GameWorld.SpawnLoot)),prefix:new HarmonyMethod(typeof(LabsNewRoomsRuntime),nameof(BeforeLoot)) { priority=Priority.First });
            harmony.Patch(AccessTools.Method(typeof(EFT.World),nameof(EFT.World.RegisterNetworkInteractionObjects)),prefix:new HarmonyMethod(typeof(LabsNewRoomsRuntime),nameof(CaptureNetworkInteractions)));
            foreach(var method in typeof(InteractionContextHelper).GetMethods(BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic).Where(m=>m.ReturnType==typeof(AvailableInteractionState)))
            {
                var parameters=method.GetParameters();
                if(parameters.Length>=2&&parameters[0].ParameterType==typeof(GamePlayerOwner)&&new[]{typeof(WorldInteractiveObject),typeof(Door),typeof(KeycardDoor)}.Contains(parameters[1].ParameterType))
                    harmony.Patch(method,prefix:new HarmonyMethod(typeof(LabsNewRoomsRuntime),nameof(DoorActions)){priority=Priority.First});
            }
        }

        private static bool DoorActions(GamePlayerOwner __0,WorldInteractiveObject __1,ref AvailableInteractionState __result)
        {
            if(active==null||__1==null)return true;
            if(__1==active.cardReader||__1==active.codeReader)
            {
                __result=active.ReaderActions(__0,(KeycardDoor)__1);return false;
            }
            if(__1.DoorState!=EDoorState.Locked)return true;
            if(__1!=active.room04&&__1!=active.room06)return true;
            __result=new AvailableInteractionState{Actions=new List<InteractionAction>()};return false;
        }

        public static void Shutdown()
        {
            harmony?.UnpatchSelf();harmony=null;LabsNewRoomsSparks.Reset();
            if(active!=null) UnityEngine.Object.Destroy(active);
        }

        private static void BeforeLoot(GameWorld __instance)
        {
            if(active!=null&&active.gameObject==__instance.gameObject)return;
            if(!LabsNewRoomsIdentity.IsLabs(__instance.LocationId)&&!SceneManager.GetSceneByName("Laboratory_Medical_corridor_floor_1").isLoaded)return;
            try
            {
                var snapshot=JObject.Parse(RequestHandler.GetJson("/Aishi/miniLabs/layout"));
                if(snapshot["layout"]?["enabled"]?.Value<bool>()!=true)return;
                if(active!=null)UnityEngine.Object.DestroyImmediate(active);
                active=__instance.gameObject.AddComponent<LabsNewRoomsRuntime>();
                active.layout=(JObject)snapshot["layout"];
                active.state=snapshot["state"] as JObject;
                active.Prepare();
            }
            catch(Exception ex) {if(active!=null)UnityEngine.Object.DestroyImmediate(active);log?.LogError("[Aishi Labs Expansion] Could not prepare rooms before loot: "+ex);}
        }

        private static Vector3 V(JToken value) => value is JArray a ? new Vector3((float)a[0],(float)a[1],(float)a[2]) : new Vector3((float)(value?["x"]??0),(float)(value?["y"]??0),(float)(value?["z"]??0));
        private static string S(JToken value,string key) => (string)value?[key]??"";
        private static IEnumerable<JToken> A(JToken value,string key) => value?[key] as JArray ?? Enumerable.Empty<JToken>();
        private static string PathOf(Transform node) {string value=node.name;while(node.parent!=null){node=node.parent;value=node.name+"/"+value;}return value;}

        private void Prepare()
        {
            var transforms=Resources.FindObjectsOfTypeAll<Transform>().Where(t=>t.gameObject.scene.IsValid()&&t.gameObject.scene.isLoaded&&!t.name.StartsWith("ISB_LABS_")).ToArray();
            var byPath=transforms.GroupBy(t=>PathOf(t)).ToDictionary(g=>g.Key,g=>g.ToArray(),StringComparer.Ordinal);
            var containerTemplate=Resources.FindObjectsOfTypeAll<LootableContainer>().FirstOrDefault(t=>t.gameObject.scene.IsValid()&&t.Template!=""&&t.GetComponentInChildren<Collider>()!=null);
            foreach(var room in A(layout,"rooms"))
            {
                var scene=SceneManager.GetSceneByName(S(room,"scene").Replace("_MX",""));
                if(!scene.IsValid()||!scene.isLoaded)throw new InvalidDataException("Room scene is not loaded: "+S(room,"scene"));
                foreach(var prop in A(room,"props"))
                {
                    var root=new GameObject("ISB_LABS_"+S(prop,"id"));root.SetActive(false);roots.Add(root);SceneManager.MoveGameObjectToScene(root,scene);
                    var source=prop["source"];
                    Transform native=FindSource(byPath,source);
                    string mode=S(source,"mode");
                    if(mode=="composite")
                    {
                        foreach(var part in A(source,"components"))
                        {
                            var component=FindSource(byPath,part);
                            if(component==null)throw new InvalidDataException("Composite part unavailable: "+S(part,"path"));
                            CloneNative(component,root.transform,source);
                        }
                        var original=FindSource(byPath,source);
                        if(original!=null)foreach(var collider in original.GetComponentsInChildren<Collider>(true).Where(c=>c.enabled&&!c.isTrigger&&c.name.IndexOf("BALLISTIC",StringComparison.OrdinalIgnoreCase)<0))CloneCollider(collider,root.transform,source);
                    }
                    else if(native!=null&&mode!="mesh")CloneNative(native,root.transform,source);
                    else LoadModel(root,S(source,"model"));
                    root.transform.position=V(prop["position"]);
                    root.transform.rotation=prop["rotation"]!=null?Quaternion.Euler(V(prop["rotation"])):Quaternion.Euler(0,(float)(prop["rotationY"]??0),0);
                    foreach(var renderer in root.GetComponentsInChildren<Renderer>(true))
                    {
                        renderer.enabled=true;renderer.forceRenderingOff=false;renderer.allowOcclusionWhenDynamic=true;renderer.lightmapIndex=-1;
                        if(renderer.sharedMaterials.Any(m=>m==null||m.shader==null))throw new InvalidDataException("Missing native material: "+S(prop,"assetId"));
                    }
                    foreach(var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
                    {
                        if(behaviour==null)continue;
                        string type=behaviour.GetType().FullName;
                        if(type.IndexOf("Culling",StringComparison.OrdinalIgnoreCase)>=0||type.IndexOf("GuidComponent",StringComparison.OrdinalIgnoreCase)>=0||behaviour is WorldInteractiveObject&&!(behaviour is LootableContainer))UnityEngine.Object.DestroyImmediate(behaviour);
                    }
                    if(!root.GetComponentsInChildren<Collider>(true).Any(c=>c.enabled))
                        foreach(var filter in root.GetComponentsInChildren<MeshFilter>(true))
                            if(filter.sharedMesh!=null){filter.gameObject.AddComponent<MeshCollider>().sharedMesh=filter.sharedMesh;filter.gameObject.layer=LayerMask.NameToLayer("LowPolyCollider");}
                    var containers=root.GetComponentsInChildren<LootableContainer>(true);
                    if(prop["loot"]!=null)
                    {
                        var original=native?.GetComponentInChildren<LootableContainer>(true)??native?.GetComponentInParent<LootableContainer>()??native?.parent?.GetComponentInChildren<LootableContainer>(true);
                        nativeStorageTemplates[S(prop,"id")]=S(prop,"assetId")=="X_TERMINAL_GEAR_CABINET"?LabsNewRoomsIdentity.TerminalStorage:original?.Template;
                        var container=S(prop,"assetId")=="X_TERMINAL_GEAR_CABINET"?CreateTerminalStorage(root):containers.FirstOrDefault();
                        if(container==null)
                        {
                            if(containerTemplate==null)throw new InvalidDataException("Native loot container unavailable.");
                            var bounds=BoundsOf(root);
                            var interaction=UnityEngine.Object.Instantiate((original??containerTemplate).gameObject,root.transform);
                            interaction.transform.localPosition=Vector3.zero;interaction.transform.localRotation=Quaternion.identity;interaction.transform.localScale=Vector3.one;
                            foreach(var renderer in interaction.GetComponentsInChildren<Renderer>(true))renderer.enabled=false;
                            foreach(var collider in interaction.GetComponentsInChildren<Collider>(true))collider.enabled=false;
                            container=interaction.GetComponent<LootableContainer>();
                            var hit=interaction.AddComponent<BoxCollider>();hit.center=root.transform.InverseTransformPoint(bounds.center);hit.size=bounds.size;interaction.layer=LayerMask.NameToLayer("Interactive");
                            container.Collider=hit;
                            interaction.SetActive(true);
                        }
                        container.gameObject.SetActive(true);container.enabled=true;container.Operatable=true;container.InteractionDot=-1;
                        container.Id=root.name;container.NetId=StableNetId(root.name);container.Template=S(prop,"storageTemplate");if(container.Template=="")container.Template=LabsNewRoomsIdentity.StorageTemplate(S(prop,"assetId"));container.ItemOwner=null;container.IsInitialized=false;container.IsAlwaysLootable=true;container.IsAlwaysSpawn=true;container.SpawnChance=100;
                        container.GameObjectsToDestroy=new List<GameObject>();container.DoorState=EDoorState.Shut;
                        foreach(var extra in containers.Where(c=>c!=container))UnityEngine.Object.DestroyImmediate(extra);
                        RegisterContainer(scene,container);
                    }
                    else foreach(var container in containers)UnityEngine.Object.DestroyImmediate(container);
                    root.SetActive(true);
                    if(S(room["codeBoardBinding"],"propId")==S(prop,"id"))PrepareBoard(root,room["codeBoardBinding"]);
                    var visible=root.GetComponentsInChildren<Renderer>(true).Where(r=>r.enabled).ToArray();
                    if(visible.Length>0){var bounds=BoundsOf(root);renderers.Add(visible);spheres.Add(new BoundingSphere(bounds.center,bounds.extents.magnitude));}
                }
            }
            PrepareDoorDecorations(byPath);
            PrepareCeilingLights(byPath);
            BindDoors();
            culling=new CullingGroup();culling.SetBoundingSpheres(spheres.ToArray());culling.SetBoundingSphereCount(spheres.Count);culling.SetBoundingDistances(new[]{180f});
            culling.onStateChanged=evt=>{bool hide=!evt.isVisible||evt.currentDistance>0;foreach(var renderer in renderers[evt.index])if(renderer!=null)renderer.forceRenderingOff=hide;};
            ApplyState();
        }

        private static Transform FindSource(Dictionary<string,Transform[]> paths,JToken source)
        {
            if(!paths.TryGetValue(S(source,"path"),out var found))return null;
            string scene=S(source,"scene").Replace("_MX","");
            return found.Where(t=>t.gameObject.scene.name==scene||t.gameObject.scene.name==scene+"_MX").OrderBy(t=>(t.position-V(source["position"])).sqrMagnitude).FirstOrDefault();
        }

        private void PrepareDoorDecorations(Dictionary<string,Transform[]> paths)
        {
            foreach(var room in A(layout,"rooms"))foreach(var definition in A(room,"disabledDecals"))
            {
                if(!paths.TryGetValue(S(definition,"path"),out var candidates))continue;
                var decal=candidates.FirstOrDefault(t=>(t.position-V(definition["position"])).sqrMagnitude<.01f&&t.GetComponent<StaticDeferredDecal>()!=null);
                if(decal==null)continue;
                var target=decal.gameObject;bool previous=target.activeSelf;target.SetActive(false);unregister.Add(()=>{if(target!=null)target.SetActive(previous);});
            }
            foreach(var room in A(layout,"rooms"))foreach(var definition in A(room,"disabledDecorations"))
            {
                if(!paths.TryGetValue(S(definition,"path"),out var candidates))continue;
                var node=candidates.FirstOrDefault(t=>(t.position-V(definition["position"])).sqrMagnitude<.01f&&t.GetComponentsInChildren<Renderer>(true).Any(r=>r.sharedMaterials.Any(m=>m!=null&&m.name.StartsWith(S(definition,"material"),StringComparison.Ordinal))));
                if(node==null)continue;
                var target=node.gameObject;bool previous=target.activeSelf;target.SetActive(false);unregister.Add(()=>{if(target!=null)target.SetActive(previous);});
            }
            foreach(var room in A(layout,"rooms").Where(r=>r["removeStaticDoorSign"]?.Value<bool>()==true))
            {
                var door=LocationScene.GetAllObjects<WorldInteractiveObject>(true).OfType<Door>().FirstOrDefault(d=>d.Id==S(room,"doorId"));
                if(door==null)continue;
                var bounds=BoundsOf(door.gameObject);bounds.Expand(new Vector3(.15f,-.35f,.15f));
                foreach(var renderer in paths.Values.SelectMany(v=>v).Select(t=>t.GetComponent<MeshRenderer>()).Where(r=>r!=null&&!r.transform.IsChildOf(door.transform)&&bounds.Contains(r.bounds.center)&&r.sharedMaterials.Any(m=>m!=null&&m.shader!=null&&m.shader.name.StartsWith("Decal/",StringComparison.Ordinal))))
                {
                    var node=renderer.transform;var parent=node.parent;node.SetParent(door.transform,true);unregister.Add(()=>{if(node!=null)node.SetParent(parent,true);});
                }
                foreach(var decal in paths.Values.SelectMany(v=>v).Select(t=>t.GetComponent<StaticDeferredDecal>()).Where(d=>d!=null&&bounds.Contains(d.transform.position)))
                {
                    var target=decal.gameObject;bool previous=target.activeSelf;target.SetActive(false);unregister.Add(()=>{if(target!=null)target.SetActive(previous);});
                }
            }
        }

        private static void CloneNative(Transform source,Transform parent,JToken metadata)
        {
            var clone=UnityEngine.Object.Instantiate(source.gameObject,parent);
            clone.SetActive(true);
            var inverse=Quaternion.Euler(0,-V(metadata["rotation"]).y,0);
            clone.transform.localPosition=inverse*(source.position-V(metadata["position"]))-V(metadata["canonicalPivot"]);
            clone.transform.localRotation=inverse*source.rotation;
            clone.transform.localScale=source.lossyScale;
        }

        private static Bounds BoundsOf(GameObject root)
        {
            var rs=root.GetComponentsInChildren<Renderer>(true).Where(r=>r.enabled).ToArray();var bounds=rs.Length>0?rs[0].bounds:new Bounds(root.transform.position,Vector3.one);
            foreach(var renderer in rs.Skip(1))bounds.Encapsulate(renderer.bounds);return bounds;
        }

        private static void CloneCollider(Collider source,Transform parent,JToken metadata)
        {
            var child=new GameObject("ISB_LABS_Collision_"+source.name);child.transform.SetParent(parent,false);child.layer=LayerMask.NameToLayer("LowPolyCollider");
            var inverse=Quaternion.Euler(0,-V(metadata["rotation"]).y,0);child.transform.localPosition=inverse*(source.transform.position-V(metadata["position"]))-V(metadata["canonicalPivot"]);child.transform.localRotation=inverse*source.transform.rotation;child.transform.localScale=source.transform.lossyScale;
            Collider copy=null;
            if(source is MeshCollider mesh){var target=child.AddComponent<MeshCollider>();target.sharedMesh=mesh.sharedMesh;target.convex=mesh.convex;copy=target;}
            else if(source is BoxCollider box){var target=child.AddComponent<BoxCollider>();target.center=box.center;target.size=box.size;copy=target;}
            else if(source is CapsuleCollider capsule){var target=child.AddComponent<CapsuleCollider>();target.center=capsule.center;target.direction=capsule.direction;target.radius=capsule.radius;target.height=capsule.height;copy=target;}
            else if(source is SphereCollider sphere){var target=child.AddComponent<SphereCollider>();target.center=sphere.center;target.radius=sphere.radius;copy=target;}
            if(copy!=null){copy.sharedMaterial=source.sharedMaterial;copy.contactOffset=source.contactOffset;}
        }

        private sealed class LabsRoomAmbientArea
        {
            internal string Id;
            internal SpatialAudioRoom Room;
            internal BoxCollider Volume;
            internal BoxCollider[] PreviousColliders;
            internal Bounds PreviousBounds;
            private IPlayer occupant;
            internal void Update(Player player)
            {
                bool inside=player!=null&&!player.IsAI&&player.HealthController?.IsAlive==true&&Volume.bounds.Contains(player.PlayerColliderPointOnCenterAxis(.5f));
                if(occupant!=null&&(!inside||!ReferenceEquals(occupant,player)))Exit();
                if(inside&&occupant==null){occupant=player;Room.AudioTriggerAreaEventArgsHandle(this,new AudioTriggerAreaEventArgs{Player=player,TriggerEventType=AudioTriggerAreaEventArgs.ETriggerEventType.TriggerEnter});}
            }
            private void Exit()
            {
                if(Room!=null&&occupant!=null)Room.AudioTriggerAreaEventArgsHandle(this,new AudioTriggerAreaEventArgs{Player=occupant,TriggerEventType=AudioTriggerAreaEventArgs.ETriggerEventType.TriggerExit});occupant=null;
            }
            internal void Restore(){Exit();if(Room!=null){Room.Colliders=PreviousColliders;Room._bounds=PreviousBounds;}}
        }

        private static void RebuildAudioIndex()
        {
            if(!MonoBehaviourSingleton<SpatialAudioSystem>.Instantiated)return;
            var system=MonoBehaviourSingleton<SpatialAudioSystem>.Instance;
            var storage=AccessTools.Field(typeof(SpatialAudioSystem),"_audioRoomStorage").GetValue(system) as AudioRoomStorage;
            if(storage!=null&&storage._validSpatialAudioRooms.Count>0)storage.method_1();
        }

        private void UpdateAmbientRooms()
        {
            if(!ambientPrepared&&Time.unscaledTime>=nextAmbientPrepare)
            {
                nextAmbientPrepare=Time.unscaledTime+3f;
                var definitions=A(layout,"rooms").Where(r=>r["ambientRoom"]!=null).ToArray();
                var system=MonoBehaviourSingleton<SpatialAudioSystem>.Instantiated?MonoBehaviourSingleton<SpatialAudioSystem>.Instance:null;
                var storage=system==null?null:AccessTools.Field(typeof(SpatialAudioSystem),"_audioRoomStorage").GetValue(system) as AudioRoomStorage;
                var native=storage?._validSpatialAudioRooms?.Where(r=>r!=null).ToArray()??new SpatialAudioRoom[0];
                if(native.Length==0)native=Resources.FindObjectsOfTypeAll<SpatialAudioRoom>().Where(r=>r.gameObject.scene.IsValid()&&r.gameObject.scene.isLoaded).ToArray();
                foreach(var definition in definitions)
                {
                    string id=S(definition,"id");if(ambientAreas.Any(a=>a.Id==id))continue;
                    string expectedName=S(definition["ambientRoom"],"nativeName");int expectedId=(int)definition["ambientRoom"]["nativeId"];
                    var room=native.FirstOrDefault(r=>string.Equals(r.name?.Trim(),expectedName,StringComparison.OrdinalIgnoreCase)||string.Equals(r.Name?.Trim(),expectedName,StringComparison.OrdinalIgnoreCase)||r.Name?.Trim().EndsWith("/"+expectedName,StringComparison.OrdinalIgnoreCase)==true)
                        ??native.FirstOrDefault(r=>r.ID==expectedId);
                    if(room==null){ReportFailure("ambient/"+id,new InvalidDataException("Native ambient corridor unavailable: "+expectedName+"; registered rooms="+native.Length));continue;}
                    if(!room.IsInitialized)continue;
                    var root=new GameObject("ISB_LABS_Ambient_"+id);roots.Add(root);SceneManager.MoveGameObjectToScene(root,room.gameObject.scene);
                    var size=V(definition["measurement"]["size"]);root.transform.position=V(definition["measurement"]["origin"])+Vector3.up*size.y*.5f;
                    var box=root.AddComponent<BoxCollider>();box.size=size-new Vector3(.04f,.04f,.04f);box.isTrigger=true;root.layer=LayersMaskController.TriggersLayer;
                    var area=new LabsRoomAmbientArea{Id=id,Room=room,Volume=box,PreviousColliders=room.Colliders,PreviousBounds=room._bounds};ambientAreas.Add(area);
                    room.Colliders=(room.Colliders??new BoxCollider[0]).Concat(new[]{box}).ToArray();var bounds=room._bounds;bounds.Encapsulate(box.bounds);room._bounds=bounds;
                    failures.Remove("ambient/"+id);
                }
                ambientPrepared=ambientAreas.Count==definitions.Length;if(ambientPrepared)failures.Remove("ambient");RebuildAudioIndex();
            }
            foreach(var area in ambientAreas)area.Update(Singleton<GameWorld>.Instance?.MainPlayer);
        }

        private void ReportFailure(string section,Exception error)
        {
            string signature=error.GetType().FullName+":"+error.Message;
            if(failures.TryGetValue(section,out var previous)&&previous==signature)return;
            failures[section]=signature;log?.LogError("[Aishi Labs Expansion] "+section+": "+error);
        }

        private LootableContainer CreateTerminalStorage(GameObject root)
        {
            var metadata=JObject.Parse(File.ReadAllText(System.IO.Path.Combine(assetFolder,"terminal-cabinet-motion.json")));
            var hinge=new GameObject("ISB_LABS_CabinetDoor");hinge.transform.SetParent(root.transform,false);hinge.transform.localPosition=V(metadata["hinge"]);
            var moving=A(metadata,"movingParts").Select(v=>(string)v).ToArray();
            var parts=root.GetComponentsInChildren<Transform>(true).Where(t=>moving.Contains(t.name)).ToArray();
            if(parts.Length!=moving.Length)throw new InvalidDataException("Cabinet door meshes unavailable.");
            foreach(var part in parts)part.SetParent(hinge.transform,true);
            var panel=parts[0].GetComponent<MeshFilter>();var local=panel.sharedMesh.bounds;var matrix=hinge.transform.worldToLocalMatrix*panel.transform.localToWorldMatrix;
            var bounds=new Bounds(matrix.MultiplyPoint3x4(local.center),Vector3.zero);
            for(int i=0;i<8;i++)bounds.Encapsulate(matrix.MultiplyPoint3x4(local.center+Vector3.Scale(local.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1))));
            hinge.layer=LayerMask.NameToLayer("Interactive");var hit=hinge.AddComponent<BoxCollider>();hit.center=bounds.center;hit.size=bounds.size+new Vector3(.015f,.015f,.015f);
            var container=hinge.AddComponent<LootableContainer>();container.Collider=hit;container.DoorAxis=(WorldInteractiveObject.EDoorAxis)(int)metadata["axis"];container.OpenAngle=(float)metadata["openAngle"];container.CloseAngle=(float)metadata["closedAngle"];container.ClosedPosition=container.OpenPosition=hinge.transform.localPosition;
            container._openInteraction=(EInteraction)(int)metadata["openInteraction"];container._closeInteraction=(EInteraction)(int)metadata["closeInteraction"];container.FilterExtended=new List<LootableContainerParameters>();
            container.OpenSound=new AudioClip[0];container.ShutSound=new AudioClip[0];container.SqueakSound=new AudioClip[0];
            var triggers=AccessTools.Field(typeof(WorldInteractiveObject),"TriggersMap");triggers.SetValue(container,System.Array.CreateInstance(triggers.FieldType.GetElementType(),0));
            return container;
        }

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<EFT.World,Dictionary<string,int>> networkInteractions=new System.Runtime.CompilerServices.ConditionalWeakTable<EFT.World,Dictionary<string,int>>();
        private static void CaptureNetworkInteractions(EFT.World __instance,IDictionary<string,int> __0)
        {
            networkInteractions.Remove(__instance);
            if(__0!=null)networkInteractions.Add(__instance,new Dictionary<string,int>(__0));
        }

        private static int StableNetId(string identity)
        {
            var world=Singleton<GameWorld>.Instance?.World;
            Dictionary<string,int> remote=null;
            if(world!=null)networkInteractions.TryGetValue(world,out remote);
            var occupied=LocationScene.GetAllObjects<WorldInteractiveObject>(true).Where(o=>!o.Id?.StartsWith("ISB_LABS_",StringComparison.Ordinal)??true).Select(o=>o.NetId);
            var planned=new List<string>{"ISB_LABS_CardReader","ISB_LABS_CodeKeypad",identity};
            if(active?.layout!=null)planned.AddRange(A(active.layout,"rooms").SelectMany(r=>A(r,"props")).Where(p=>p["loot"]!=null).Select(p=>"ISB_LABS_"+S(p,"id")));
            return LabsNewRoomsNetworkIdentity.Allocate(identity,planned,occupied,remote);
        }

        private static void RegisterDynamicInteraction(WorldInteractiveObject interactive)
        {
            var game=Singleton<GameWorld>.Instance;var world=game?.World;if(world==null)return;
            if(!world.WorldInteractiveObjects().Any(o=>o.Id==interactive.Id))game.RegisterWorldInteractionObject(interactive);
            var field=AccessTools.Field(typeof(EFT.World),"_interactableObjectsForNetSync");
            var current=field.GetValue(world) as WorldInteractiveObject[]??new WorldInteractiveObject[0];
            if(!current.Contains(interactive))field.SetValue(world,current.Concat(new[]{interactive}).ToArray());
            if(world.Interactables is Dictionary<string,int> registered)registered[interactive.Id]=interactive.NetId;
            active?.unregister.Add(()=>
            {
                if(world==null)return;
                var entries=AccessTools.Field(typeof(EFT.World),"_interactiveObjectsDictionary").GetValue(world) as Dictionary<string,WorldInteractiveObject>;
                if(entries!=null&&entries.TryGetValue(interactive.Id,out var existing)&&ReferenceEquals(existing,interactive))entries.Remove(interactive.Id);
                var remaining=field.GetValue(world) as WorldInteractiveObject[];
                if(remaining!=null)field.SetValue(world,remaining.Where(o=>!ReferenceEquals(o,interactive)).ToArray());
                if(world.Interactables is Dictionary<string,int> ids&&ids.TryGetValue(interactive.Id,out var id)&&id==interactive.NetId)ids.Remove(interactive.Id);
            });
        }

        private void RegisterContainer(Scene scene,LootableContainer container)
        {
            var owner=LocationScene.LoadedScenes.FirstOrDefault(s=>s.gameObject.scene==scene);
            if(owner==null)throw new InvalidOperationException("Native scene registry unavailable: "+scene.name);
            var world=Singleton<GameWorld>.Instance;
            RegisterDynamicInteraction(container);
            if(world!=null){container.OnDoorStateChanged+=world.WorldInteractiveObjectOnDoorStateChanged;unregister.Add(()=>{if(container!=null&&world!=null)container.OnDoorStateChanged-=world.WorldInteractiveObjectOnDoorStateChanged;});}
            var oldContainers=owner.LootableContainers;var oldObjects=owner.WorldInteractiveObjects;
            owner.LootableContainers=(oldContainers??new LootableContainer[0]).Concat(new[]{container}).ToArray();
            owner.WorldInteractiveObjects=(oldObjects??new WorldInteractiveObject[0]).Concat(new WorldInteractiveObject[]{container}).ToArray();
            unregister.Add(()=>{if(owner!=null){owner.LootableContainers=oldContainers;owner.WorldInteractiveObjects=oldObjects;}});
            var dict=(Dictionary<Type,System.Array>)AccessTools.Field(typeof(LocationScene),"_typeToContainer").GetValue(owner);
            foreach(var type in new[]{typeof(LootableContainer),typeof(WorldInteractiveObject)})
            {
                dict.TryGetValue(type,out var previous);
                var values=previous?.Cast<object>().ToList()??new List<object>();values.Add(container);
                var updated=System.Array.CreateInstance(type,values.Count);for(int i=0;i<values.Count;i++)updated.SetValue(values[i],i);dict[type]=updated;
                unregister.Add(()=>{if(previous!=null)dict[type]=previous;else dict.Remove(type);});
            }
        }

        private void LoadModel(GameObject root,string name)
        {
            if(System.IO.Path.GetFileName(name)!=name)throw new InvalidDataException("Invalid model path.");
            if(models.TryGetValue(name,out var cached)){UnityEngine.Object.Instantiate(cached,root.transform,false).SetActive(true);return;}
            var target=root;root=new GameObject("ISB_LABS_Model_"+name);root.SetActive(false);owned.Add(root);
            var data=JObject.Parse(File.ReadAllText(System.IO.Path.Combine(assetFolder,name)));
            int index=0;
            foreach(var part in A(data,"parts"))
            {
                var child=new GameObject("Model_"+index++);child.transform.SetParent(root.transform,false);
                string geometry=S(part,"geometry");
                using(var reader=new BinaryReader(File.OpenRead(System.IO.Path.Combine(assetFolder,geometry))))
                {
                    int n=reader.ReadInt32(),count=reader.ReadInt32();if(n<1||n>1000000||count<3||count>6000000)throw new InvalidDataException("Invalid model size.");
                    var vertices=new Vector3[n];var uv=new Vector2[n];for(int i=0;i<n;i++){vertices[i]=new Vector3(reader.ReadSingle(),reader.ReadSingle(),-reader.ReadSingle());uv[i]=new Vector2(reader.ReadSingle(),reader.ReadSingle());}
                    var indices=new int[count];for(int i=0;i<count;i++)indices[i]=reader.ReadInt32();
                    for(int i=0;i<count;i+=3){int swap=indices[i+1];indices[i+1]=indices[i+2];indices[i+2]=swap;}
                    var mesh=new Mesh{indexFormat=n>65535?IndexFormat.UInt32:IndexFormat.UInt16,vertices=vertices,uv=uv};
                    var groups=A(part,"groups").ToArray();var definitions=A(part,"materials").ToArray();mesh.subMeshCount=definitions.Length;for(int i=0;i<definitions.Length;i++)mesh.SetTriangles(groups.Where(g=>(int)g["materialIndex"]==i).SelectMany(g=>indices.Skip((int)g["start"]).Take((int)g["count"])).ToArray(),i);
                    mesh.RecalculateNormals();mesh.RecalculateBounds();owned.Add(mesh);child.AddComponent<MeshFilter>().sharedMesh=mesh;
                    var mats=new List<Material>();foreach(var definition in definitions)
                    {
                        var shader=Shader.Find("Standard")??throw new InvalidOperationException("Standard shader unavailable.");
                        var material=new Material(shader);var color=V(definition["color"]);material.color=new Color(color.x,color.y,color.z);material.SetFloat("_Glossiness",.2f);owned.Add(material);
                        string map=S(definition,"map");if(map!="")
                        {
                            if(!textures.TryGetValue(map,out var texture)){texture=new Texture2D(2,2,TextureFormat.RGBA32,true);if(!ImageConversion.LoadImage(texture,File.ReadAllBytes(System.IO.Path.Combine(assetFolder,map))))throw new InvalidDataException("Texture could not load: "+map);texture.wrapMode=TextureWrapMode.Repeat;texture.anisoLevel=4;textures.Add(map,texture);owned.Add(texture);}material.mainTexture=texture;
                        }
                        mats.Add(material);
                    }
                    child.AddComponent<MeshRenderer>().sharedMaterials=mats.ToArray();
                    var collider=child.AddComponent<MeshCollider>();collider.sharedMesh=mesh;child.layer=LayerMask.NameToLayer("LowPolyCollider");
                }
            }
            models.Add(name,root);UnityEngine.Object.Instantiate(root,target.transform,false).SetActive(true);
        }

        private void PrepareBoard(GameObject root,JToken binding)
        {
            var surface=JObject.Parse(File.ReadAllText(System.IO.Path.Combine(assetFolder,"room-board-surface.json")));
            var panel=new GameObject("ISB_LABS_RaidCode");panel.transform.SetParent(root.transform,false);panel.transform.localPosition=V(surface["position"]);panel.transform.localRotation=Quaternion.Euler(V(surface["rotation"]));
            board=new Mesh{name="ISB_Labs_RaidCode"};owned.Add(board);panel.AddComponent<MeshFilter>().sharedMesh=board;
            boardWriting=binding["lines"];
            if(boardWriting==null||!boardWriting.HasValues)throw new InvalidDataException("Labs whiteboard writing is missing.");
            var metadata=JObject.Parse(File.ReadAllText(System.IO.Path.Combine(assetFolder,"room-board-glyphs.json")));boardGlyphs=(JObject)metadata["glyphs"];
            string filename=S(metadata,"texture");
            if(!textures.TryGetValue(filename,out var atlas)){atlas=new Texture2D(2,2,TextureFormat.RGBA32,false);ImageConversion.LoadImage(atlas,File.ReadAllBytes(System.IO.Path.Combine(assetFolder,filename)));textures.Add(filename,atlas);owned.Add(atlas);}
            var material=new Material(Shader.Find("Legacy Shaders/Transparent/Diffuse"));material.mainTexture=atlas;material.color=Color.white;owned.Add(material);panel.AddComponent<MeshRenderer>().sharedMaterial=material;
        }

        private void WriteBoardCode(string code)
        {
            if(board==null||code==boardCode||code.Length!=4)return;boardCode=code;
            var vertices=new List<Vector3>();var uv=new List<Vector2>();var indices=new List<int>();
            foreach(var line in boardWriting)
            {
                float x=(float)line["x"],y=(float)line["y"],height=(float)line["height"],spacing=(float)line["spacing"];
                foreach(char character in S(line,"text").Replace("{code}",code))
                {
                    if(character==' '){x+=height*.45f;continue;}
                    var glyph=boardGlyphs[character.ToString()];if(glyph==null)continue;
                    float h=height*(float)glyph["heightScale"],width=h*(float)glyph["aspect"];var bounds=(JArray)glyph["uv"];int v=vertices.Count;
                    vertices.Add(new Vector3(x,y-h*.5f,0));vertices.Add(new Vector3(x+width,y-h*.5f,0));vertices.Add(new Vector3(x+width,y+h*.5f,0));vertices.Add(new Vector3(x,y+h*.5f,0));
                    uv.Add(new Vector2((float)bounds[0],(float)bounds[1]));uv.Add(new Vector2((float)bounds[2],(float)bounds[1]));uv.Add(new Vector2((float)bounds[2],(float)bounds[3]));uv.Add(new Vector2((float)bounds[0],(float)bounds[3]));
                    indices.AddRange(new[]{v,v+2,v+1,v,v+3,v+2});x+=width+spacing;
                }
            }
            board.Clear();board.vertices=vertices.ToArray();board.uv=uv.ToArray();board.normals=Enumerable.Repeat(Vector3.back,vertices.Count).ToArray();board.triangles=indices.ToArray();board.RecalculateBounds();
        }

        

        private static JObject CeilingFixtureSource()
        {
            return JObject.Parse("{\"scene\":\"Laboratory_LIGHT\",\"path\":\"SBG_Laboratory_Light/OO/Laboratory_Medical_corridor_floor_01/LAMPS/Laboratory_Secret_Lab/LIGHT/lab_lamp_lam_ceiling\",\"position\":[-114.269,3.284,-423.45],\"rotation\":[0,180,106.906059],\"canonicalPivot\":[0.0133742988,-0.07893729,-0.0000152587891],\"model\":\"P1196.json\"}");
        }

        private static IEnumerable<Vector3> CeilingPositions(JToken room)
        {
            var origin=V(room["measurement"]?["origin"]);var size=V(room["measurement"]?["size"]);
            int count=Math.Max(1,Math.Min(4,(int)Math.Ceiling(size.x*size.z/15f)));
            for(int i=0;i<count;i++)
            {
                float x=0,z=0;
                if(count==4){x=(i%2==0?-.25f:.25f)*size.x;z=(i<2?-.25f:.25f)*size.z;}
                else if(size.x>size.z)x=((i+.5f)/count-.5f)*size.x;
                else z=((i+.5f)/count-.5f)*size.z;
                yield return origin+new Vector3(x,size.y-.16f,z);
            }
        }

        private void PrepareCeilingLights(Dictionary<string,Transform[]> paths)
        {
            var interop=AccessTools.TypeByName("ISBSSOFExtra.FacilityLightingInterop");
            var register=interop==null?null:AccessTools.Method(interop,"Register");var remove=interop==null?null:AccessTools.Method(interop,"Unregister");
            foreach(var room in A(layout,"rooms"))
            {
                var scene=SceneManager.GetSceneByName(S(room,"scene").Replace("_MX",""));
                var definitions=room["ceilingLights"] as JArray;
                if(definitions==null)definitions=new JArray(CeilingPositions(room).Select((p,i)=>new JObject{["id"]="ceiling-"+i,["position"]=new JArray(p.x,p.y,p.z),["rotationY"]=0,["intensity"]=1.2f,["range"]=5.5f}));
                foreach(var definition in definitions)
                {
                    var root=new GameObject("ISB_LABS_Light_"+S(room,"id")+"_"+S(definition,"id"));root.SetActive(false);roots.Add(root);SceneManager.MoveGameObjectToScene(root,scene);
                    var source=definition["source"]??CeilingFixtureSource();var native=FindSource(paths,source);
                    if(native!=null)
                    {
                        CloneNative(native,root.transform,source);
                        foreach(var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))if(behaviour!=null)UnityEngine.Object.DestroyImmediate(behaviour);
                        foreach(var light in root.GetComponentsInChildren<Light>(true))UnityEngine.Object.DestroyImmediate(light);
                    }
                    else
                    {
                        try{LoadModel(root,S(source,"model"));}catch(Exception ex){log?.LogError("[Aishi Labs Expansion] Ceiling fixture model unavailable: "+ex.Message);}
                        if(root.transform.childCount>0)root.transform.GetChild(0).name="lab_lamp_lam_ceiling_ISB";
                    }
                    root.transform.position=V(definition["position"]);root.transform.rotation=Quaternion.Euler(0,(float)(definition["rotationY"]??0),0);
                    var fixture=new RoomLamp();
                    foreach(var renderer in root.GetComponentsInChildren<Renderer>(true))
                    {
                        renderer.enabled=true;renderer.forceRenderingOff=false;renderer.lightmapIndex=-1;renderer.allowOcclusionWhenDynamic=true;renderer.SetPropertyBlock(null);
                        var materials=renderer.sharedMaterials;
                        for(int slot=0;slot<materials.Length;slot++)
                        {
                            renderer.SetPropertyBlock(null,slot);var original=materials[slot];if(original==null)continue;
                            bool emissive=original.shader!=null&&original.shader.name.IndexOf("Emissive",StringComparison.OrdinalIgnoreCase)>=0||original.IsKeywordEnabled("_EMISSION")||original.HasProperty("_EmissionColor")&&original.GetColor("_EmissionColor").maxColorComponent>.001f;
                            bool unlitTube=original.name.StartsWith("shopping_mall_lamp_lum_ceiling_noemissive",StringComparison.OrdinalIgnoreCase);
                            if(!emissive&&!unlitTube&&native!=null)continue;
                            if(!original.HasProperty("_EmissiveColor")&&!original.HasProperty("_EmissionColor")&&!(unlitTube&&original.HasProperty("_Color")))continue;
                            var material=new Material(original){name=original.name};owned.Add(material);materials[slot]=material;
                            if(native==null&&material.HasProperty("_EmissionMap")){material.SetTexture("_EmissionMap",material.mainTexture);material.EnableKeyword("_EMISSION");}
                            if(material.HasProperty("_EmissiveColor"))material.SetColor("_EmissiveColor",Color.white);
                            if(material.HasProperty("_EmissionColor"))material.SetColor("_EmissionColor",Color.white*.6f);
                            if(material.HasProperty("_EmissionPower"))material.SetFloat("_EmissionPower",.6f);
                            if(material.HasProperty("_EmissionVisibility"))material.SetFloat("_EmissionVisibility",1f);
                            fixture.Surfaces.Add(new RoomLampSurface{Renderer=renderer,Slot=slot,Tint=material.HasProperty("_Color"),NormalTint=material.HasProperty("_Color")?material.GetColor("_Color"):Color.white});
                        }
                        renderer.sharedMaterials=materials;
                    }
                    var lamp=new GameObject("ISB_LABS_RoomLight");lamp.transform.SetParent(root.transform,false);lamp.transform.localPosition=definition["emitterOffset"]==null?new Vector3(0,-.025f,0):V(definition["emitterOffset"]);
                    var point=lamp.AddComponent<Light>();point.type=S(definition,"lightType")=="spot"?LightType.Spot:LightType.Point;if(point.type==LightType.Spot){point.spotAngle=(float)(definition["spotAngle"]??170f);lamp.transform.localRotation=Quaternion.Euler(90f,0,0);}point.color=new Color(1f,.94f,.84f);point.intensity=(float)(definition["intensity"]??1.2f);point.range=(float)(definition["range"]??5.5f);point.shadows=LightShadows.None;point.renderMode=LightRenderMode.ForcePixel;point.bounceIntensity=0;
                    var nativeCulling=lamp.AddComponent<CullingLightObject>();nativeCulling._transform=lamp.transform;nativeCulling._light=point;nativeCulling.Radius=point.range;nativeCulling._maxLightIntensity=point.intensity;nativeCulling._fadeStartDistance=35f;nativeCulling._fadeEndDistance=50f;nativeCulling._shadowsFadeStartDistance=10f;nativeCulling._shadowsFadeEndDistance=15f;nativeCulling._gameObjectsToTurnOff=new List<GameObject>();nativeCulling.OnPreProcess();
                    root.SetActive(true);
                    fixture.Light=point;fixture.Culling=nativeCulling;fixture.Intensity=(float)(definition["intensity"]??1.2f);roomLamps.Add(fixture);
                    if(register!=null){var registered=root.transform;register.Invoke(null,new object[]{registered});unregister.Add(()=>remove?.Invoke(null,new object[]{registered}));}
                    var visible=root.GetComponentsInChildren<Renderer>(true).Where(r=>r.enabled).ToArray();if(visible.Length>0){var bounds=BoundsOf(root);renderers.Add(visible);spheres.Add(new BoundingSphere(bounds.center,bounds.extents.magnitude));}
                }
            }
            if(interop!=null)
            {
                var changed=interop.GetEvent("Changed",BindingFlags.Public|BindingFlags.Static);
                Action<bool,bool,Color,float> handler=ApplyRoomLighting;
                changed?.AddEventHandler(null,handler);unregister.Add(()=>changed?.RemoveEventHandler(null,handler));
                ApplyRoomLighting((bool)interop.GetProperty("PowerOn").GetValue(null),(bool)interop.GetProperty("EmergencyActive").GetValue(null),(Color)interop.GetProperty("WarningColor").GetValue(null),(float)interop.GetProperty("Pulse").GetValue(null));
            }
            else ApplyRoomLighting(true,false,Color.red,0f);
        }

        private void ApplyRoomLighting(bool powered,bool emergency,Color warning,float pulse)
        {
            if(this==null)return;
            float level=emergency?Mathf.Clamp01(pulse):powered?1f:0f;
            Color tube=level>0f?(emergency?warning:Color.white):Color.black;
            foreach(var lamp in roomLamps)
            {
                if(lamp.Light==null)continue;
                float scale=level*(emergency?.7f:1f);
                lamp.Light.color=emergency?warning:new Color(1f,.94f,.84f);
                lamp.Light.enabled=scale>0f;lamp.Light.intensity=lamp.Intensity*scale;
                if(lamp.Culling!=null){lamp.Culling.IntensityMultiplier=scale;if(CullingManager.Instance!=null&&lamp.Culling.Index>=0)lamp.Culling.UpdateIntensity();}
                foreach(var surface in lamp.Surfaces)
                {
                    if(surface.Renderer==null)continue;
                    surface.Block.SetColor(LampEmissiveColor,tube);
                    surface.Block.SetColor(LampEmissionColor,tube*(.6f*level));
                    surface.Block.SetFloat(LampPower,.6f*level);surface.Block.SetFloat(LampVisibility,level>0f?1f:0f);
                    if(surface.Tint)surface.Block.SetColor(LampTint,level<=0f?Color.black:emergency?warning*level:surface.NormalTint);
                    surface.Renderer.SetPropertyBlock(surface.Block,surface.Slot);
                }
            }
        }

        private void BindDoors()
        {
            foreach(var definition in A(layout,"rooms"))
            {
                var door=LocationScene.GetAllObjects<WorldInteractiveObject>(true).OfType<Door>().FirstOrDefault(d=>d.Id==S(definition,"doorId"));
                if(door==null)throw new InvalidDataException("Room door missing: "+S(definition,"doorId"));
                string id=S(definition,"id");
                var oldState=door.DoorState;var oldKey=door.KeyId;var oldOperatable=door.Operatable;
                if(id=="service-flammable")door.KeyId=LabsNewRoomsIdentity.FlammableKey;
                else if(id=="service-plain")door.KeyId=LabsNewRoomsIdentity.PlainKey;
                else if(id=="lab04")room04=door;
                else if(id=="lab06")room06=door;
                unregister.Add(()=>{if(door!=null){door.KeyId=oldKey;door.Operatable=oldOperatable;door.SetDoorState(oldState,true);}});
                door.Operatable=true;door.SetDoorState(EDoorState.Locked,true);
            }
            var definition04=A(layout,"rooms").Single(r=>S(r,"id")=="lab04");var definition06=A(layout,"rooms").Single(r=>S(r,"id")=="lab06");
            readerPosition=V(definition04["reader"]);
            if(readerPosition.y<V(definition04["measurement"]?["origin"]).y+.2f)readerPosition.y=room04.transform.position.y+.2f;
            room06.OnDoorStateChanged+=Room06Changed;unregister.Add(()=>{if(room06!=null)room06.OnDoorStateChanged-=Room06Changed;});
        }

        private void PrepareControls()
        {
            var definition06=A(layout,"rooms").Single(r=>S(r,"id")=="lab06");
            if(cardReader==null)cardReader=CreateNativeReader("ISB_LABS_CardReader",readerPosition,true);
            if(codeReader==null)codeReader=CreateNativeReader("ISB_LABS_CodeKeypad",V(definition06["reader"]),false);
            controlsPrepared=true;
            try{PrepareKeypadAudio();}catch(Exception ex){log?.LogError("[Aishi Labs Expansion] Keypad audio could not initialize: "+ex.Message);}
        }

        private KeycardDoor CreateNativeReader(string identity,Vector3 position,bool card)
        {
            var native=Resources.FindObjectsOfTypeAll<InteractiveProxy>().Where(p=>p.gameObject.scene.IsValid()&&p.gameObject.scene.isLoaded&&p.Link is KeycardDoor&&!p.transform.root.name.StartsWith("ISB_LABS_")&&p.Grips?.Length>0&&p.Handle!=null&&p.transform.parent?.name.StartsWith("security_pass_card",StringComparison.OrdinalIgnoreCase)==true).OrderBy(p=>(p.transform.position-position).sqrMagnitude).FirstOrDefault();
            if(native==null)throw new InvalidOperationException("Native Labs keycard proxy unavailable.");
            var template=(KeycardDoor)native.Link;
            var staging=new GameObject(identity);staging.SetActive(false);roots.Add(staging);
            SceneManager.MoveGameObjectToScene(staging,room04.gameObject.scene);
            staging.transform.position=position;
            var control=staging.AddComponent<KeycardDoor>();control.Id=identity;control.NetId=StableNetId(identity);control.Proxies=new InteractiveProxy[0];
            control._forceLocalInteraction=true;control.Operatable=true;control.InteractionDot=-1;control.CanBeBreached=false;control.CanInteractWithBreach=false;
            control.KeyId=LabsNewRoomsIdentity.AishiMasterKey;control._additionalKeys=new[]{LabsNewRoomsIdentity.AishiLimited,LabsNewRoomsIdentity.WhiteTusk};
            control._openOnUnlock=false;control._lockOnShut=false;control.DoorKeyOpenInteraction=EInteraction.DoorCardOpen;
            control.GrantedBeep=template.GrantedBeep;control.DeniedBeep=template.DeniedBeep;control.UnlockSound=template.UnlockSound;
            control.OpenSound=new AudioClip[0];control.ShutSound=new AudioClip[0];control.SqueakSound=new AudioClip[0];
            var triggers=AccessTools.Field(typeof(WorldInteractiveObject),"TriggersMap");triggers.SetValue(control,System.Array.CreateInstance(triggers.FieldType.GetElementType(),0));
            var definition=A(layout,"rooms").Single(r=>S(r,"id")== (card?"lab04":"lab06"));
            var proxies=new List<InteractiveProxy>();
            if(card)
            {
                var model=new GameObject("ISB_LABS_ReaderModel");model.SetActive(false);roots.Add(model);SceneManager.MoveGameObjectToScene(model,room04.gameObject.scene);
                var clone=UnityEngine.Object.Instantiate(native.transform.parent.gameObject,model.transform,false);
                clone.transform.localPosition=Vector3.zero;clone.transform.localRotation=native.transform.parent.rotation;clone.transform.localScale=native.transform.parent.lossyScale;
                model.transform.rotation=Quaternion.Euler(0,(float)(definition["reader"]?["rotationY"]??0),0);
                foreach(var renderer in clone.GetComponentsInChildren<Renderer>(true)){renderer.lightmapIndex=-1;renderer.forceRenderingOff=false;}
                model.transform.position+=position-(S(definition["reader"],"positionMode")=="modelOrigin"?clone.transform.position:BoundsOf(model).center);
                staging.transform.position=BoundsOf(model).center;readerPosition=staging.transform.position;
                var proxy=clone.GetComponentInChildren<InteractiveProxy>(true);proxy.Link=control;
                foreach(var collider in clone.GetComponentsInChildren<Collider>(true))collider.enabled=collider.transform==proxy.transform||collider.transform.IsChildOf(proxy.transform);
                foreach(var collider in proxy.GetComponentsInChildren<Collider>(true))collider.gameObject.layer=LayerMask.NameToLayer("Interactive");
                proxies.Add(proxy);
                model.SetActive(true);
            }
            else
            {
                foreach(var panel in new[]{definition["reader"],definition["insideReader"]}.Where(p=>p!=null))
                {
                    var obj=new GameObject(panel==definition["reader"]?"ISB_LABS_KeypadOutside":"ISB_LABS_KeypadInside");obj.SetActive(false);roots.Add(obj);SceneManager.MoveGameObjectToScene(obj,room04.gameObject.scene);obj.transform.position=V(panel);
                    obj.transform.rotation=Quaternion.Euler(0,(float)(panel["rotationY"]??0),0);obj.layer=LayerMask.NameToLayer("Interactive");
                    var hit=obj.AddComponent<BoxCollider>();hit.size=new Vector3(.24f,.32f,.07f);
                    var proxy=obj.AddComponent<InteractiveProxy>();proxy.Link=control;proxy.Grips=new GripPose[0];proxy.FlickCurve=native.FlickCurve;
                    var light=new GameObject("ReaderStatus");light.transform.SetParent(obj.transform,false);proxy.StatusLight=light.AddComponent<Light>();proxy.StatusLight.intensity=0;proxy.StatusLight.shadows=LightShadows.None;
                    var flicker=new GameObject("ReaderFlicker");flicker.transform.SetParent(obj.transform,false);proxy.FlickerLight=flicker.AddComponent<Light>();proxy.FlickerLight.intensity=0;proxy.FlickerLight.shadows=LightShadows.None;
                    proxies.Add(proxy);
                    if(panel!=definition["reader"])CloneInsideKeypad(obj.transform,V(definition["reader"]));
                    obj.SetActive(true);
                }
            }
            control.Proxies=proxies.ToArray();control.Collider=proxies[0].GetComponent<Collider>();control.DoorState=EDoorState.Locked;
            staging.SetActive(true);RegisterReader(control);RegisterPowerReader(control);
            unregister.Add(()=>UnregisterPowerReader(control));
            return control;
        }

        private void RegisterReader(KeycardDoor reader)
        {
            var owner=LocationScene.LoadedScenes.FirstOrDefault(s=>s.gameObject.scene==reader.gameObject.scene);
            if(owner==null)throw new InvalidOperationException("Reader scene registry unavailable.");
            var previousObjects=owner.WorldInteractiveObjects;owner.WorldInteractiveObjects=(previousObjects??new WorldInteractiveObject[0]).Concat(new WorldInteractiveObject[]{reader}).ToArray();
            unregister.Add(()=>{if(owner!=null)owner.WorldInteractiveObjects=previousObjects;});
            var cache=(Dictionary<Type,System.Array>)AccessTools.Field(typeof(LocationScene),"_typeToContainer").GetValue(owner);
            foreach(var type in new[]{typeof(KeycardDoor),typeof(Door),typeof(WorldInteractiveObject)})
            {
                cache.TryGetValue(type,out var previous);var values=previous?.Cast<object>().ToList()??new List<object>();values.Add(reader);
                var updated=System.Array.CreateInstance(type,values.Count);for(int i=0;i<values.Count;i++)updated.SetValue(values[i],i);cache[type]=updated;
                unregister.Add(()=>{if(previous==null)cache.Remove(type);else cache[type]=previous;});
            }
            var world=Singleton<GameWorld>.Instance;
            RegisterDynamicInteraction(reader);
            if(world!=null){reader.OnDoorStateChanged+=world.WorldInteractiveObjectOnDoorStateChanged;unregister.Add(()=>{if(reader!=null&&world!=null)reader.OnDoorStateChanged-=world.WorldInteractiveObjectOnDoorStateChanged;});}
        }

        private void CloneInsideKeypad(Transform target,Vector3 outside)
        {
            var model=Resources.FindObjectsOfTypeAll<MeshRenderer>().Where(r=>r.gameObject.scene.IsValid()&&r.gameObject.scene.isLoaded&&!r.transform.root.name.StartsWith("ISB_LABS_")&&r.name.StartsWith("security_pass_LOD0",StringComparison.OrdinalIgnoreCase)).OrderBy(r=>(r.bounds.center-outside).sqrMagnitude).FirstOrDefault();
            if(model==null)throw new InvalidOperationException("Room 06 native keypad mesh unavailable.");
            var source=model.transform.parent;var clone=UnityEngine.Object.Instantiate(source.gameObject,target,false);
            foreach(var collider in clone.GetComponentsInChildren<Collider>(true))collider.enabled=false;
            foreach(var behaviour in clone.GetComponentsInChildren<MonoBehaviour>(true))UnityEngine.Object.DestroyImmediate(behaviour);
            clone.transform.rotation=Quaternion.Euler(0,180,0)*source.rotation;clone.transform.localScale=source.lossyScale;
            foreach(var renderer in clone.GetComponentsInChildren<Renderer>(true)){renderer.lightmapIndex=-1;renderer.forceRenderingOff=false;}
            clone.transform.position+=target.position-BoundsOf(clone).center;
        }

        private AvailableInteractionState ReaderActions(GamePlayerOwner owner,KeycardDoor reader)
        {
            var actions=new List<InteractionAction>();
            if(reader==codeReader)actions.Add(new InteractionAction{Name="Arsenal Password",Disabled=relocking||busy,Action=()=>{activeKeypadPosition=codeReader.Proxies.OrderBy(p=>(p.transform.position-owner.Player.Position).sqrMagnitude).First().transform.position;OpenKeypad();}});
            else if(room04.DoorState==EDoorState.Locked)
            {
                foreach(var card in owner.Player.InventoryController.Inventory.Equipment.GetAllItems().Where(i=>i.Template.ParentId=="5c164d2286f774194c5e69fa"))
                {
                    var key=card.GetItemComponent<KeyComponent>();if(key==null||key.Template.MaximumNumberOfUsage>0&&key.NumberOfUsages>=key.Template.MaximumNumberOfUsage)continue;
                    actions.Add(new InteractionAction{Name=string.Format("Try_keycard {0}".Localized(),card.ShortName.Localized()),Disabled=busy,Action=()=>UseSelectedCard(card)});
                }
            }
            return new AvailableInteractionState{Actions=actions,SelectedAction=actions.FirstOrDefault(),Error=actions.Count>0?"":room04.DoorState==EDoorState.Locked?"Aishi or White Tusks credentials are required":"Unlocked".Localized()};
        }

        private void ApplyState()
        {
            if(state==null)return;
            WriteBoardCode(S(state,"code"));
            if(!initialState&&room04!=null&&room04.DoorState==EDoorState.Locked&&state["room04Unlocked"]?.Value<bool>()==true&&state["readerSpark"]?.Value<bool>()==true)SparkReader();
            if(room04!=null&&state["room04Unlocked"]?.Value<bool>()==true&&room04.DoorState==EDoorState.Locked)room04.SetDoorState(EDoorState.Shut,true);
            if(cardReader!=null&&state["room04Unlocked"]?.Value<bool>()==true&&cardReader.DoorState==EDoorState.Locked)cardReader.SetDoorState(EDoorState.Shut,true);
            if(room06!=null&&!relocking)
            {
                bool unlocked=state["room06Unlocked"]?.Value<bool>()==true;
                if(unlocked&&room06.DoorState==EDoorState.Locked)room06.SetDoorState(EDoorState.Shut,true);
                else if(!unlocked&&room06.DoorState==EDoorState.Shut)room06.SetDoorState(EDoorState.Locked,true);
            }
            initialState=false;
        }

        private void SparkReader()
        {
            if(cardReader!=null)LabsNewRoomsSparks.Play(cardReader,false,"miniLabs-card-"+S(state,"raid"),1f);
        }

        private IEnumerator SparkDuringSwipe(Player player,string raid)
        {
            yield return new WaitForSecondsRealtime(1.5f);
            if(this!=null&&busy&&S(state,"raid")==raid&&player!=null&&player.HealthController?.IsAlive==true)SparkReader();
        }

        private void Room06Changed(WorldInteractiveObject door,EDoorState previous,EDoorState current)
        {
            if(current==EDoorState.Open)room06Opened=true;
            if(current!=EDoorState.Shut||!room06Opened||relocking)return;
            room06Opened=false;relocking=true;StartCoroutine(RelockRoom06((int)(state?["room06Cycle"]??0)));
        }

        private IEnumerator RelockRoom06(int cycle)
        {
            yield return null;
            if(room06!=null&&room06.DoorState==EDoorState.Shut)room06.SetDoorState(EDoorState.Locked,true);
            CommitRelock(cycle);
        }

        private async void CommitRelock(int cycle)
        {
            var payload=new JObject{["raid"]=S(state,"raid"),["room"]="lab06",["action"]="close",["cycle"]=cycle};
            for(int attempt=0;this!=null;attempt++)
            {
                try
                {
                    var json=await System.Threading.Tasks.Task.Run(()=>RequestHandler.PostJson("/Aishi/miniLabs/unlock",payload.ToString()));
                    if(this==null)return;var response=JObject.Parse(json);AcceptState(response["state"] as JObject);relocking=false;ApplyState();return;
                }
                catch(Exception ex){if(attempt==0)log?.LogError("[Aishi Labs Expansion] Arsenal relock synchronization failed: "+ex.Message);await System.Threading.Tasks.Task.Delay(2000);}
            }
        }

        private void AcceptState(JObject incoming)
        {
            if(incoming==null)return;
            if(state!=null&&S(state,"raid")==S(incoming,"raid")&&(int)(incoming["revision"]??0)<(int)(state["revision"]??0))return;
            state=incoming;
        }

        private void Update()
        {
            LabsNewRoomsSparks.Tick();
            if(Time.unscaledTime>=nextAmbient){nextAmbient=Time.unscaledTime+.25f;try{UpdateAmbientRooms();}catch(Exception ex){ReportFailure("ambient",ex);}}
            if(!controlsPrepared&&Time.unscaledTime>=nextControls){nextControls=Time.unscaledTime+5f;try{PrepareControls();failures.Remove("controls");}catch(Exception ex){ReportFailure("controls",ex);}}
            if(culling!=null&&Time.unscaledTime>=nextCamera){nextCamera=Time.unscaledTime+.5f;var camera=Camera.main;if(culling.targetCamera!=camera){culling.targetCamera=camera;if(camera!=null)culling.SetDistanceReferencePoint(camera.transform);}}
            if(Time.unscaledTime<nextState||polling)return;
            nextState=Time.unscaledTime+2f;Poll();
        }

        private async void Poll()
        {
            polling=true;
            try{var json=await System.Threading.Tasks.Task.Run(()=>RequestHandler.GetJson("/Aishi/miniLabs/state"));if(this==null)return;var response=JObject.Parse(json);AcceptState(response["state"] as JObject);ApplyState();failures.Remove("state");}
            catch(Exception ex){ReportFailure("state",ex);nextState=Time.unscaledTime+15f;}
            finally{polling=false;}
        }

        private void UseCard()
        {
            if(busy||state==null||state["room04Unlocked"]?.Value<bool>()==true)return;
            var player=Singleton<GameWorld>.Instance?.MainPlayer;if(player==null)return;
            var candidates=player.InventoryController.Inventory.Equipment.GetAllItems().Where(i=>new[]{LabsNewRoomsIdentity.AishiMasterKey,LabsNewRoomsIdentity.AishiLimited,LabsNewRoomsIdentity.WhiteTusk}.Contains(i.TemplateId.ToString()));
            var card=candidates.OrderBy(i=>i.TemplateId.ToString()==LabsNewRoomsIdentity.AishiMasterKey?0:1).FirstOrDefault(i=>{var k=i.GetItemComponent<KeyComponent>();return k!=null&&(k.Template.MaximumNumberOfUsage<=0||k.NumberOfUsages<k.Template.MaximumNumberOfUsage);});
            if(card==null){EFT.Communications.NotificationManager.DisplayMessageNotification("Aishi or White Tusks credentials are required");return;}
            UseSelectedCard(card);
        }

        private async void UseSelectedCard(Item card)
        {
            if(busy||state==null||cardReader==null||ReaderBlocked(cardReader)||state["room04Unlocked"]?.Value<bool>()==true)return;
            var player=Singleton<GameWorld>.Instance?.MainPlayer;if(player==null||player.MovementContext.CanInteract!=null)return;
            busy=true;
            try
            {
                string template=card.TemplateId.ToString();
                if(!new[]{LabsNewRoomsIdentity.AishiMasterKey,LabsNewRoomsIdentity.AishiLimited,LabsNewRoomsIdentity.WhiteTusk}.Contains(template))
                {
                    player.MovementContext.ResetCanUsePropState();
                    player.StartInteraction(cardReader,new UnlockResult(card.GetItemComponent<KeyComponent>(),null,false),null);
                    await System.Threading.Tasks.Task.Delay(250);
                    var deadline=Time.realtimeSinceStartup+15f;
                    while(this!=null&&player.CurrentState?.GetType().Name!="IdlePlayerState"&&Time.realtimeSinceStartup<deadline)await System.Threading.Tasks.Task.Delay(50);
                    return;
                }
                var payload=new JObject{["raid"]=S(state,"raid"),["room"]="lab04",["card"]=template,["item"]=card.Id,["prepare"]=true};
                var authorization=JObject.Parse(await System.Threading.Tasks.Task.Run(()=>RequestHandler.PostJson("/Aishi/miniLabs/unlock",payload.ToString())));
                if(this==null)return;
                if(authorization["accepted"]?.Value<bool>()!=true||authorization["state"]?["room04Unlocked"]?.Value<bool>()==true){AcceptState(authorization["state"] as JObject);ApplyState();return;}
                if(ReaderBlocked(cardReader))return;
                var completed=new System.Threading.Tasks.TaskCompletionSource<bool>();
                void Unlocked(WorldInteractiveObject door,EDoorState previous,EDoorState current){if(current==EDoorState.Shut)completed.TrySetResult(true);}
                player.MovementContext.ResetCanUsePropState();
                cardReader.OnDoorStateChanged+=Unlocked;
                try
                {
                    player.StartInteraction(cardReader,new UnlockResult(card.GetItemComponent<KeyComponent>(),null,true),null);
                    if(template!=LabsNewRoomsIdentity.AishiMasterKey)StartCoroutine(SparkDuringSwipe(player,S(state,"raid")));
                    if(await System.Threading.Tasks.Task.WhenAny(completed.Task,System.Threading.Tasks.Task.Delay(15000))!=completed.Task||this==null||player.HealthController?.IsAlive!=true)return;
                    var animationDeadline=Time.realtimeSinceStartup+5f;
                    while(player.CurrentState is DoorInteractState&&Time.realtimeSinceStartup<animationDeadline)await System.Threading.Tasks.Task.Delay(50);
                    if(player.CurrentState is DoorInteractState||this==null||player.HealthController?.IsAlive!=true)return;
                }
                finally{if(cardReader!=null)cardReader.OnDoorStateChanged-=Unlocked;}
                if(template!=LabsNewRoomsIdentity.AishiMasterKey)
                {
                    var result=await player.InventoryController.TryRunNetworkTransaction(ItemManipulator.Remove(card,player.InventoryController,true));
                    if(result.Failed)return;
                    EFT.Communications.NotificationManager.DisplayMessageNotification("aishi_labs_card_destroyed".Localized());
                }
                payload["prepare"]=false;
                JObject response=null;
                for(int attempt=0;attempt<3;attempt++){try{response=JObject.Parse(await System.Threading.Tasks.Task.Run(()=>RequestHandler.PostJson("/Aishi/miniLabs/unlock",payload.ToString())));break;}catch when(attempt<2){await System.Threading.Tasks.Task.Delay(500);}}
                if(this==null)return;AcceptState(response["state"] as JObject);ApplyState();
            }
            catch(Exception ex){log?.LogError("[Aishi Labs Expansion] Card reader failed: "+ex.Message);}
            finally{busy=false;if(this!=null&&cardReader!=null&&state?["room04Unlocked"]?.Value<bool>()!=true)cardReader.SetDoorState(EDoorState.Locked,true);}
        }

        private void OpenKeypad()
        {
            if(keypad||state==null||codeReader==null||relocking||ReaderBlocked(codeReader))return;keypad=true;entry="";feedback="";cursorLock=Cursor.lockState;cursorVisible=Cursor.visible;GamePlayerOwner.SetIgnoreInput(true);Cursor.lockState=CursorLockMode.None;Cursor.visible=true;
        }

        private void CloseKeypad(){if(!keypad)return;keypad=false;GamePlayerOwner.SetIgnoreInput(false);Cursor.lockState=cursorLock;Cursor.visible=cursorVisible;}

        private void OnGUI()
        {
            if(!keypad)return;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            var e = Event.current;
            if (e.type == EventType.KeyDown)
            {
                if (e.keyCode == KeyCode.Escape)
                {
                    CloseKeypad();
                    e.Use();
                    return;
                }

                if (busy)
                {
                    e.Use();
                    return;
                }

                if (e.keyCode == KeyCode.Backspace && entry.Length > 0)
                {
                    entry = entry.Substring(0, entry.Length - 1);
                    PlayKeypadSound("keypress");
                    e.Use();
                }
                else if ((e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter))
                {
                    PlayKeypadSound("keypress");
                    SubmitCode();
                    e.Use();
                }
                else if (e.character >= '0' && e.character <= '9' && entry.Length < 4 && Time.unscaledTime >= keypadResultUntil)
                {
                    entry += e.character;
                    PlayKeypadSound("keypress");
                    e.Use();
                }
            }

            var s = Screen.height / 1080f;
            const float panelW = 224f;
            const float panelH = 229f;
            var px = (Screen.width - panelW * s) / 2f;
            var py = Screen.height - 264.5f * s;
            var panelCx = px + panelW * s / 2f;
            var showResult = Time.unscaledTime < keypadResultUntil;
            EnsureKeypadStyles();
            GUI.Label(new Rect(px - 80f * s, py - 32f * s, (panelW + 160f) * s, 30f * s), busy ? "Waiting for host..." : "Arsenal Password", _keypadButtonStyle);
            DrawKeypadTex("Background_Shadow", new Rect(px - 15f * s, py - 15f * s, (panelW + 30f) * s, (panelH + 30f) * s), 1f);
            DrawKeypadTex("Background", new Rect(px, py, panelW * s, panelH * s), 1f);
            var subRect = new Rect(panelCx - 107f * s, py + 5f * s, 214f * s, 52f * s);
            DrawKeypadTex(showResult ? (keypadGranted ? "Substrate_Green" : "Substrate_Red") : "Substrate_Gray", subRect, 1f);
            if (showResult)
            {
                var fade = Mathf.Clamp01((keypadResultUntil - Time.unscaledTime) / 0.8f);
                var lightRect = new Rect(panelCx - 150f * s, subRect.y + subRect.height / 2f + (84f - 74f) * s, 300f * s, 148f * s);
                DrawKeypadTex(keypadGranted ? "Light_Green" : "Light_Red", lightRect, fade);
                DrawKeypadTex(keypadGranted ? "Flashing_Green" : "Flashing_Red", subRect, fade);
            }

            var holderLeft = panelCx - 110f * s;
            var holderTop = py + 2.5f * s;
            _keypadSlotStyle.fontSize = Mathf.RoundToInt(30f * s);
            for (var slot = 0; slot < 7; slot++)
            {
                var cx = holderLeft + (26f + 28f * slot) * s;
                var cy = holderTop + 28f * s;
                DrawKeypadTex("Number_Enter", new Rect(cx - 14f * s, cy - 14.5f * s, 28f * s, 39f * s), 1f);
                var typed = slot < entry.Length;
                _keypadSlotStyle.normal.textColor = showResult ? (keypadGranted ? new Color(0.65f, 1f, 0.65f) : new Color(1f, 0.5f, 0.5f)) : (typed ? new Color(0.9f, 0.94f, 1f) : new Color(0.3f, 0.33f, 0.37f));
                GUI.Label(new Rect(cx - 14f * s, cy - 25f * s, 28f * s, 50f * s), typed ? entry[slot].ToString() : "0", _keypadSlotStyle);
            }

            var keys = new[]
            {
                "1",
                "2",
                "3",
                "4",
                "5",
                "6",
                "7",
                "8",
                "9",
                "X",
                "0",
                "E"
            };
            var gridLeft = panelCx - 70f * s;
            var gridTop = py + (114.5f + 26f - 65f) * s;
            _keypadButtonStyle.fontSize = Mathf.RoundToInt(17f * s);
            var mouse = Event.current.mousePosition;
            for (var i = 0; i < keys.Length; i++)
            {
                var col = i % 3;
                var row = i / 3;
                var bcx = gridLeft + (22f + 48f * col) * s;
                var bcy = gridTop + (17f + 32f * row) * s;
                var hitRect = new Rect(bcx - 20f * s, bcy - 14f * s, 40f * s, 28f * s);
                var visRect = new Rect(bcx - 21f * s, bcy - 15f * s, 42f * s, 30f * s);
                DrawKeypadTex(hitRect.Contains(mouse) ? "Button_Highlighted" : "Button_Normal", visRect, 1f);
                if (keys[i] == "X")
                {
                    DrawKeypadTex("Icon_Del_Static", new Rect(bcx - 7.5f * s, bcy - 8.5f * s, 15f * s, 17f * s), 1f);
                }
                else if (keys[i] == "E")
                {
                    DrawKeypadTex("Icon_Enter_Static", new Rect(bcx - 9.5f * s, bcy - 9.5f * s, 19f * s, 19f * s), 1f);
                }
                else
                {
                    GUI.Label(hitRect, keys[i], _keypadButtonStyle);
                }

                if (!GUI.Button(hitRect, GUIContent.none, GUIStyle.none))
                {
                    continue;
                }

                PlayKeypadSound("keypress");
                if (keys[i] == "X")
                {
                    if (entry.Length > 0)
                    {
                        entry = "";
                    }
                    else
                    {
                        CloseKeypad();
                    }
                }
                else if (keys[i] == "E")
                {
                    SubmitCode();
                }
                else if (entry.Length < 4 && Time.unscaledTime >= keypadResultUntil)
                {
                    entry += keys[i];
                }
            }
        }

        private GUIStyle _keypadSlotStyle;
        private GUIStyle _keypadButtonStyle;
        private Dictionary<string, Texture2D> _keypadTex;
        private void DrawKeypadTex(string name, Rect rect, float alpha)
        {
            if (!_keypadTex.TryGetValue(name, out var tex) || tex == null)
            {
                return;
            }

            GUI.color = new Color(1f, 1f, 1f, alpha);
            GUI.DrawTexture(rect, tex, ScaleMode.StretchToFill, true);
            GUI.color = Color.white;
        }

        private void EnsureKeypadStyles()
        {
            if (_keypadSlotStyle != null)
            {
                return;
            }

            _keypadTex = new Dictionary<string, Texture2D>();
            foreach (var name in new[]
            {
                "Background",
                "Background_Shadow",
                "Substrate_Gray",
                "Substrate_Red",
                "Substrate_Green",
                "Flashing_Red",
                "Flashing_Green",
                "Light_Red",
                "Light_Green",
                "Button_Normal",
                "Button_Highlighted",
                "Button_Disabled",
                "Icon_Del_Static",
                "Icon_Enter_Static",
                "Number_Enter",
            }

            )
            {
                _keypadTex[name] = LoadKeypadTexture($"Aishi.Labs.keypad.keypad-{name}.png");
            }

            _keypadSlotStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 30,
                alignment = TextAnchor.MiddleCenter,
            };
            _keypadButtonStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 17,
                alignment = TextAnchor.MiddleCenter,
            };
            _keypadButtonStyle.normal.textColor = new Color(0.87f, 0.9f, 0.93f);
            foreach (var font in Resources.FindObjectsOfTypeAll<Font>())
            {
                if (font != null && font.name.ToLowerInvariant().Contains("bender"))
                {
                    _keypadSlotStyle.font = font;
                    _keypadButtonStyle.font = font;
                    break;
                }
            }
        }

        private Texture2D LoadKeypadTexture(string name)
        {
            using(var stream=typeof(LabsNewRoomsRuntime).Assembly.GetManifestResourceStream(name))
            using(var memory=new MemoryStream())
            {
                if(stream==null)throw new InvalidDataException("Keypad texture unavailable: "+name);
                stream.CopyTo(memory);var texture=new Texture2D(2,2,TextureFormat.RGBA32,false);if(!ImageConversion.LoadImage(texture,memory.ToArray()))throw new InvalidDataException(name);owned.Add(texture);return texture;
            }
        }

        private void PlayKeypadSound(string name)
        {
            if(keypadAudio==null){var root=new GameObject("ISB_LABS_KeypadAudio");roots.Add(root);root.transform.position=V(A(layout,"rooms").Single(r=>S(r,"id")=="lab06")["reader"]);keypadAudio=root.AddComponent<AudioSource>();keypadAudio.spatialBlend=1;keypadAudio.minDistance=1;keypadAudio.maxDistance=12;keypadAudio.volume=.5f;keypadAudio.playOnAwake=false;}
            if(!keypadClips.TryGetValue(name,out var clip)||clip==null||clip.samples<=0){PrepareKeypadAudio();keypadClips.TryGetValue(name,out clip);}
            if(activeKeypadPosition!=Vector3.zero)keypadAudio.transform.position=activeKeypadPosition;
            if(clip!=null)keypadAudio.PlayOneShot(clip);
        }

        private void PrepareKeypadAudio()
        {
            foreach(var name in new[]{"keypress","granted","denied"})
            {
                if(keypadClips.TryGetValue(name,out var existing)&&existing!=null&&existing.samples>0)continue;
                if(existing!=null)UnityEngine.Object.Destroy(existing);keypadClips.Remove(name);
                using(var reader=new BinaryReader(File.OpenRead(System.IO.Path.Combine(assetFolder,"keypad-"+name+".wav"))))
                {
                    if(new string(reader.ReadChars(4))!="RIFF")throw new InvalidDataException("Invalid keypad audio.");reader.ReadInt32();if(new string(reader.ReadChars(4))!="WAVE")throw new InvalidDataException("Invalid keypad audio.");
                    int channels=0,frequency=0,bits=0,format=0;byte[] data=null;
                    while(reader.BaseStream.Position+8<=reader.BaseStream.Length)
                    {
                        string chunk=new string(reader.ReadChars(4));int length=reader.ReadInt32();long end=reader.BaseStream.Position+length;if(length<0||end>reader.BaseStream.Length)throw new InvalidDataException("Invalid keypad audio chunk.");
                        if(chunk=="fmt "){format=reader.ReadInt16();channels=reader.ReadInt16();frequency=reader.ReadInt32();reader.ReadInt32();reader.ReadInt16();bits=reader.ReadInt16();}
                        else if(chunk=="data")data=reader.ReadBytes(length);
                        reader.BaseStream.Position=end+(length&1);
                    }
                    if(format!=1||bits!=16||channels<1||channels>2||frequency<=0||data==null||data.Length%(channels*2)!=0)throw new InvalidDataException("Unsupported keypad audio format.");
                    var samples=new float[data.Length/2];for(int i=0;i<samples.Length;i++)samples[i]=(short)(data[i*2]|data[i*2+1]<<8)/32768f;
                    var clip=AudioClip.Create("ISB_Labs_Keypad_"+name,samples.Length/channels,channels,frequency,false);clip.hideFlags=HideFlags.DontUnloadUnusedAsset;
                    if(!clip.SetData(samples,0))throw new InvalidDataException("Keypad audio sample upload failed.");keypadClips[name]=clip;owned.Add(clip);
                }
            }
        }


        private async void SubmitCode()
        {
            if(busy||entry.Length!=4||Time.unscaledTime<keypadResultUntil||relocking||codeReader==null||ReaderBlocked(codeReader))return;busy=true;
            try
            {
                string request=new JObject{["raid"]=S(state,"raid"),["room"]="lab06",["code"]=entry}.ToString();
                var json=await System.Threading.Tasks.Task.Run(()=>RequestHandler.PostJson("/Aishi/miniLabs/unlock",request));if(this==null)return;
                var response=JObject.Parse(json);AcceptState(response["state"] as JObject);ApplyState();keypadGranted=response["accepted"]?.Value<bool>()==true;keypadResultUntil=Time.unscaledTime+.8f;PlayKeypadSound(keypadGranted?"granted":"denied");
                if(keypadGranted)StartCoroutine(FinishKeypad());else{entry="";feedback="Incorrect code.";}
            }
            catch(Exception ex){feedback="Unable to contact the server.";log?.LogError("[Aishi Labs Expansion] Keypad failed: "+ex.Message);}
            finally{busy=false;}
        }

        private IEnumerator FinishKeypad(){yield return new WaitForSecondsRealtime(.8f);CloseKeypad();}

        private void OnDestroy()
        {
            foreach(var area in ambientAreas)area.Restore();
            CloseKeypad();LabsNewRoomsSparks.Reset();culling?.Dispose();foreach(var action in unregister.AsEnumerable().Reverse())action();
            foreach(var root in roots)if(root!=null)UnityEngine.Object.Destroy(root);foreach(var item in owned)if(item!=null)UnityEngine.Object.Destroy(item);
            if(active==this)active=null;
        }
    }

    public static class LabsNewRoomsWindowCovers
    {
        private static bool initialized;

        public static void Initialize()
        {
            if (initialized) return;
            initialized = true;
            SceneManager.sceneLoaded += OnSceneLoaded;
            for (int i = 0; i < SceneManager.sceneCount; i++)
                Apply(SceneManager.GetSceneAt(i));
        }

        public static void Shutdown()
        {
            if (!initialized) return;
            initialized = false;
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Apply(scene);

        private static void Apply(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded || scene.name != LabsNewRoomsWindowCoverPolicy.Scene) return;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name != LabsNewRoomsWindowCoverPolicy.Parent) continue;
                foreach (Transform child in root.transform)
                {
                    if (!child.gameObject.activeSelf) continue;
                    foreach (var renderer in child.GetComponentsInChildren<Renderer>(true))
                    {
                        var center = renderer.bounds.center;
                        if (!LabsNewRoomsWindowCoverPolicy.Matches(scene.name, root.name, child.name, renderer.name, center.x, center.y, center.z)) continue;
                        child.gameObject.SetActive(false);
                        break;
                    }
                }
            }
        }
    }
#endif

    public static class LabsNewRoomsIdentity
    {
        public const string ConfigFile = "miniLabsExpansion.json";
        public const string FlammableKey = "d8d9671481acbcdb6cdc9b2b";
        public const string PlainKey = "c1ba6df54f1dcfdd088398a7";
        public const string AishiMasterKey = "694479f61287f9a2b1060a7f";
        public const string AishiLimited = "6944b4b15a0413f1b7f50724";
        public const string WhiteTusk = "999a5a06e1a05ca89b786f34";
        public const string TerminalStorage = "68a5b9af76a25d8e6e0e2e56";
        public static string StorageTemplate(string asset)
        {
            switch(asset)
            {
                case "X_TERMINAL_GEAR_CABINET":return TerminalStorage;
                case "P1592":return "5909d76c86f77471e53d2adf";
                case "P1591":return "5909d89086f77472591234a0";
                case "P1599":return "5909d5ef86f77467974efbd8";
                case "P1594":return "5909d4c186f7746ad34e805a";
                case "P1593":return "5909d24f86f77466f56e6855";
                case "P1587":return "578f87a3245977356274f2cb";
                case "P1598":return "5909d36d86f774660f0bb900";
                default:throw new ArgumentException("Storage template was not configured for "+asset);
            }
        }
        public static bool IsLabs(string location) => string.Equals(location, "laboratory", StringComparison.OrdinalIgnoreCase) || string.Equals(location, "5b0fc42d86f7744a585f9105", StringComparison.OrdinalIgnoreCase);
        public static bool IsTemplateId(string value) => value != null && value.Length == 24 && value.All(c => c >= '0' && c <= '9' || c >= 'a' && c <= 'f');
    }

    public sealed class LabsRoomPoint
    {
        public float x { get; set; }
        public float y { get; set; }
        public float z { get; set; }
        public float rotationX { get; set; }
        public float rotationY { get; set; }
        public float rotationZ { get; set; }
        public LabsRoomPoint() { }
        public LabsRoomPoint(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public bool Valid() => new[] { x, y, z, rotationX, rotationY, rotationZ }.All(f => !float.IsNaN(f) && !float.IsInfinity(f) && Math.Abs(f) <= 10000f);
    }

    public sealed class LabsRoomCard
    {
        public string template { get; set; } = "";
        public bool consumeAllRemaining { get; set; }
    }

    public sealed class LabsRoomDefinition
    {
        public string id { get; set; } = "";
        public string doorId { get; set; } = "";
        public string scene { get; set; } = "";
        public LabsRoomPoint doorPosition { get; set; } = new LabsRoomPoint();
        public string access { get; set; } = "";
        public string keyTemplate { get; set; } = "";
        public LabsRoomPoint reader { get; set; }
        public LabsRoomPoint codeBoard { get; set; }
    }

    public sealed class LabsNewRoomsConfig
    {
        public int schemaVersion { get; set; } = 1;
        public bool enabled { get; set; }
        public List<LabsRoomCard> room04Cards { get; set; } = new List<LabsRoomCard>
        {
            new LabsRoomCard { template = LabsNewRoomsIdentity.AishiMasterKey },
            new LabsRoomCard { template = LabsNewRoomsIdentity.AishiLimited, consumeAllRemaining = true },
            new LabsRoomCard { template = LabsNewRoomsIdentity.WhiteTusk, consumeAllRemaining = true }
        };
        public List<LabsRoomDefinition> rooms { get; set; } = new List<LabsRoomDefinition>
        {
            new LabsRoomDefinition { id = "lab04", doorId = "door_Laboratory_Medical_corridor_floor_1_00031", scene = "Laboratory_Medical_corridor_floor_1", doorPosition = new LabsRoomPoint(-114.479324f, 1.12753057f, -429.57254f), access = "keycard" },
            new LabsRoomDefinition { id = "lab06", doorId = "door_Laboratory_Medical_corridor_floor_1_00030", scene = "Laboratory_Medical_corridor_floor_1", doorPosition = new LabsRoomPoint(-114.869621f, 1.12753057f, -439.543671f), access = "raid-code", reader = new LabsRoomPoint(-116.174f, 1.3106f, -439.545f) },
            new LabsRoomDefinition { id = "service-flammable", doorId = "door_Laboratory_parking_corridorR_floor_1_00008", scene = "Laboratory_parking_corridorR_floor_1", doorPosition = new LabsRoomPoint(-205.483749f, 1.13807249f, -388.873f), access = "mechanical-key", keyTemplate = LabsNewRoomsIdentity.FlammableKey },
            new LabsRoomDefinition { id = "service-plain", doorId = "door_Laboratory_parking_corridorL_floor_1_00011", scene = "Laboratory_parking_corridorL_floor_1", doorPosition = new LabsRoomPoint(-274.084747f, 1.10447264f, -382.655f), access = "mechanical-key", keyTemplate = LabsNewRoomsIdentity.PlainKey }
        };

        public void Validate()
        {
            if (schemaVersion != 1 || rooms == null || rooms.Count != 4 || room04Cards == null || room04Cards.Count != 3)
                throw new ArgumentException("Labs expansion requires schema 1, four rooms and three accepted cards.");
            if (rooms.Any(r => r == null || string.IsNullOrWhiteSpace(r.id) || string.IsNullOrWhiteSpace(r.doorId) || string.IsNullOrWhiteSpace(r.scene) || r.doorPosition == null || !r.doorPosition.Valid() || r.reader != null && !r.reader.Valid() || r.codeBoard != null && !r.codeBoard.Valid()))
                throw new ArgumentException("Labs expansion contains an invalid room anchor.");
            if (rooms.Select(r => r.id).Distinct(StringComparer.Ordinal).Count() != 4 || rooms.Select(r => r.doorId).Distinct(StringComparer.Ordinal).Count() != 4)
                throw new ArgumentException("Labs expansion room and door IDs must be unique.");
            var expected = new Dictionary<string, string>(StringComparer.Ordinal) { ["lab04"] = "keycard", ["lab06"] = "raid-code", ["service-flammable"] = "mechanical-key", ["service-plain"] = "mechanical-key" };
            if (rooms.Any(r => !expected.ContainsKey(r.id) || r.access != expected[r.id]))
                throw new ArgumentException("Labs expansion access types do not match their rooms.");
            if (rooms.Single(r => r.id == "service-flammable").keyTemplate != LabsNewRoomsIdentity.FlammableKey || rooms.Single(r => r.id == "service-plain").keyTemplate != LabsNewRoomsIdentity.PlainKey)
                throw new ArgumentException("Labs expansion mechanical key IDs are persistent and cannot be replaced.");
            if (room04Cards.Any(c => c == null || !LabsNewRoomsIdentity.IsTemplateId(c.template)) || room04Cards.Select(c => c.template).Distinct(StringComparer.Ordinal).Count() != 3)
                throw new ArgumentException("Labs expansion contains invalid or repeated card IDs.");
            var master = room04Cards.SingleOrDefault(c => c.template == LabsNewRoomsIdentity.AishiMasterKey);
            if (master == null || master.consumeAllRemaining || !room04Cards.Any(c => c.template == LabsNewRoomsIdentity.AishiLimited && c.consumeAllRemaining) || !room04Cards.Any(c => c.template == LabsNewRoomsIdentity.WhiteTusk && c.consumeAllRemaining))
                throw new ArgumentException("Labs expansion must preserve the master key and exhaust limited accepted cards.");
            var room04 = rooms.Single(r => r.id == "lab04");
            if (enabled && (room04.reader == null || room04.codeBoard == null || rooms.Single(r => r.id == "lab06").reader == null))
                throw new ArgumentException("Labs expansion cannot be enabled before reader and code board placements are configured.");
        }
    }

    public sealed class LabsNewRoomsRaidState
    {
        public string raid { get; set; } = "";
        public string code { get; set; } = "";
        public bool room04Unlocked { get; set; }
        public bool room06Unlocked { get; set; }
        public int room06Cycle { get; set; }
        public bool readerSpark { get; set; }
        public int revision { get; set; }

        public static LabsNewRoomsRaidState Create(string raid, Func<int, int, int> next)
        {
            if (string.IsNullOrWhiteSpace(raid) || raid.Length > 128 || next == null)
                throw new ArgumentException("A raid identity and authority random source are required.");
            int value = next(1000, 10000);
            if (value < 1000 || value >= 10000)
                throw new ArgumentOutOfRangeException(nameof(next));
            return new LabsNewRoomsRaidState { raid = raid, code = value.ToString(System.Globalization.CultureInfo.InvariantCulture) };
        }

        public bool TryCode(string raid, string submitted)
        {
            if (this.raid != raid || submitted == null || submitted.Length != 4 || submitted != code)
                return false;
            if (!room06Unlocked) { room06Unlocked = true; room06Cycle++; revision++; }
            return true;
        }

        public bool CloseArsenal(string raid,int cycle)
        {
            if(this.raid!=raid||cycle!=room06Cycle)return false;
            if(room06Unlocked){room06Unlocked=false;revision++;}
            return true;
        }
    }

#if ISB_LABS_SERVER
    [Injectable(InjectionType.Singleton, TypePriority = OnLoadOrder.TraderRegistration + 99999)]
    public sealed class LabsNewRoomsRegistration : IOnLoad
    {
        private readonly ModHelper _mods;
        private readonly CustomItemService _customItems;
        private readonly TemplateTable _templates;
        private readonly JsonUtil _json;
        private readonly ItemHelper _items;
        private readonly IServiceProvider _services;
        private readonly ILogger<LabsNewRoomsRegistration> _logger;
        private bool reportedSessionError;
        private JsonObject _layout;
        private static LabsNewRoomsRegistration instance;
        private readonly Dictionary<string, LabsNewRoomsRaidState> states = new Dictionary<string, LabsNewRoomsRaidState>();
        private readonly System.Runtime.CompilerServices.ConditionalWeakTable<LocationBase, LabsNewRoomsRaidState> locationStates = new System.Runtime.CompilerServices.ConditionalWeakTable<LocationBase, LabsNewRoomsRaidState>();
        private readonly Dictionary<string, Tuple<string,string,DateTime>> reservations = new Dictionary<string, Tuple<string,string,DateTime>>();
        private readonly object gate = new object();
        public LabsNewRoomsConfig Config { get; private set; } = new LabsNewRoomsConfig();

        public LabsNewRoomsRegistration(ModHelper mods, CustomItemService customItems, TemplateTable templates, JsonUtil json, ItemHelper items, IServiceProvider services, ILogger<LabsNewRoomsRegistration> logger)
        {
            _mods = mods;
            _customItems = customItems;
            _templates = templates;
            _json = json;
            _items = items;
            _services = services;
            _logger = logger;
        }

        public Task OnLoadAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string folder = _mods.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());
            string path = System.IO.Path.Combine(folder, LabsNewRoomsIdentity.ConfigFile);
            if (!File.Exists(path)) return Task.CompletedTask;
            _layout = JsonNode.Parse(File.ReadAllText(path))?.AsObject() ?? throw new InvalidDataException("Labs expansion configuration is empty.");
            if (_layout["enabled"]?.GetValue<bool>() != true) return Task.CompletedTask;
            if (Text(_layout,"targetSpt") != "4.1.6" || _layout["layoutOnly"]?.GetValue<bool>() != false || _layout["rooms"]?.AsArray().Count != 4)
                throw new InvalidDataException("Labs expansion needs the validated SPT 4.1.6 runtime layout.");

            RequireTemplate(LabsNewRoomsIdentity.FlammableKey);

            RequireTemplate(LabsNewRoomsIdentity.PlainKey);
            if (!_templates.Items.ContainsKey(LabsNewRoomsIdentity.TerminalStorage))
            {
                var result = _customItems.CreateItemFromClone(new NewItemFromCloneDetails
                {
                    ItemTplToClone = "578f8782245977354405a1e3", NewId = LabsNewRoomsIdentity.TerminalStorage, NewItemName = "lc_spec_stash_random_terminal", ParentId = _templates.Items["578f8782245977354405a1e3"].Parent,
                    Locales = new Dictionary<string,LocaleDetails> { ["en"] = new LocaleDetails { Name="Storage",ShortName="Storage",Description="TerraGroup storage." } },
                    OverrideProperties = _json.Deserialize<TemplateItemProperties>("{\"Grids\":[{\"_name\":\"main\",\"_id\":\"68a5b9af76a25d8e6e0e2e58\",\"_parent\":\"68a5b9af76a25d8e6e0e2e56\",\"_props\":{\"cellsH\":10,\"cellsV\":20,\"filters\":[{\"Filter\":[\"54009119af1c881c07000029\"],\"ExcludedFilter\":[\"5448bf274bdc2dfc2f8b456a\"]}]}}]}"),
                    AddToHandbook = false, AddToFleaPriceDb = false
                }, Assembly.GetExecutingAssembly());
                if (!result.Success) throw new InvalidOperationException("Labs expansion inventory template could not be created.");
            }
            foreach (var preset in Array(_layout,"lootPresets"))
                foreach (var item in Array(preset,"items")) RequireTemplate(Text(item,"_tpl"));
            foreach (var item in Array(_layout,"lootItems")) RequireTemplate(Text(item,"id"));
            instance = this;
            new Harmony("com.samc137.aishi.labs-new-rooms").Patch(AccessTools.Method(typeof(LocationLifecycleService), "GenerateLocationAndLoot"),
                postfix: new HarmonyMethod(typeof(LabsNewRoomsRegistration), nameof(AfterLocation)) { priority = Priority.Last });
            var blacklist=AccessTools.TypeByName("Aishi.AishiItemBlacklist");
            var filter=blacklist==null?null:AccessTools.Method(blacklist,"FilterWorld");
            if(filter!=null)new Harmony("com.samc137.aishi.labs-new-rooms").Patch(filter,
                prefix:new HarmonyMethod(typeof(LabsNewRoomsRegistration),nameof(PreserveAuthoredLoot)),
                finalizer:new HarmonyMethod(typeof(LabsNewRoomsRegistration),nameof(RestoreAuthoredLoot)));
            return Task.CompletedTask;
        }

        private static void PreserveAuthoredLoot(LocationBase __0,out List<SpawnpointTemplate> __state)
        {
            __state=__0?.Loot?.Where(p=>p.Id?.StartsWith("ISB_LABS_",StringComparison.Ordinal)==true).ToList()??[];
            var preserved=__state;
            if(preserved.Count>0)__0.Loot=__0.Loot.Where(p=>!preserved.Contains(p)).ToList();
        }

        private static Exception? RestoreAuthoredLoot(LocationBase __0,List<SpawnpointTemplate> __state,Exception? __exception)
        {
            if(__state?.Count>0&&__0!=null)__0.Loot=(__0.Loot??[]).Concat(__state).ToList();
            return __exception;
        }

        private TemplateItem RequireTemplate(string identity)
        {
            if (!_templates.Items.TryGetValue(identity, out var value)) throw new InvalidDataException("Labs expansion item unavailable: " + identity);
            return value;
        }

        internal static string Text(JsonNode value, string name) => value?[name]?.GetValue<string>() ?? "";
        internal static IEnumerable<JsonNode> Array(JsonNode value, string name) => value?[name] is JsonArray array ? array.Where(x => x != null) : Enumerable.Empty<JsonNode>();
        private static int Number(JsonNode value, string name, int fallback = 0) => value?[name]?.GetValue<int>() ?? fallback;
        private static double Real(JsonNode value, string name, double fallback = 0) => value?[name]?.GetValue<double>() ?? fallback;
        private static string Id() => Guid.NewGuid().ToString("N").Substring(0,24);
        private static bool Roll(double chance, Random random) => chance >= 100 || chance > 0 && random.NextDouble() * 100 < chance;

        private static void AfterLocation(MongoId __0, string __1, bool __2, LocationBase __result)
        {
            if (instance?._layout == null || !__2 || !LabsNewRoomsIdentity.IsLabs(__1)) return;
            instance.Populate(__0.ToString(), __result);
        }

        private object FikaService(string name)
        {
            var type=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType(name,false)).FirstOrDefault(t=>t!=null);
            return type==null?null:_services.GetService(type);
        }

        private string EventSession(string session)
        {
            if(string.IsNullOrWhiteSpace(session))return session;
            try
            {
                var headless=FikaService("FikaServer.Services.Headless.HeadlessService");
                var clients=headless?.GetType().GetProperty("HeadlessClients")?.GetValue(headless) as IEnumerable;
                if(clients!=null)foreach(var entry in clients)if(entry.GetType().GetProperty("Key")?.GetValue(entry)?.ToString()==session)return session;
                var matches=FikaService("FikaServer.Services.MatchService");if(matches==null)return session;
                var method=matches.GetType().GetMethod("GetMatchIdByPlayer",new[]{typeof(MongoId)})??throw new MissingMethodException(matches.GetType().FullName,"GetMatchIdByPlayer");
                var shared=method.Invoke(matches,new object[]{new MongoId(session)})?.ToString();if(shared!=null)return shared;
                var saves=_services.GetService(typeof(SaveServer)) as SaveServer;
                if(saves==null||!saves.GetProfiles().TryGetValue(new MongoId(session),out var profile))return session;
                foreach(var player in new[]{profile.CharacterData?.PmcData?.Id,profile.CharacterData?.ScavData?.Id})
                {
                    if(!player.HasValue)continue;shared=method.Invoke(matches,new object[]{player.Value})?.ToString();if(shared!=null)return shared;
                }
            }
            catch(Exception ex)
            {
                if(!reportedSessionError){reportedSessionError=true;_logger.LogError(ex,"Aishi Labs expansion could not resolve the Fika raid owner.");}
            }
            return session;
        }

        public object Snapshot(string session, bool includeLayout = false)
        {
            lock (gate)
            {
                TryRaidState(session, out var state);
                if(includeLayout)return new { layout = _layout, state };
                return new { state };
            }
        }

        private bool TryRaidState(string session, out LabsNewRoomsRaidState state)
        {
            state = null;
            var matches = FikaService("FikaServer.Services.MatchService");
            if (matches != null)
            {
                string matchId = EventSession(session);
                var byProfile = matches.GetType().GetMethod("GetMatchIdByProfile", new[] { typeof(MongoId) });
                var resolved = byProfile?.Invoke(matches, new object[] { new MongoId(session) });
                if (resolved != null) matchId = resolved.ToString();
                var entries = matches.GetType().GetProperty("Matches")?.GetValue(matches) as IEnumerable;
                if (entries != null)
                    foreach (var entry in entries)
                    {
                        if (entry.GetType().GetProperty("Key")?.GetValue(entry)?.ToString() != matchId) continue;
                        var match = entry.GetType().GetProperty("Value")?.GetValue(entry);
                        var location = match?.GetType().GetProperty("LocationData")?.GetValue(match) as LocationBase;
                        return location != null && locationStates.TryGetValue(location, out state);
                    }
            }
            return states.TryGetValue(EventSession(session), out state);
        }

        public object Unlock(string session, LabsNewRoomsUnlock request)
        {
            lock (gate)
            {
                if (!TryRaidState(session, out var state) || state.raid != request.raid) return new { accepted = false };
                bool accepted = false;
                if (request.room == "lab06") accepted = request.action=="close"?state.CloseArsenal(request.raid,request.cycle):state.TryCode(request.raid, request.code);
                if (request.room == "lab04" && new[] { LabsNewRoomsIdentity.AishiMasterKey, LabsNewRoomsIdentity.AishiLimited, LabsNewRoomsIdentity.WhiteTusk }.Contains(request.card))
                {
                    if(state.room04Unlocked)return new {accepted=true,state};
                    if(!LabsNewRoomsIdentity.IsTemplateId(request.item))return new {accepted=false,state};
                    reservations.TryGetValue(state.raid,out var reservation);
                    if(request.prepare)
                    {
                        accepted=reservation==null||reservation.Item3<DateTime.UtcNow||reservation.Item1==session&&reservation.Item2==request.item;
                        if(accepted)reservations[state.raid]=Tuple.Create(session,request.item,DateTime.UtcNow.AddSeconds(30));
                    }
                    else if(reservation!=null&&reservation.Item1==session&&reservation.Item2==request.item)
                    {
                        accepted=true;state.room04Unlocked=true;state.readerSpark=request.card!=LabsNewRoomsIdentity.AishiMasterKey;state.revision++;reservations.Remove(state.raid);
                    }
                }
                return new { accepted, state };
            }
        }

        private void Populate(string session, LocationBase location)
        {
            var random = new Random(System.Security.Cryptography.RandomNumberGenerator.GetInt32(int.MaxValue));
            var additions = new List<SpawnpointTemplate>();
            foreach (var room in Array(_layout,"rooms"))
            {
                var active = new Dictionary<string, HashSet<string>>();
                foreach (var group in Array(room,"lootGroups"))
                {
                    var ids = Array(group,"pointIds").Select(x=>x.GetValue<string>()).OrderBy(x=>random.Next()).ToArray();
                    int count = Roll(Real(group,"spawnChance",100),random) ? random.Next(Number(group,"quantityMin"),Number(group,"quantityMax")+1) : 0;
                    active[Text(group,"id")] = new HashSet<string>(ids.Take(count));
                }
                foreach (var prop in Array(room,"props"))
                {
                    if (prop["loot"] == null) continue;
                    
                    string storage=Text(prop,"storageTemplate");if(storage.Length==0)storage=LabsNewRoomsIdentity.StorageTemplate(Text(prop,"assetId"));
                    var grid=RequireTemplate(storage).Properties?.Grids?.FirstOrDefault()??throw new InvalidDataException("Storage grid unavailable: "+storage);
                    int cellsH=Math.Max(1,Convert.ToInt32(grid.Properties.CellsH)),cellsV=Math.Max(1,Convert.ToInt32(grid.Properties.CellsV));
                    var generated = Generate(prop["loot"],random,cellsH,cellsV);
                    var root = new JsonObject { ["_id"] = Id(), ["_tpl"] = storage };
                    var contents = new JsonArray(root);
                    var occupied = new bool[cellsH,cellsV];
                    foreach (var tree in generated)
                    {
                        var nativeItems = _json.Deserialize<List<Item>>(tree.ToJsonString());
                        var size = _items.GetItemSize(nativeItems,nativeItems[0].Id);
                        int width = Math.Max(1,size?.Width ?? 1), height = Math.Max(1,size?.Height ?? 1);
                        bool placed = false;
                        for (int rotate = 0; rotate < 2 && !placed; rotate++)
                        {
                            int w = rotate == 0 ? width : height, h = rotate == 0 ? height : width;
                            for (int y = 0; y+h <= cellsV && !placed; y++)
                                for (int x = 0; x+w <= cellsH && !placed; x++)
                                {
                                    bool available = true;
                                    for(int xx=x;xx<x+w;xx++) for(int yy=y;yy<y+h;yy++) if(occupied[xx,yy]) available=false;
                                    if(!available) continue;
                                    for(int xx=x;xx<x+w;xx++) for(int yy=y;yy<y+h;yy++) occupied[xx,yy]=true;
                                    tree[0]["parentId"] = Text(root,"_id"); tree[0]["slotId"] = grid.Name;
                                    tree[0]["location"] = new JsonObject { ["x"]=x,["y"]=y,["r"]=rotate };
                                    foreach(var item in tree) contents.Add(item.DeepClone());
                                    placed = true;
                                }
                        }
                    }
                    additions.Add(Spawn("ISB_LABS_"+Text(prop,"id"),prop,contents,true));
                }
                foreach(var point in Array(room,"lootSpawns"))
                {
                    string group = Text(point["loot"],"selectionGroupId");
                    if(group.Length>0 && (!active.TryGetValue(group,out var selected)||!selected.Contains(Text(point,"id")))) continue;
                    AddLoose(additions,point,random);
                }
            }
            foreach(var point in Array(_layout,"mapLootSpawns")) AddLoose(additions,point,random);
            location.Loot = (location.Loot ?? Enumerable.Empty<SpawnpointTemplate>()).Where(x=>!x.Id?.StartsWith("ISB_LABS_",StringComparison.Ordinal) ?? true).Concat(additions).ToList();
            lock(gate)
            {
                if(states.Count>128) states.Clear();
                var raidState = LabsNewRoomsRaidState.Create(Guid.NewGuid().ToString("N"),(min,max)=>random.Next(min,max));
                locationStates.Remove(location);
                locationStates.Add(location, raidState);
                states[EventSession(session)] = raidState;
            }
        }

        private void AddLoose(List<SpawnpointTemplate> output, JsonNode point, Random random)
        {
            if(point["enabled"]?.GetValue<bool>()!=true || point["loot"]==null) return;
            var generated = Generate(point["loot"],random);
            for(int i=0;i<generated.Count;i++)
            {
                var spawn = Spawn("ISB_LABS_"+Text(point,"id")+"_"+i,point,generated[i],false);
                double radius=Real(point["loot"],"spreadRadius");
                if(i>0&&radius>0&&spawn.Position!=null)
                {
                    double angle=i*2.399963229728653;
                    var position=spawn.Position.Value;
                    spawn.Position=new SPTarkov.Server.Core.Models.Eft.Common.Vector3(position.X+(float)(Math.Cos(angle)*radius),position.Y,position.Z+(float)(Math.Sin(angle)*radius));
                }
                output.Add(spawn);
            }
        }

        private SpawnpointTemplate Spawn(string identity, JsonNode point, JsonArray items, bool container)
        {
            var node = new JsonObject
            {
                ["Id"]=identity,["IsContainer"]=container,["useGravity"]=false,["randomRotation"]=false,["IsAlwaysSpawn"]=true,
                ["IsGroupPosition"]=false,["GroupPositions"]=new JsonArray(),["Root"]=Text(items[0],"_id"),["Items"]=items.DeepClone(),
                ["Position"]=point["position"].DeepClone(),["Rotation"]=point["rotation"]?.DeepClone()??new JsonObject{["x"]=0,["y"]=Real(point,"rotationY"),["z"]=0}
            };
            return _json.Deserialize<SpawnpointTemplate>(node.ToJsonString());
        }

        private List<JsonArray> Generate(JsonNode rules, Random random,int cellsH=int.MaxValue,int cellsV=int.MaxValue)
        {
            var result = new List<JsonArray>();
            if(!Roll(Real(rules,"spawnChance",100),random)) return result;
            var pool = Array(_layout,"lootPools").Single(p=>Text(p,"id")==Text(rules,"poolId"));
            string filter = Text(rules,"entryFilter");
            var candidates = Array(pool,"entries").Where(e=>(filter.Length==0||Category(e)==filter)&&FitsStorage(e,cellsH,cellsV)).ToList();
            bool single=rules["singleDraw"]?.GetValue<bool>()==true;
            Action<JsonNode> append = entry =>
            {
                int count=single?1:random.Next(Number(entry,"min",1),Number(entry,"max",1)+1);
                int maximum=Text(entry,"kind")=="preset"?1:Math.Max(1,RequireTemplate(Text(entry,"ref")).Properties?.StackMaxSize??1);
                while(count>0){int stack=Math.Min(count,maximum);var tree=BuildTree(entry);tree[0]["upd"]??=new JsonObject();tree[0]["upd"]["StackObjectsCount"]=stack;result.Add(tree);count-=stack;}
            };
            if(!single)
                foreach(var guaranteed in candidates.Where(e=>e["guaranteed"]?.GetValue<bool>()==true).ToArray()) {append(guaranteed);candidates.Remove(guaranteed);}
            int rolls=single?1:random.Next(Number(rules,"rollsMin"),Number(rules,"rollsMax")+1);
            for(int i=0;i<rolls&&candidates.Count>0;i++)
            {
                double ticket=random.NextDouble()*candidates.Sum(e=>Real(e,"weight",1));
                var selected=candidates[candidates.Count-1];
                foreach(var entry in candidates) {ticket-=Real(entry,"weight",1);if(ticket<0){selected=entry;break;}}
                append(selected);
                if(rules["replacement"]?.GetValue<bool>()!=true) candidates.Remove(selected);
            }
            return result;
        }

        private readonly System.Collections.Concurrent.ConcurrentDictionary<string,(int Width,int Height)> lootSizes=new();

        private bool FitsStorage(JsonNode entry,int width,int height)
        {
            if(width==int.MaxValue&&height==int.MaxValue)return true;
            string key=Text(entry,"kind")+":"+Text(entry,"ref");
            if(!lootSizes.TryGetValue(key,out var size))
            {
                var tree=BuildTree(entry);var items=_json.Deserialize<List<Item>>(tree.ToJsonString());
                var native=_items.GetItemSize(items,items[0].Id);size=(Math.Max(1,native?.Width??1),Math.Max(1,native?.Height??1));lootSizes[key]=size;
            }
            return size.Width<=width&&size.Height<=height||size.Height<=width&&size.Width<=height;
        }

        private string Category(JsonNode entry)
        {
            string identity=Text(entry,"kind")=="preset"?Text(Array(_layout,"lootPresets").Single(p=>Text(p,"id")==Text(entry,"ref")),"rootTpl"):Text(entry,"ref");
            var template=RequireTemplate(identity);
            if(template.Properties?.WeapFireType?.Count>0)return "weapons";
            return template.Parent.ToString()=="543be5cb4bdc2deb348b4568"?"ammoBoxes":"any";
        }

        private JsonArray BuildTree(JsonNode entry)
        {
            string reference=Text(entry,"ref");
            var definition = Text(entry,"kind")=="preset" ? Array(_layout,"lootPresets").Single(p=>Text(p,"id")==reference) : Array(_layout,"lootItems").Single(p=>Text(p,"id")==reference);
            var items=definition["items"]?.DeepClone().AsArray() ?? new JsonArray(new JsonObject { ["_id"]="root",["_tpl"]=reference });
            var identities=items.ToDictionary(i=>Text(i,"_id"),i=>Id());
            foreach(var item in items)
            {
                string old=Text(item,"_id");item["_id"]=identities[old];
                string parent=Text(item,"parentId");
                if(identities.ContainsKey(parent))item["parentId"]=identities[parent];
                else {item.AsObject().Remove("parentId");item.AsObject().Remove("slotId");item.AsObject().Remove("location");}
                var template=RequireTemplate(Text(item,"_tpl"));
                var defaults=JsonNode.Parse(_json.Serialize(_items.GenerateUpdForItem(template)))?.AsObject()??new JsonObject();
                if(item["upd"] is JsonObject provided)foreach(var property in provided)defaults[property.Key]=property.Value?.DeepClone();
                if(defaults["Light"] is JsonObject light)light["IsActive"]=false;
                item["upd"]=defaults;
            }
            var root=items.Single(item=>item["parentId"]==null);
            if(!ReferenceEquals(root,items[0])) {items.Remove(root);items.Insert(0,root);}
            return items;
        }
    }
    public sealed class LabsNewRoomsUnlock : IRequestData
    {
        public string raid { get; set; } = "";
        public string room { get; set; } = "";
        public string card { get; set; } = "";
        public string code { get; set; } = "";
        public string item { get; set; } = "";
        public bool prepare { get; set; }
        public string action { get; set; } = "";
        public int cycle { get; set; }
    }

    [Injectable(TypePriority = OnLoadOrder.Routers + 1)]
    public sealed class LabsNewRoomsRouter : StaticRouter
    {
        public LabsNewRoomsRouter(JsonUtil json, HttpResponseUtil http, LabsNewRoomsRegistration rooms) : base(json, new List<RouteAction>
        {
            new RouteAction("/Aishi/miniLabs/layout", (url,info,session,output,token)=>new ValueTask<object>(http.NoBody(rooms.Snapshot(session,true)))),
            new RouteAction("/Aishi/miniLabs/state", (url,info,session,output,token)=>new ValueTask<object>(http.NoBody(rooms.Snapshot(session)))),
            new RouteAction<LabsNewRoomsUnlock>("/Aishi/miniLabs/unlock", (url,info,session,output,token)=>new ValueTask<string>(http.NoBody(rooms.Unlock(session,info))))
        }) { }
    }
#endif
}

#if ISB_LABS_CLIENT
namespace Aishi.LabsExpansion
{
    internal static class LabsNewRoomsSparks
    {
        private sealed class Burst
        {
            internal GameObject Root;
            internal Light Flash;
            internal float Started, NextPulse, LastPulse, Duration;
            internal string Token;
            internal bool Modified;
            internal ParticleSystem Particles;
            internal AudioSource Sound;
        }

        private static readonly List<Burst> Active = new List<Burst>(4);
        private static readonly HashSet<string> Played = new HashSet<string>(StringComparer.Ordinal);
        private static Material _material;
        private static AudioClip _clip;
        private static bool _failed;
        internal static void Play(KeycardDoor reader, bool modified, string token, float duration = 0f)
        {
            var player = Singleton<GameWorld>.Instance?.MainPlayer;
            if (_failed || reader == null || player == null || player.IsAI || string.IsNullOrEmpty(token) || !Played.Add(token))
                return;
            try
            {
                bool found = false;
                if (reader.Proxies != null)
                    foreach (var proxy in reader.Proxies)
                    {
                        if (proxy == null)
                            continue;
                        found = true;
                        var target = proxy.StatusLight != null ? proxy.StatusLight.transform : proxy.transform;
                        Emit(target.position + target.forward * .035f, target.rotation, player.Transform.position, modified, token, duration);
                    }

                if (!found)
                    Emit(reader.transform.position + Vector3.up * 1.15f, reader.transform.rotation, player.Transform.position, modified, token, duration);
            }
            catch
            {
                Reset();
                _failed = true;
            }
        }

        private static void Prepare()
        {
            if (_material != null)
                return;
            var shader = Shader.Find("Legacy Shaders/Particles/Additive");
            if (shader == null || !shader.isSupported)
                throw new InvalidOperationException("Reader spark shader unavailable.");
            _material = new Material(shader)
            {
                name = "ISB_ReaderSparks",
                mainTexture = Texture2D.whiteTexture
            };
            _material.SetColor("_TintColor", Color.white);
            const int sampleRate = 22050;
            var samples = new float[(int)(sampleRate * .32f)];
            var random = new System.Random(137);
            float previous = 0f;
            for (int i = 0; i < samples.Length; i++)
            {
                float t = i / (float)sampleRate;
                float noise = (float)random.NextDouble() * 2f - 1f;
                float envelope = (float)(Math.Exp(-t * 36f) + .38f * Math.Exp(-Math.Abs(t - .058f) * 170f) + .15f * Math.Exp(-Math.Abs(t - .115f) * 130f));
                samples[i] = Mathf.Clamp((noise - previous * .65f) * envelope * .4f, -.8f, .8f);
                previous = noise;
            }

            _clip = AudioClip.Create("ISB_ReaderElectricalArc", samples.Length, 1, sampleRate, false);
            _clip.SetData(samples, 0);
        }

        private static void Emit(Vector3 position, Quaternion rotation, Vector3 viewer, bool modified, string token, float duration)
        {
            if ((position - viewer).sqrMagnitude > 625f || Active.Count >= 4)
                return;
            Prepare();
            var root = new GameObject(modified ? "ISB_ReaderBatterySparks" : "ISB_ReaderBatteryShortCircuit");
            root.SetActive(false);
            var burst = new Burst
            {
                Root = root,
                Started = Time.realtimeSinceStartup,
                Modified = modified,
                Token = token,
                Duration = duration > 0f ? duration : modified ? 2f : 3f
            };
            Active.Add(burst);
            root.transform.SetPositionAndRotation(position, rotation);
            var particles = root.AddComponent<ParticleSystem>();
            var main = particles.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = burst.Duration;
            main.maxParticles = 96;
            main.startLifetime = new ParticleSystem.MinMaxCurve(.12f, .38f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(.8f, 2.6f);
            main.startSize = modified ? new ParticleSystem.MinMaxCurve(.006f, .018f) : new ParticleSystem.MinMaxCurve(.018f, .07f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, .42f, .09f), new Color(1f, .9f, .55f));
            main.gravityModifier = .55f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var emission = particles.emission;
            emission.rateOverTime = 0f;
            var shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 65f;
            shape.radius = .025f;
            var collision = particles.collision;
            collision.enabled = false;
            var renderer = particles.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = _material;
            renderer.renderMode = modified ? ParticleSystemRenderMode.Stretch : ParticleSystemRenderMode.Billboard;
            renderer.lengthScale = 2f;
            renderer.velocityScale = .045f;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            var light = root.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, .62f, .24f);
            light.range = modified ? .85f : 1.3f;
            light.intensity = 1.2f;
            light.shadows = LightShadows.None;
            burst.Flash = light;
            burst.Particles = particles;
            var sound = root.AddComponent<AudioSource>();
            sound.playOnAwake = false;
            sound.clip = _clip;
            sound.spatialBlend = 1f;
            sound.rolloffMode = AudioRolloffMode.Linear;
            sound.minDistance = .6f;
            sound.maxDistance = 12f;
            sound.dopplerLevel = 0f;
            sound.volume = modified ? .4f : .7f;
            burst.Sound = sound;
            root.SetActive(true);
            particles.Play(false);
            Pulse(burst, Time.realtimeSinceStartup);
        }

        private static void Pulse(Burst burst, float now)
        {
            burst.LastPulse = now;
            burst.NextPulse = now + (burst.Modified ? .28f : .42f);
            burst.Particles.Emit(burst.Modified ? 16 : 28);
            burst.Sound.Play();
        }

        internal static void Stop(string token)
        {
            for (int i = Active.Count - 1; i >= 0; i--)
                if (Active[i].Token == token)
                {
                    if (Active[i].Root != null)
                        UnityEngine.Object.Destroy(Active[i].Root);
                    Active.RemoveAt(i);
                }
        }

        internal static void Tick()
        {
            for (int i = Active.Count - 1; i >= 0; i--)
            {
                var burst = Active[i];
                float elapsed = Time.realtimeSinceStartup - burst.Started;
                float duration = burst.Duration;
                if (elapsed > duration + .4f || burst.Root == null)
                {
                    if (burst.Root != null)
                        UnityEngine.Object.Destroy(burst.Root);
                    Active.RemoveAt(i);
                }
                else
                {
                    if (elapsed < duration && Time.realtimeSinceStartup >= burst.NextPulse)
                        Pulse(burst, Time.realtimeSinceStartup);
                    if (burst.Flash != null)
                        burst.Flash.intensity = (burst.Modified ? 1.2f : 2.6f) * Mathf.Clamp01(1f - (Time.realtimeSinceStartup - burst.LastPulse) / (burst.Modified ? .09f : .16f));
                }
            }
        }

        internal static void Reset()
        {
            foreach (var burst in Active)
                if (burst.Root != null)
                    UnityEngine.Object.Destroy(burst.Root);
            Active.Clear();
            Played.Clear();
            if (_material != null)
                UnityEngine.Object.Destroy(_material);
            if (_clip != null)
                UnityEngine.Object.Destroy(_clip);
            _material = null;
            _clip = null;
            _failed = false;
        }
    }
}
#endif
