// Idle Adventurer - the first-person expedition view. The simulation decides everything; this file only shows it.
// Each dungeon floor is one real dungeon block built with no game loaded. A walkable route through the block is worked
// out with Unity's navigation, and a camera is placed along it from the simulation's clock: no bot, no physics.

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallConnect.Utility;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Items;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using DaggerfallWorkshop.Utility;

namespace IdleAdventurer
{
    // One dungeon block, its route, a camera and at most one enemy sprite.
    public class RailsScene
    {
        // Far from anything the game itself may have in the scene.
        static readonly Vector3 Origin = new Vector3(4000f, 0f, 4000f);
        const float EyeHeight = 1.55f;
        const float MinRoute = 80f;

        static List<string> blockNames;
        static readonly Dictionary<string, bool> blockWorks = new Dictionary<string, bool>();
        static readonly Dictionary<int, Texture2D[]> idleFrames = new Dictionary<int, Texture2D[]>();
        static readonly Dictionary<int, Texture2D[]> attackFrames = new Dictionary<int, Texture2D[]>();
        static readonly Dictionary<int, float> heights = new Dictionary<int, float>();

        public RenderTexture Target;
        public string BlockName;
        public float Length;
        public string Problem;

        GameObject root;
        GameObject block;
        GameObject eye;
        GameObject enemy;
        Material enemyMaterial;
        int enemyShown = -1;
        NavMeshDataInstance nav;
        bool navAdded;
        int agentType = -1;
        Color oldAmbient;
        Vector3[] route;
        float[] along;
        bool snap = true;

        public bool Ready
        {
            get { return route != null && eye != null; }
        }

        public Vector3 EnemyPosition
        {
            get { return (enemy != null) ? enemy.transform.position : Vector3.zero; }
        }

        public Camera Eye
        {
            get { return (eye != null) ? eye.GetComponent<Camera>() : null; }
        }

        public RailsScene()
        {
            root = new GameObject("IdleRails");
            root.transform.position = Origin;

            eye = new GameObject("Eye");
            eye.transform.parent = root.transform;
            Camera cam = eye.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.fieldOfView = 65f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 200f;
            cam.depth = -50f;
            Target = new RenderTexture(Screen.width, Screen.height, 24);
            cam.targetTexture = Target;
            Light torch = eye.AddComponent<Light>();
            torch.type = LightType.Point;
            torch.range = 14f;
            torch.intensity = 1.1f;
            torch.color = new Color(1f, 0.9f, 0.75f);

            oldAmbient = RenderSettings.ambientLight;
            RenderSettings.ambientLight = new Color(0.22f, 0.22f, 0.24f);

            NavMeshBuildSettings settings = NavMesh.CreateSettings();
            agentType = settings.agentTypeID;
        }

        public void Dispose()
        {
            Unload();
            if (agentType >= 0)
                NavMesh.RemoveSettings(agentType);
            agentType = -1;
            if (root != null)
                UnityEngine.Object.Destroy(root);
            root = null;
            eye = null;
            if (Target != null)
            {
                Target.Release();
                UnityEngine.Object.Destroy(Target);
            }
            Target = null;
            RenderSettings.ambientLight = oldAmbient;
        }

        void Unload()
        {
            if (navAdded)
                nav.Remove();
            navAdded = false;
            if (block != null)
                UnityEngine.Object.Destroy(block);
            block = null;
            route = null;
            HideEnemy();
        }

        // ------------------------------------------------------------------ choosing and building a floor

        static List<string> Names()
        {
            if (blockNames != null)
                return blockNames;
            blockNames = new List<string>();
            BlocksFile file = DaggerfallUnity.Instance.ContentReader.BlockFileReader;
            for (int i = 0; i < file.Count; i++)
            {
                string name = file.GetBlockName(i);
                if (!string.IsNullOrEmpty(name) && name.StartsWith("N") && name.EndsWith(".RDB"))
                    blockNames.Add(name);
            }
            return blockNames;
        }

