// Idle Adventurer - first-person "rails" spike. One real dungeon block is built with no game loaded, a walkable
// route through it is worked out automatically, and a camera follows the route: no bot, no player, no physics.

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using DaggerfallWorkshop.Utility;

namespace IdleAdventurer
{
    public class IdleRailsWindow : DaggerfallPopupWindow
    {
        // Far from anything the game itself may have in the scene.
        static readonly Vector3 Origin = new Vector3(4000f, 0f, 4000f);
        const float EyeHeight = 1.55f;
        const float WalkSpeed = 3.2f;
        const float FightSeconds = 5f;

        RailsCanvas canvas;
        readonly List<string> notes = new List<string>();

        GameObject root;
        GameObject eye;
        GameObject enemy;
        Camera cam;
        RenderTexture target;
        NavMeshDataInstance nav;
        bool navAdded;
        int agentType = -1;
        Color oldAmbient;
        bool ambientChanged;

        Vector3[] route;
        float[] along;
        float length;
        float travelled;
        int direction = 1;
        int speed = 1;
        float enemyAt = -1f;
        bool enemyAlive;
        float fightLeft;
        Texture2D[] enemyIdle;
        Texture2D[] enemyAttack;
        Texture2D weaponIdle;
        Texture2D[] weaponSwing;

        public IdleRailsWindow(IUserInterfaceManager uiManager, IUserInterfaceWindow previous)
            : base(uiManager, previous)
        {
        }

        protected override void Setup()
        {
            NativePanel.BackgroundColor = Color.clear;
            canvas = new RailsCanvas();
            canvas.Owner = this;
            canvas.AutoSize = AutoSizeModes.None;
            ParentPanel.Components.Add(canvas);
        }

        void Note(bool ok, string text)
        {
            notes.Add((ok ? "OK  " : "FAIL  ") + text);
            Debug.Log("[Idle] RAILS " + (ok ? "OK  " : "FAIL  ") + text);
        }

        public override void OnPush()
        {
            base.OnPush();
            notes.Clear();
            try
            {
                Build();
            }
            catch (Exception ex)
            {
                Note(false, ex.ToString());
            }
        }

        public override void OnPop()
        {
            base.OnPop();
            if (navAdded)
                nav.Remove();
            navAdded = false;
            if (agentType >= 0)
                NavMesh.RemoveSettings(agentType);
            agentType = -1;
            if (root != null)
                UnityEngine.Object.Destroy(root);
            root = null;
            if (target != null)
            {
                target.Release();
                UnityEngine.Object.Destroy(target);
            }
            target = null;
            if (ambientChanged)
                RenderSettings.ambientLight = oldAmbient;
            ambientChanged = false;
        }

        // ------------------------------------------------------------------ building the scene

        static string PickBlock()
        {
            DFLocation location;
            if (DaggerfallUnity.Instance.ContentReader.GetLocation("Daggerfall", "Privateer's Hold", out location)
                && location.HasDungeon && location.Dungeon.Blocks != null)
            {
                for (int i = 0; i < location.Dungeon.Blocks.Length; i++)
                    if (location.Dungeon.Blocks[i].IsStartingBlock)
                        return location.Dungeon.Blocks[i].BlockName;
            }
            return "N0000000.RDB";
        }