        // Builds the floor for a dungeon seed. Blocks whose longest route is too short are skipped and remembered.
        public void LoadFloor(int seed, int floor)
        {
            System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
            List<string> names = Names();
            int textures = seed * 31 + 7;
            uint pick = (uint)(seed * 7919 + floor * 104729);
            string tried = "";
            for (int attempt = 0; attempt < 8 && names.Count > 0; attempt++)
            {
                string name = names[(int)((pick + (uint)attempt * 2654435761u) % (uint)names.Count)];
                bool known;
                if (blockWorks.TryGetValue(name, out known) && !known)
                    continue;
                bool ok = false;
                try
                {
                    ok = Load(name, textures);
                }
                catch (Exception ex)
                {
                    Debug.Log("[Idle] VIEW block " + name + " failed: " + ex.Message);
                }
                blockWorks[name] = ok;
                tried += name + (ok ? " ok " : " no ");
                if (ok)
                {
                    Debug.Log("[Idle] VIEW floor " + floor + ": " + tried + "route " + (int)Length + " m, " + watch.ElapsedMilliseconds + " ms");
                    return;
                }
            }
            // Nothing suitable: fall back to the one block known to work.
            try
            {
                if (!Load("S0000999.RDB", textures))
                    Problem = "No usable dungeon block was found";
            }
            catch (Exception ex)
            {
                Problem = ex.Message;
            }
            Debug.Log("[Idle] VIEW floor " + floor + " fell back: " + tried + watch.ElapsedMilliseconds + " ms");
        }

        bool Load(string name, int textureSeed)
        {
            Unload();
            snap = true;
            BlockName = name;

            // Models, lights and decorations, but no doors, enemies or player.
            int[] textureTable = DungeonTextureTables.RandomTextureTableClassic(textureSeed);
            Dictionary<int, RDBLayout.ActionLink> links = new Dictionary<int, RDBLayout.ActionLink>();
            DFBlock data;
            block = RDBLayout.CreateBaseGameObject(name, links, out data, textureTable, false, null, false);
            if (block == null)
                return false;
            block.transform.parent = root.transform;
            block.transform.localPosition = Vector3.zero;
            RDBLayout.AddLights(block, ref data);
            try
            {
                DFBlock.RdbObject[] editorObjects;
                GameObject[] starts;
                GameObject[] enters;
                RDBLayout.AddFlats(block, links, ref data, out editorObjects, out starts, out enters, DFRegion.DungeonTypes.OrcStronghold);
            }
            catch (Exception ex)
            {
                Debug.Log("[Idle] VIEW decorations of " + name + ": " + ex.Message);
            }
            Physics.SyncTransforms();

            // The walls and floors near the block set the working volume; anything stray is ignored.
            Collider[] colliders = block.GetComponentsInChildren<Collider>();
            Bounds bounds = new Bounds(Origin, Vector3.zero);
            bool any = false;
            for (int i = 0; i < colliders.Length; i++)
            {
                Bounds b = colliders[i].bounds;
                if ((b.center - Origin).magnitude > 300f || b.size.x > 200f || b.size.z > 200f)
                    continue;
                if (any)
                    bounds.Encapsulate(b);
                else
                    bounds = b;
                any = true;
            }
            if (!any)
                return false;
            bounds.Expand(2f);

            NavMeshBuildSettings settings = NavMesh.GetSettingsByID(agentType);
            settings.agentRadius = 0.25f;
            settings.agentHeight = 1.7f;
            settings.agentClimb = 0.45f;
            settings.agentSlope = 50f;
            settings.overrideVoxelSize = true;
            settings.voxelSize = 0.08f;
            List<NavMeshBuildSource> sources = new List<NavMeshBuildSource>();
            NavMeshBuilder.CollectSources(bounds, ~0, NavMeshCollectGeometry.PhysicsColliders, 0, new List<NavMeshBuildMarkup>(), sources);
            NavMeshData mesh = NavMeshBuilder.BuildNavMeshData(settings, sources, bounds, Vector3.zero, Quaternion.identity);
            if (mesh == null)
                return false;
            nav = NavMesh.AddNavMeshData(mesh);
            navAdded = true;

            NavMeshQueryFilter filter = new NavMeshQueryFilter();
            filter.agentTypeID = agentType;
            filter.areaMask = NavMesh.AllAreas;

            // Walkable spots on a coarse grid. Blocks have separate areas (ledges, sealed rooms), so the route is
            // looked for from a few different spots and the longest one wins.
            List<Vector3> spots = new List<Vector3>();
            for (float y = bounds.min.y; y <= bounds.max.y; y += 2f)
                for (float x = bounds.min.x; x <= bounds.max.x; x += 3f)
                    for (float z = bounds.min.z; z <= bounds.max.z; z += 3f)
                    {
                        NavMeshHit spot;
                        if (NavMesh.SamplePosition(new Vector3(x, y, z), out spot, 1.5f, filter))
                            spots.Add(spot.position);
                    }
            if (spots.Count < 2)
                return false;

            Vector3[] best = null;
            float bestLength = 0f;
            for (int seedSpot = 0; seedSpot < 5; seedSpot++)
            {
                Vector3 from = spots[(seedSpot * spots.Count) / 5];
                Vector3[] first = Farthest(from, spots, filter);
                if (first == null)
                    continue;
                // From the far end of that walk, the farthest spot again: the two ends of the area.
                Vector3[] second = Farthest(first[first.Length - 1], spots, filter);
                Vector3[] corners = (second != null && PathLength(second) > PathLength(first)) ? second : first;
                float sum = PathLength(corners);
                if (sum > bestLength)
                {
                    bestLength = sum;
                    best = corners;
                }
            }
            if (best == null || bestLength < MinRoute)
                return false;

            route = Centre(best, filter);
            along = new float[route.Length];
            for (int i = 1; i < route.Length; i++)
                along[i] = along[i - 1] + Vector3.Distance(route[i - 1], route[i]);
            Length = along[route.Length - 1];
            return true;
        }