        void Build()
        {
            System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
            root = new GameObject("IdleRails");
            root.transform.position = Origin;

            // 1. The block: models, lights and decorations, but no doors, enemies or player.
            string blockName = PickBlock();
            Dictionary<int, RDBLayout.ActionLink> links = new Dictionary<int, RDBLayout.ActionLink>();
            DFBlock data;
            GameObject block = RDBLayout.CreateBaseGameObject(blockName, links, out data, null, false, null, false);
            if (block == null)
            {
                Note(false, "Could not build block " + blockName);
                return;
            }
            block.transform.parent = root.transform;
            block.transform.localPosition = Vector3.zero;
            RDBLayout.AddLights(block, ref data);
            DFBlock.RdbObject[] editorObjects;
            GameObject[] starts;
            GameObject[] enters;
            try
            {
                RDBLayout.AddFlats(block, links, ref data, out editorObjects, out starts, out enters, DFRegion.DungeonTypes.HumanStronghold);
            }
            catch (Exception ex)
            {
                starts = null;
                Note(false, "Decorations: " + ex.Message);
            }
            Physics.SyncTransforms();

            Renderer[] renderers = block.GetComponentsInChildren<Renderer>();
            Collider[] colliders = block.GetComponentsInChildren<Collider>();
            if (renderers.Length == 0)
            {
                Note(false, "Block " + blockName + " has nothing to draw");
                return;
            }
            // The walls and floors near the block set the working volume; anything stray is ignored.
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
                bounds = new Bounds(Origin + new Vector3(25.6f, 0f, 25.6f), new Vector3(60f, 60f, 60f));
            bounds.Expand(2f);
            Note(true, "Block " + blockName + " built with no game loaded: " + renderers.Length + " renderers, " + colliders.Length
                + " colliders, " + (int)bounds.size.x + " x " + (int)bounds.size.y + " x " + (int)bounds.size.z + " m, " + watch.ElapsedMilliseconds + " ms");

            // 2. Walkable surface and a route, from Unity's own navigation.
            watch.Reset();
            watch.Start();
            NavMeshBuildSettings settings = NavMesh.CreateSettings();
            agentType = settings.agentTypeID;
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
            {
                Note(false, "Navigation data could not be built from " + sources.Count + " sources");
            }
            else
            {
                nav = NavMesh.AddNavMeshData(mesh);
                navAdded = true;
                FindRoute(bounds, (starts != null && starts.Length > 0) ? starts[0].transform.position : bounds.center, sources.Count, watch);
            }

            // 3. The camera, drawing into a texture the window shows.
            eye = new GameObject("Eye");
            eye.transform.parent = root.transform;
            cam = eye.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.fieldOfView = 65f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 200f;
            cam.depth = -50f;
            target = new RenderTexture(Screen.width, Screen.height, 24);
            cam.targetTexture = target;
            Light torch = eye.AddComponent<Light>();
            torch.type = LightType.Point;
            torch.range = 14f;
            torch.intensity = 1.1f;
            torch.color = new Color(1f, 0.9f, 0.75f);
            oldAmbient = RenderSettings.ambientLight;
            RenderSettings.ambientLight = new Color(0.22f, 0.22f, 0.24f);
            ambientChanged = true;

            if (route != null)
            {
                eye.transform.position = route[0] + Vector3.up * EyeHeight;
                eye.transform.rotation = Quaternion.LookRotation(Flat(PointAt(1.5f) - route[0]), Vector3.up);
            }
            else
            {
                eye.transform.position = bounds.center;
            }

            // 4. Something to fight, and the hero's weapon.
            try
            {
                MobileEnemy def;
                EnemyBasics.GetEnemy(MobileTypes.SkeletalWarrior, out def);
                enemyIdle = Frames(TextureFile.IndexToFileName(def.MaleTexture), def.HasIdle ? 15 : 0);
                enemyAttack = Frames(TextureFile.IndexToFileName(def.MaleTexture), 5);
                weaponSwing = Frames(WeaponBasics.GetWeaponFilename(WeaponTypes.LongBlade), 1);
                Texture2D[] idle = Frames(WeaponBasics.GetWeaponFilename(WeaponTypes.LongBlade), 0);
                weaponIdle = (idle != null) ? idle[0] : null;
                if (route != null && enemyIdle != null)
                {
                    enemyAt = length * 0.55f;
                    Vector3 feet = PointAt(enemyAt);
                    float tall = 1.9f;
                    enemy = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    enemy.name = "Enemy";
                    UnityEngine.Object.Destroy(enemy.GetComponent<Collider>());
                    enemy.transform.parent = root.transform;
                    enemy.transform.position = feet + Vector3.up * (tall / 2f);
                    enemy.transform.localScale = new Vector3(tall * enemyIdle[0].width / Mathf.Max(1, enemyIdle[0].height), tall, 1f);
                    Material material = MaterialReader.CreateBillboardMaterial();
                    material.mainTexture = enemyIdle[0];
                    enemy.GetComponent<MeshRenderer>().sharedMaterial = material;
                    enemyAlive = true;
                }
                Note(enemyIdle != null && weaponSwing != null, "Sprites: enemy " + Count(enemyIdle) + " idle and " + Count(enemyAttack)
                    + " attack frames, weapon " + Count(weaponSwing) + " swing frames");
            }
            catch (Exception ex)
            {
                Note(false, "Sprites: " + ex.Message);
            }
        }