        static float PathLength(Vector3[] corners)
        {
            float sum = 0f;
            for (int i = 1; i < corners.Length; i++)
                sum += Vector3.Distance(corners[i - 1], corners[i]);
            return sum;
        }

        static Vector3[] Farthest(Vector3 from, List<Vector3> spots, NavMeshQueryFilter filter)
        {
            Vector3[] best = null;
            float bestLength = 0f;
            NavMeshPath path = new NavMeshPath();
            for (int i = 0; i < spots.Count; i++)
            {
                if (!NavMesh.CalculatePath(from, spots[i], filter, path) || path.status != NavMeshPathStatus.PathComplete)
                    continue;
                Vector3[] corners = path.corners;
                float sum = PathLength(corners);
                if (sum > bestLength)
                {
                    bestLength = sum;
                    best = corners;
                }
            }
            return (best != null && best.Length >= 2) ? best : null;
        }

        // Navigation routes cut corners tight against the walls. This resamples the route every half metre, pushes
        // each point away from the nearest wall where there is room, and smooths the result.
        static Vector3[] Centre(Vector3[] corners, NavMeshQueryFilter filter)
        {
            const float step = 0.5f;
            const float clearance = 0.75f;
            List<Vector3> points = new List<Vector3>();
            for (int i = 1; i < corners.Length; i++)
            {
                float span = Vector3.Distance(corners[i - 1], corners[i]);
                int pieces = Mathf.Max(1, Mathf.CeilToInt(span / step));
                for (int n = (i == 1) ? 0 : 1; n <= pieces; n++)
                    points.Add(Vector3.Lerp(corners[i - 1], corners[i], n / (float)pieces));
            }

            for (int pass = 0; pass < 3; pass++)
            {
                for (int i = 1; i < points.Count - 1; i++)
                {
                    NavMeshHit edge;
                    if (!NavMesh.FindClosestEdge(points[i], out edge, filter) || edge.distance >= clearance)
                        continue;
                    Vector3 away = points[i] - edge.position;
                    away.y = 0f;
                    if (away.sqrMagnitude < 0.0001f)
                        away = edge.normal;
                    NavMeshHit spot;
                    if (NavMesh.SamplePosition(points[i] + away.normalized * (clearance - edge.distance), out spot, 0.4f, filter))
                        points[i] = spot.position;
                }
                for (int i = 1; i < points.Count - 1; i++)
                {
                    NavMeshHit spot;
                    Vector3 mean = (points[i - 1] + points[i] * 2f + points[i + 1]) / 4f;
                    if (NavMesh.SamplePosition(mean, out spot, 0.4f, filter))
                        points[i] = spot.position;
                }
            }
            return points.ToArray();
        }

        // ------------------------------------------------------------------ placing things on the route

        public Vector3 PointAt(float distance)
        {
            distance = Mathf.Clamp(distance, 0f, Length);
            for (int i = 1; i < route.Length; i++)
            {
                if (distance <= along[i])
                {
                    float span = Mathf.Max(0.0001f, along[i] - along[i - 1]);
                    return Vector3.Lerp(route[i - 1], route[i], (distance - along[i - 1]) / span);
                }
            }
            return route[route.Length - 1];
        }

        static Vector3 Level(Vector3 v)
        {
            v.y *= 0.35f;
            return (v.sqrMagnitude < 0.0001f) ? Vector3.forward : v;
        }

        // Puts the camera at a distance along the route, looking the way it walks or at the enemy.
        public void PlaceEye(float distance, int direction, bool faceEnemy, float dt)
        {
            if (!Ready)
                return;
            Vector3 here = PointAt(distance) + Vector3.up * EyeHeight;
            Vector3 look = PointAt(distance + direction * 1.8f) + Vector3.up * EyeHeight - here;
            if (faceEnemy && enemy != null)
                look = enemy.transform.position - here;
            Quaternion want = Quaternion.LookRotation(Level(look), Vector3.up);
            if (snap || (here - eye.transform.position).magnitude > 6f)
                eye.transform.rotation = want;
            else
                eye.transform.rotation = Quaternion.Slerp(eye.transform.rotation, want, Mathf.Clamp01(dt * 4f));
            eye.transform.position = here;
            snap = false;
        }

        static Texture2D[] Frames(string file, int record)
        {
            ImageData all = ImageReader.GetImageData(file, record, 0, true, true, true);
            if (all.animatedTextures != null && all.animatedTextures.Length > 1)
                return all.animatedTextures;
            // Some file types only hand back one frame at a time.
            List<Texture2D> list = new List<Texture2D>();
            for (int frame = 0; frame < 12; frame++)
            {
                Texture2D texture = null;
                try
                {
                    texture = ImageReader.GetImageData(file, record, frame, true, true, false).texture;
                }
                catch (Exception)
                {
                    texture = null;
                }
                if (texture == null)
                    break;
                list.Add(texture);
            }
            return (list.Count > 0) ? list.ToArray() : null;
        }

        // The game's own size for a sprite: its pixel height, the scale stored with the image, 40 units to the metre.
        static float SpriteHeight(string file, int record, int fallbackPixels)
        {
            float pixels = fallbackPixels;
            try
            {
                TextureFile texture = new TextureFile();
                if (texture.Load(System.IO.Path.Combine(DaggerfallUnity.Instance.Arena2Path, file), FileUsage.UseMemory, true))
                {
                    DFSize size = texture.GetSize(record);
                    DFSize scale = texture.GetScale(record);
                    if (size.Height > 0)
                        pixels = size.Height + (int)(size.Height * (scale.Height / BlocksFile.ScaleDivisor));
                }
            }
            catch (Exception)
            {
            }
            return Mathf.Clamp(pixels * MeshReader.GlobalScale, 0.3f, 4f);
        }

        public static Texture2D[] WeaponFrames(WeaponTypes type, int record)
        {
            try
            {
                return Frames(WeaponBasics.GetWeaponFilename(type), record);
            }
            catch (Exception)
            {
                return null;
            }
        }