        static int Count(Texture2D[] frames)
        {
            return (frames != null) ? frames.Length : 0;
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

        // The route is the longest walk the navigation data allows from the starting point.
        void FindRoute(Bounds bounds, Vector3 from, int sourceCount, System.Diagnostics.Stopwatch watch)
        {
            NavMeshQueryFilter filter = new NavMeshQueryFilter();
            filter.agentTypeID = agentType;
            filter.areaMask = NavMesh.AllAreas;

            NavMeshHit hit;
            if (!NavMesh.SamplePosition(from, out hit, 6f, filter))
            {
                // No floor near the start marker: take any walkable spot.
                bool found = false;
                for (float y = bounds.min.y; y <= bounds.max.y && !found; y += 2f)
                    for (float x = bounds.min.x; x <= bounds.max.x && !found; x += 4f)
                        for (float z = bounds.min.z; z <= bounds.max.z && !found; z += 4f)
                            found = NavMesh.SamplePosition(new Vector3(x, y, z), out hit, 2f, filter);
                if (!found)
                {
                    Note(false, "Navigation data built from " + sourceCount + " sources, but it has no walkable floor");
                    return;
                }
            }
            Vector3 start = hit.position;

            int reachable = 0;
            int unreachable = 0;
            Vector3[] best = null;
            float bestLength = 0f;
            NavMeshPath path = new NavMeshPath();
            for (float y = bounds.min.y; y <= bounds.max.y; y += 2f)
            {
                for (float x = bounds.min.x; x <= bounds.max.x; x += 3f)
                {
                    for (float z = bounds.min.z; z <= bounds.max.z; z += 3f)
                    {
                        NavMeshHit spot;
                        if (!NavMesh.SamplePosition(new Vector3(x, y, z), out spot, 1.5f, filter))
                            continue;
                        if (!NavMesh.CalculatePath(start, spot.position, filter, path) || path.status != NavMeshPathStatus.PathComplete)
                        {
                            unreachable++;
                            continue;
                        }
                        reachable++;
                        Vector3[] corners = path.corners;
                        float sum = 0f;
                        for (int i = 1; i < corners.Length; i++)
                            sum += Vector3.Distance(corners[i - 1], corners[i]);
                        if (sum > bestLength)
                        {
                            bestLength = sum;
                            best = corners;
                        }
                    }
                }
            }

            if (best == null || best.Length < 2)
            {
                Note(false, "Navigation data has floor, but no route leads anywhere from the start");
                return;
            }
            route = Centre(best, filter);
            along = new float[route.Length];
            for (int i = 1; i < route.Length; i++)
                along[i] = along[i - 1] + Vector3.Distance(route[i - 1], route[i]);
            length = along[route.Length - 1];
            Note(true, "Route found automatically: " + (int)length + " m with " + best.Length + " turns; " + reachable + " sampled spots reachable, "
                + unreachable + " cut off; " + watch.ElapsedMilliseconds + " ms");
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

        // ------------------------------------------------------------------ moving along the route

        Vector3 PointAt(float distance)
        {
            distance = Mathf.Clamp(distance, 0f, length);
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

        static Vector3 Flat(Vector3 v)
        {
            v.y *= 0.35f;
            return (v.sqrMagnitude < 0.0001f) ? Vector3.forward : v;
        }

        public override void Update()
        {
            base.Update();
            if (canvas != null)
                canvas.Size = new Vector2(Screen.width, Screen.height);
            if (Input.GetKeyDown(KeyCode.F))
                speed = (speed == 1) ? 3 : 1;
            if (route == null || eye == null)
                return;

            float dt = Mathf.Min(0.1f, Time.unscaledDeltaTime) * speed;
            if (fightLeft > 0f)
            {
                fightLeft -= dt;
                if (fightLeft <= 0f && enemy != null)
                {
                    UnityEngine.Object.Destroy(enemy);
                    enemy = null;
                    enemyAlive = false;
                }
            }
            else
            {
                float next = travelled + direction * WalkSpeed * dt;
                if (enemyAlive && direction > 0 && next >= enemyAt - 2.2f)
                {
                    next = enemyAt - 2.2f;
                    fightLeft = FightSeconds;
                }
                if (next >= length)
                {
                    next = length;
                    direction = -1;
                }
                else if (next <= 0f)
                {
                    next = 0f;
                    direction = 1;
                }
                travelled = next;
            }

            Vector3 here = PointAt(travelled);
            Vector3 ahead = PointAt(travelled + direction * 1.8f);
            Vector3 look = (fightLeft > 0f && enemy != null) ? enemy.transform.position - (here + Vector3.up * EyeHeight) : ahead - here;
            eye.transform.position = here + Vector3.up * EyeHeight;
            eye.transform.rotation = Quaternion.Slerp(eye.transform.rotation, Quaternion.LookRotation(Flat(look), Vector3.up), Mathf.Clamp01(dt * 4f));

            if (enemy != null)
            {
                Vector3 toEye = eye.transform.position - enemy.transform.position;
                toEye.y = 0f;
                if (toEye.sqrMagnitude > 0.001f)
                    enemy.transform.rotation = Quaternion.LookRotation(-toEye, Vector3.up);
                Texture2D[] frames = (fightLeft > 0f && enemyAttack != null) ? enemyAttack : enemyIdle;
                enemy.GetComponent<MeshRenderer>().sharedMaterial.mainTexture = frames[(int)(Time.unscaledTime * 6f) % frames.Length];
            }
        }

        class RailsCanvas : BaseScreenComponent
        {
            public IdleRailsWindow Owner;

            static void Text(string s, float x, float y, float scale, Color c)
            {
                float o = Mathf.Max(1f, Mathf.Round(scale * 0.5f));
                DaggerfallUI.DefaultFont.DrawText(s, new Vector2(x, y), new Vector2(scale, scale), c, Color.black, new Vector2(o, o));
            }

            public override void Draw()
            {
                base.Draw();
                IdleRailsWindow o = Owner;
                float W = Screen.width;
                float H = Screen.height;
                DaggerfallUI.DrawTexture(new Rect(0, 0, W, H), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, Color.black);
                if (o.target != null)
                    GUI.DrawTexture(new Rect(0, 0, W, H), o.target, ScaleMode.StretchToFill, false);

                // The weapon, drawn the way the game draws it: a flat image over the view, sized from a 320x200 screen.
                float unit = H / 200f;
                Texture2D weapon = o.weaponIdle;
                if (o.fightLeft > 0f && o.weaponSwing != null)
                    weapon = o.weaponSwing[(int)(Time.unscaledTime * 9f) % o.weaponSwing.Length];
                if (weapon != null)
                {
                    float w = weapon.width * unit;
                    float h = weapon.height * unit;
                    DaggerfallUI.DrawTexture(new Rect(W * 0.5f + 60f * unit - w * 0.5f + ((o.fightLeft > 0f) ? -60f * unit : 0f), H - h, w, h), weapon, ScaleMode.StretchToFill, true);
                }

                float sc = H * 0.028f * 0.74f / DaggerfallUI.DefaultFont.GlyphHeight;
                float line = Mathf.Round(H * 0.032f);
                float y = line;
                Text("Rails spike. F speed, Esc back.", line, y, sc, DaggerfallUI.DaggerfallDefaultTextColor);
                y += line * 1.3f;
                for (int i = 0; i < o.notes.Count; i++)
                {
                    string s = o.notes[i];
                    if (s.Length > 150)
                        s = s.Substring(0, 150);
                    Text(s, line, y, sc * 0.85f, s.StartsWith("OK") ? new Color(0.44f, 0.83f, 0.44f, 1f) : new Color(1f, 0.45f, 0.4f, 1f));
                    y += line;
                }
                if (o.route != null)
                    Text((int)o.travelled + " / " + (int)o.length + " m" + ((o.fightLeft > 0f) ? "   fighting" : ""), line, y, sc * 0.85f, Color.white);
            }
        }
    }
}