        // Stands an enemy on the route as a flat sprite that always faces the camera.
        public void ShowEnemy(int id, float distance, bool attacking)
        {
            if (!Ready)
                return;
            Texture2D[] idle;
            if (!idleFrames.TryGetValue(id, out idle))
            {
                EnemyInfo info = IdleSim.Enemy(id);
                string file = TextureFile.IndexToFileName(info.def.MaleTexture);
                try
                {
                    idle = Frames(file, info.def.HasIdle ? 15 : 0);
                    attackFrames[id] = Frames(file, 5);
                }
                catch (Exception ex)
                {
                    Debug.Log("[Idle] VIEW sprite for enemy " + id + ": " + ex.Message);
                    idle = null;
                    attackFrames[id] = null;
                }
                idleFrames[id] = idle;
                heights[id] = SpriteHeight(file, info.def.HasIdle ? 15 : 0, (idle != null && idle.Length > 0) ? idle[0].height : 64);
            }
            if (idle == null || idle.Length == 0)
                return;

            if (enemy == null)
            {
                enemy = GameObject.CreatePrimitive(PrimitiveType.Quad);
                enemy.name = "Enemy";
                UnityEngine.Object.Destroy(enemy.GetComponent<Collider>());
                enemy.transform.parent = root.transform;
                enemyMaterial = MaterialReader.CreateBillboardMaterial();
                enemy.GetComponent<MeshRenderer>().sharedMaterial = enemyMaterial;
            }
            Texture2D[] attack = attackFrames[id];
            Texture2D[] frames = (attacking && attack != null && attack.Length > 0) ? attack : idle;
            Texture2D frame = frames[(int)(Time.unscaledTime * 7f) % frames.Length];
            enemyMaterial.mainTexture = frame;

            float tall = heights[id];
            float wide = tall * frame.width / Mathf.Max(1, frame.height);
            if (enemyShown != id || !enemy.activeSelf)
            {
                // Flying things hover at chest height instead of standing on the floor.
                float lift = (IdleSim.Enemy(id).def.Behaviour == MobileBehaviour.Flying) ? 0.8f : 0f;
                enemy.transform.position = PointAt(distance) + Vector3.up * (tall / 2f + lift);
                enemyShown = id;
                enemy.SetActive(true);
            }
            enemy.transform.localScale = new Vector3(wide, tall, 1f);
            Vector3 toEye = eye.transform.position - enemy.transform.position;
            toEye.y = 0f;
            if (toEye.sqrMagnitude > 0.001f)
                enemy.transform.rotation = Quaternion.LookRotation(-toEye, Vector3.up);
        }

        public void HideEnemy()
        {
            if (enemy != null)
                enemy.SetActive(false);
            enemyShown = -1;
        }
    }

    // ---------------------------------------------------------------------------------------
    // The window: advances the simulation, turns its state into a camera position and its events into animation.
    // ---------------------------------------------------------------------------------------

    public class IdleViewWindow : DaggerfallPopupWindow
    {
        const float WalkSpeed = 3f;
        const float FightGap = 2.3f;
        const int BtnSpeed = 1, BtnRecall = 2, BtnBack = 3, BtnGo = 4;

        class Floater
        {
            public string text;
            public float born;
            public bool onHero;
        }

        ViewCanvas canvas;
        RailsScene scene;
        int loadedSeed;
        int loadedFloor = -1;
        int loadIn;
        long seen;
        float swingUntil;
        float enemyAttackUntil;
        float hurtUntil;
        readonly List<Floater> floaters = new List<Floater>();
        WeaponTypes weaponType = WeaponTypes.None;
        Texture2D weaponIdle;
        Texture2D[] weaponSwing;

        public IdleViewWindow(IUserInterfaceManager uiManager, IUserInterfaceWindow previous)
            : base(uiManager, previous)
        {
        }

        protected override void Setup()
        {
            NativePanel.BackgroundColor = Color.clear;
            canvas = new ViewCanvas();
            canvas.Owner = this;
            canvas.AutoSize = AutoSizeModes.None;
            ParentPanel.Components.Add(canvas);
        }

        public override void OnPush()
        {
            base.OnPush();
            if (IdleStore.W == null)
                IdleStore.Load();
            IdleStore.W.record = true;
            seen = IdleStore.W.eventTotal;
            try
            {
                scene = new RailsScene();
            }
            catch (Exception ex)
            {
                Debug.Log("[Idle] VIEW could not start: " + ex);
                scene = null;
            }
        }

        public override void OnPop()
        {
            base.OnPop();
            if (scene != null)
                scene.Dispose();
            scene = null;
            IdleStore.Save();
        }

        static bool InDungeon(IdleSave s)
        {
            return (s.phase == IdleData.PhaseExplore || s.phase == IdleData.PhaseFight) && s.floor >= 1;
        }

        void PickWeapon(IdleWorld w)
        {
            WeaponTypes want = WeaponTypes.Melee;
            DaggerfallUnityItem item = w.gear[IdleData.SlotWeapon];
            if (item != null && item.currentCondition > 0)
            {
                int skill = item.GetWeaponSkillIDAsShort();
                if (skill == (int)DFCareer.Skills.LongBlade) want = WeaponTypes.LongBlade;
                else if (skill == (int)DFCareer.Skills.ShortBlade) want = WeaponTypes.Dagger;
                else if (skill == (int)DFCareer.Skills.Axe) want = WeaponTypes.Battleaxe;
                else if (skill == (int)DFCareer.Skills.BluntWeapon) want = WeaponTypes.Mace;
            }
            if (want == weaponType)
                return;
            weaponType = want;
            Texture2D[] idle = RailsScene.WeaponFrames(want, 0);
            weaponIdle = (idle != null && idle.Length > 0) ? idle[0] : null;
            weaponSwing = RailsScene.WeaponFrames(want, 1);
        }

        public override void Update()
        {
            base.Update();
            IdleWorld w = IdleStore.W;
            if (canvas == null || w == null)
                return;
            canvas.Size = new Vector2(Screen.width, Screen.height);
            IdleSave s = w.s;

            int click = canvas.Buttons.Clicked();
            if (click >= 0)
                DaggerfallUI.Instance.PlayOneShot(SoundClips.ButtonClick);
            if (Input.GetKeyDown(KeyCode.F) || click == BtnSpeed)
                IdleClock.NextSpeed();
            if ((Input.GetKeyDown(KeyCode.H) || click == BtnRecall) && s.phase != IdleData.PhaseTown && s.phase != IdleData.PhaseBack)
            {
                s.recall = 1;
                w.Emit(IdleData.EvRetreat, "You call " + s.heroName + " home");
            }
            if ((Input.GetKeyDown(KeyCode.G) || click == BtnGo) && s.phase == IdleData.PhaseTown)
                IdleSim.StartExpedition(w);
            if (click == BtnBack)
            {
                CloseWindow();
                return;
            }

            // A new floor is built while the screen is dark: one dark frame first, then the work.
            bool dungeon = InDungeon(s) && scene != null;
            if (dungeon && (loadedFloor != s.floor || loadedSeed != s.dungeonSeed))
            {
                if (loadIn == 0)
                    loadIn = 2;
                loadIn--;
                if (loadIn > 0)
                    return;
                scene.LoadFloor(s.dungeonSeed, s.floor);
                loadedFloor = s.floor;
                loadedSeed = s.dungeonSeed;
                loadIn = 0;
                seen = w.eventTotal;
            }

            IdleClock.Tick();
            float now = Time.unscaledTime;

            // What happened since the last frame.
            long fresh = w.eventTotal - seen;
            seen = w.eventTotal;
            int first = (int)Math.Max(0, w.events.Count - fresh);
            for (int i = first; i < w.events.Count; i++)
            {
                IdleEvent e = w.events[i];
                if (e.type == IdleData.EvHeroHit || e.type == IdleData.EvHeroMiss)
                    swingUntil = now + 0.45f;
                if (e.type == IdleData.EvEnemyHit || e.type == IdleData.EvEnemyMiss)
                    enemyAttackUntil = now + 0.5f;
                if (e.type == IdleData.EvEnemyHit)
                    hurtUntil = now + 0.25f;
                if ((e.type == IdleData.EvHeroHit || e.type == IdleData.EvEnemyHit) && floaters.Count < 12)
                {
                    Floater f = new Floater();
                    f.text = (e.value > 0) ? "-" + e.value : "0";
                    f.born = now;
                    f.onHero = e.type == IdleData.EvEnemyHit;
                    floaters.Add(f);
                }
            }
            for (int i = floaters.Count - 1; i >= 0; i--)
                if (now - floaters[i].born > 1.1f)
                    floaters.RemoveAt(i);

            if (!dungeon || !scene.Ready)
                return;
            PickWeapon(w);

            // Position: time walked on this floor, back and forth along the route.
            float walked = s.floorTicks / (float)IdleData.TicksPerSecond * WalkSpeed;
            float lap = Mathf.Max(1f, scene.Length);
            int laps = (int)(walked / lap);
            float within = walked - laps * lap;
            int direction = (laps % 2 == 0) ? 1 : -1;
            float distance = (direction > 0) ? within : lap - within;

            bool fighting = s.phase == IdleData.PhaseFight && s.enemyId >= 0;
            if (fighting)
            {
                float at = distance + direction * FightGap;
                if (at < 0f || at > lap)
                    at = distance - direction * FightGap;
                scene.ShowEnemy(s.enemyId, at, now < enemyAttackUntil);
            }
            else
            {
                scene.HideEnemy();
            }
            scene.PlaceEye(distance, direction, fighting, Mathf.Min(0.1f, Time.unscaledDeltaTime) * Mathf.Min(4, IdleClock.Speed));
        }

        class ViewCanvas : BaseScreenComponent
        {
            public IdleViewWindow Owner;
            public readonly IdleButtons Buttons = new IdleButtons();

            public override void Draw()
            {
                base.Draw();
                IdleViewWindow o = Owner;
                IdleWorld w = IdleStore.W;
                if (w == null)
                    return;
                IdleSave s = w.s;
                float W = Screen.width;
                float H = Screen.height;
                float now = Time.unscaledTime;
                float unit = H / 200f;
                float line = Mathf.Round(H * 0.034f);
                float sc = IdleUi.Scale(line * 0.88f);
                float pad = Mathf.Round(H * 0.025f);

                IdleUi.Box(new Rect(0, 0, W, H), Color.black);
                bool dungeon = InDungeon(s) && o.scene != null && o.scene.Ready && o.loadedFloor == s.floor && o.loadedSeed == s.dungeonSeed;
                if (dungeon)
                {
                    GUI.DrawTexture(new Rect(0, 0, W, H), o.scene.Target, ScaleMode.StretchToFill, false);

                    // The weapon, drawn the way the game draws it: a flat image over the view, sized from a 320x200 screen.
                    bool swinging = now < o.swingUntil && o.weaponSwing != null && o.weaponSwing.Length > 0;
                    Texture2D weapon = o.weaponIdle;
                    if (swinging)
                        weapon = o.weaponSwing[Mathf.Clamp((int)((1f - (o.swingUntil - now) / 0.45f) * o.weaponSwing.Length), 0, o.weaponSwing.Length - 1)];
                    if (weapon != null)
                    {
                        float ww = weapon.width * unit;
                        float wh = weapon.height * unit;
                        DaggerfallUI.DrawTexture(new Rect(W * 0.5f + (swinging ? 0f : 60f * unit) - ww * 0.5f, H - wh, ww, wh), weapon, ScaleMode.StretchToFill, true);
                    }
                    if (now < o.hurtUntil)
                        IdleUi.Box(new Rect(0, 0, W, H), new Color(0.8f, 0f, 0f, 0.28f * (o.hurtUntil - now) / 0.25f));

                    // Damage numbers drift up from the enemy, or from the health bar when the hero is hit.
                    for (int i = 0; i < o.floaters.Count; i++)
                    {
                        Floater f = o.floaters[i];
                        float age = now - f.born;
                        float fx = f.onHero ? pad + line * 4f : W * 0.5f + ((i % 3) - 1) * line * 1.5f;
                        float fy = (f.onHero ? H - pad - line * 4f : H * 0.36f) - age * line * 2.2f;
                        Color c = f.onHero ? IdleUi.Bad : IdleUi.White;
                        c.a = Mathf.Clamp01(1.4f - age * 1.3f);
                        IdleUi.Centred(f.text, fx, fy, sc * 1.5f, c);
                    }

                    if (s.phase == IdleData.PhaseFight && s.enemyId >= 0)
                    {
                        string name = IdleSim.Enemy(s.enemyId).name;
                        float bw = Mathf.Round(W * 0.26f);
                        IdleUi.Centred(name, W * 0.5f, pad, sc * 1.2f, IdleUi.Gold);
                        IdleUi.Bar(new Rect((W - bw) / 2f, pad + line * 1.25f, bw, Mathf.Round(line * 0.35f)), (float)s.enemyHealth / Mathf.Max(1, s.enemyMaxHealth), IdleUi.Line);
                    }
                    IdleUi.Text("Floor " + s.floor + " of " + s.targetFloor + "    " + s.roomsLeft + " fight(s) left", pad, pad, sc, IdleUi.Gold);
                }
                else
                {
                    // Not in a dungeon (or the floor is still being built): a plain card.
                    string title = IdleSim.PhaseName(s);
                    if (InDungeon(s))
                        title = (o.scene != null && o.scene.Problem != null) ? o.scene.Problem : "Entering floor " + s.floor + "...";
                    IdleUi.Centred(title, W * 0.5f, H * 0.4f, sc * 1.6f, IdleUi.Gold);
                    if (s.phase == IdleData.PhaseOut || s.phase == IdleData.PhaseBack)
                    {
                        float bw = Mathf.Round(W * 0.3f);
                        IdleUi.Bar(new Rect((W - bw) / 2f, H * 0.4f + line * 2.2f, bw, Mathf.Round(line * 0.4f)),
                            1f - (float)s.phaseLeft / Mathf.Max(1, s.phaseTotal), IdleUi.Line);
                    }
                    else if (s.phase == IdleData.PhaseTown)
                    {
                        IdleUi.Centred("Gold " + s.gold + "    Level " + s.level + "    Runs " + s.expeditions, W * 0.5f, H * 0.4f + line * 2.2f, sc, IdleUi.White);
                    }
                }

                // Hero health, bottom left.
                float hw = Mathf.Round(W * 0.22f);
                float tall = Mathf.Round(line * 1.25f);
                float hy = H - pad - tall - line * 1.9f;
                IdleUi.Text(s.heroName + "  " + Mathf.Max(0, s.health) + "/" + s.maxHealth, pad, hy, sc, IdleUi.White);
                IdleUi.Bar(new Rect(pad, hy + line, hw, Mathf.Round(line * 0.4f)), (float)s.health / Mathf.Max(1, s.maxHealth), IdleUi.Bad);

                // The last few events, right side.
                int rows = 7;
                float lw = Mathf.Round(W * 0.34f);
                float ly = pad + line * 0.3f;
                IdleUi.Box(new Rect(W - pad - lw - line * 0.4f, ly - line * 0.3f, lw + line * 0.8f, rows * line * 0.85f + line * 0.5f), IdleUi.Glass);
                int from = Mathf.Max(0, w.events.Count - rows);
                for (int i = from; i < w.events.Count; i++)
                {
                    string text = w.events[i].text;
                    while (text.Length > 8 && IdleUi.Width(text, sc * 0.8f) > lw)
                        text = text.Substring(0, text.Length - 4);
                    IdleUi.Text(text, W - pad - lw, ly, sc * 0.8f, IdleUi.EventColor(w.events[i].type));
                    ly += line * 0.85f;
                }

                // Buttons along the bottom.
                float gap = Mathf.Round(line * 0.3f);
                float bx = pad;
                float by = H - pad - tall;
                Buttons.Begin();
                bx += Buttons.Add(BtnSpeed, "Speed " + IdleClock.Speed + "x (F)", bx, by, tall, false) + gap;
                if (s.phase == IdleData.PhaseTown)
                    bx += Buttons.Add(BtnGo, "Send out (G)", bx, by, tall, false) + gap;
                else if (s.phase != IdleData.PhaseBack)
                    bx += Buttons.Add(BtnRecall, "Call home (H)", bx, by, tall, s.recall != 0) + gap;
                Buttons.Add(BtnBack, "Back (Esc)", bx, by, tall, false);
            }
        }
    }
}
