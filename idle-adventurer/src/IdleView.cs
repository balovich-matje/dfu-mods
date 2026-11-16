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
        const float MinRoute = 60f;

        static List<string> blockNames;
        static readonly Dictionary<string, bool> blockWorks = new Dictionary<string, bool>();
        static readonly Dictionary<int, Texture2D[]> idleFrames = new Dictionary<int, Texture2D[]>();
        static readonly Dictionary<int, Texture2D[]> attackFrames = new Dictionary<int, Texture2D[]>();
        static readonly Dictionary<int, Texture2D[]> hurtFrames = new Dictionary<int, Texture2D[]>();
        static readonly Dictionary<int, Texture2D> corpses = new Dictionary<int, Texture2D>();
        static readonly Dictionary<int, float> corpseHeights = new Dictionary<int, float>();
        static readonly Dictionary<int, float> heights = new Dictionary<int, float>();
        const float DeathSeconds = 0.4f;

        public RenderTexture Target;
        public string BlockName;
        public float Length;
        public string Problem;
        public string LastNote = "";

        GameObject root;
        GameObject block;
        GameObject eye;
        GameObject enemy;
        Material enemyMaterial;
        int enemyShown = -1;
        int dyingId = -1;                   // the enemy that is falling or lying dead, -1 when none
        float diedAt;
        Vector3 enemyFoot;
        float enemyLift;
        NavMeshDataInstance nav;
        bool navAdded;
        int agentType = -1;
        Color oldAmbient;
        Vector3[] route;
        float[] along;
        float[] eyes;                       // eye height at each route point: lower under sloping ceilings
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
            // At once, not at the end of the frame: several blocks can be tried in one frame, and a block that is
            // still around would end up in the next one's walkable area.
            if (block != null)
            {
                block.SetActive(false);
                UnityEngine.Object.DestroyImmediate(block);
            }
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
            for (int attempt = 0; attempt < 16 && names.Count > 0; attempt++)
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
                    Debug.Log("[Idle] VIEW floor " + floor + ": " + tried + "route " + (int)Length + " m, " + LastNote + ", " + watch.ElapsedMilliseconds + " ms");
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
            int outside = 0;
            for (float y = bounds.min.y; y <= bounds.max.y; y += 2f)
                for (float x = bounds.min.x; x <= bounds.max.x; x += 3f)
                    for (float z = bounds.min.z; z <= bounds.max.z; z += 3f)
                    {
                        NavMeshHit spot;
                        if (!NavMesh.SamplePosition(new Vector3(x, y, z), out spot, 1.5f, filter))
                            continue;
                        if (Indoors(spot.position) && !OffFloor(spot.position))
                            spots.Add(spot.position);
                        else
                            outside++;
                    }
            LastNote = spots.Count + " spots inside, " + outside + " outside";
            if (spots.Count < 2)
                return false;

            // A few candidate walks, each cut to its longest stretch on real floor. Of those long enough, the one
            // that passes through walls and overhangs least wins, then the longest.
            Vector3[] best = null;
            float bestLength = 0f;
            int bestBlocked = int.MaxValue;
            bool bestLong = false;
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
                if (sum < MinRoute)
                    continue;
                Vector3[] made = OnFloor(Ground(Centre(corners, filter)));
                if (made.Length < 2)
                    continue;
                float kept = PathLength(made);
                bool isLong = kept >= MinRoute * 0.6f;
                int blocked = Blocked(made);
                if ((isLong && !bestLong) || (isLong == bestLong && (isLong ? (blocked < bestBlocked || (blocked == bestBlocked && kept > bestLength)) : kept > bestLength)))
                {
                    bestBlocked = blocked;
                    bestLength = kept;
                    bestLong = isLong;
                    best = made;
                }
            }
            if (best == null)
                return false;
            LastNote += ", " + bestBlocked + " blocked steps";
            Blocks = bestBlocked;

            route = best;
            eyes = Eyes(route);
            along = new float[route.Length];
            for (int i = 1; i < route.Length; i++)
                along[i] = along[i - 1] + Vector3.Distance(route[i - 1], route[i]);
            Length = along[route.Length - 1];
            return true;
        }

        // The longest run of a route that stays on real floor (a stray point or two, on stairs, is let through).
        static Vector3[] OnFloor(Vector3[] r)
        {
            int bestFrom = 0, bestTo = -1, from = 0, misses = 0;
            for (int i = 0; i <= r.Length; i++)
            {
                bool off = i == r.Length || OffFloor(r[i]);
                if (off && i < r.Length && misses < 2)
                {
                    misses++;
                    continue;
                }
                if (!off)
                {
                    if (i - from == misses)
                        from = i;           // a run does not start on stray points
                    misses = 0;
                    continue;
                }
                int to = i - 1 - misses;
                if (to - from > bestTo - bestFrom)
                {
                    bestFrom = from;
                    bestTo = to;
                }
                from = i + 1;
                misses = 0;
            }
            if (bestTo <= bestFrom)
                return new Vector3[0];
            Vector3[] cut = new Vector3[bestTo - bestFrom + 1];
            Array.Copy(r, bestFrom, cut, 0, cut.Length);
            return cut;
        }

        // Outside the rooms: the walkable surface is built from both sides of every collider, so it also covers the
        // backs of ceilings between levels and the tops of walls. A point there has no floor facing up under it
        // (physics rays pass through back faces), or the ground falls away on two sides.
        public static bool OffFloor(Vector3 p)
        {
            RaycastHit under;
            if (!Physics.Raycast(p + Vector3.up * 0.3f, Vector3.down, out under, 0.75f) || under.normal.y < 0.5f)
                return true;
            int drops = 0;
            for (int k = 0; k < 4; k++)
            {
                float a = k * Mathf.PI / 2f;
                Vector3 side = p + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.7f + Vector3.up * 0.3f;
                if (!Physics.Linecast(p + Vector3.up * 0.3f, side) && !Physics.Raycast(side, Vector3.down, 1.2f))
                    drops++;            // open to that side, with nothing to stand on: a drop, not a wall
            }
            return drops >= 2;
        }

        // How many steps of a route pass through something solid at knee or eye height: a wall corner it cuts, an
        // overhang on stairs, a closed door.
        public int Blocks;

        static int Blocked(Vector3[] r)
        {
            float[] e = Eyes(r);
            int count = 0;
            for (int i = 1; i < r.Length; i++)
                if (Physics.Linecast(r[i - 1] + Vector3.up * 0.6f, r[i] + Vector3.up * 0.6f)
                    || Physics.Linecast(r[i - 1] + Vector3.up * e[i - 1], r[i] + Vector3.up * e[i]))
                    count++;
            return count;
        }

        // The eye's height at each point: a little under any ceiling lower than usual (the underside of a stair,
        // an arch), taken as the lowest over the nearby points so the hero ducks before getting there.
        static float[] Eyes(Vector3[] r)
        {
            float[] room = new float[r.Length];
            for (int i = 0; i < r.Length; i++)
            {
                room[i] = EyeHeight;
                RaycastHit up;
                if (Physics.Raycast(r[i] + Vector3.up * 0.3f, Vector3.up, out up, EyeHeight + 0.2f))
                    room[i] = Mathf.Clamp(up.distance + 0.3f - 0.25f, 0.9f, EyeHeight);
            }
            float[] e = new float[r.Length];
            for (int i = 0; i < r.Length; i++)
            {
                e[i] = room[i];
                for (int k = Mathf.Max(0, i - 3); k <= Mathf.Min(r.Length - 1, i + 3); k++)
                    e[i] = Mathf.Min(e[i], room[k] + 0.06f * Mathf.Abs(k - i));
            }
            return e;
        }

        float EyeAt(float distance)
        {
            if (eyes == null || eyes.Length != route.Length)
                return EyeHeight;
            distance = Mathf.Clamp(distance, 0f, Length);
            for (int i = 1; i < route.Length; i++)
                if (distance <= along[i])
                {
                    float span = Mathf.Max(0.0001f, along[i] - along[i - 1]);
                    return Mathf.Lerp(eyes[i - 1], eyes[i], (distance - along[i - 1]) / span);
                }
            return eyes[eyes.Length - 1];
        }

        // Dungeon blocks only have inward-facing walls, so from outside you see straight through them. A starting
        // spot for the route search must have a ceiling above it and a floor right under it. (Only a rough check:
        // it is wrong for some points along a route, so it is not used to judge whole routes.)
        static bool Indoors(Vector3 p)
        {
            return Physics.Raycast(p + Vector3.up * 0.3f, Vector3.up, 60f)
                && Physics.Raycast(p + Vector3.up * 0.3f, Vector3.down, 1.5f);
        }

        // Debug (key B in the view): tries every block and writes what it found to the log.
        public void TestBlocks()
        {
            List<string> names = Names();
            int good = 0;
            for (int i = 0; i < names.Count; i++)
            {
                bool ok = false;
                LastNote = "";
                try
                {
                    ok = Load(names[i], 7);
                }
                catch (Exception ex)
                {
                    LastNote = ex.Message;
                }
                if (ok)
                    good++;
                Debug.Log("[Idle] BLOCK " + names[i] + (ok ? " ok " + (int)Length + " m, " : " no, ") + LastNote);
            }
            Debug.Log("[Idle] BLOCK " + good + " of " + names.Count + " usable");
            Unload();
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

        // The navigation surface runs over stairs as a straight ramp, so a route on it floats above some steps and
        // sinks into others. This cuts the route into short pieces and drops each one onto the real floor below it.
        static Vector3[] Ground(Vector3[] points)
        {
            const float step = 0.25f;
            List<Vector3> fine = new List<Vector3>();
            for (int i = 0; i < points.Length; i++)
            {
                if (i > 0)
                {
                    float span = Vector3.Distance(points[i - 1], points[i]);
                    int pieces = Mathf.Max(1, Mathf.CeilToInt(span / step));
                    for (int n = 1; n < pieces; n++)
                        fine.Add(Vector3.Lerp(points[i - 1], points[i], n / (float)pieces));
                }
                fine.Add(points[i]);
            }
            // Only within a step's height of the navigation surface: a longer ray can reach through a stairwell or
            // over a ledge to the floor below, and the eye then walks under the floor it should be on.
            for (int i = 0; i < fine.Count; i++)
            {
                RaycastHit hit;
                Vector3 p = fine[i];
                if (Physics.Raycast(p + Vector3.up * 0.45f, Vector3.down, out hit, 0.9f) && hit.normal.y > 0.5f)
                {
                    p.y = hit.point.y;
                    fine[i] = p;
                }
            }

            // The route keeps clear of walls at the feet only; cave walls lean in and arches curve over. Ease each
            // point away from anything within reach of the eye, if the step aside is itself clear.
            const float reach = 0.4f;
            for (int i = 0; i < fine.Count; i++)
            {
                Vector3 eyeAt = fine[i] + Vector3.up * EyeHeight;
                Vector3 push = Vector3.zero;
                for (int k = 0; k < 8; k++)
                {
                    float a = k * Mathf.PI / 4f;
                    Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    RaycastHit wall;
                    if (Physics.Raycast(eyeAt, dir, out wall, reach))
                        push -= dir * (reach - wall.distance);
                }
                if (push.sqrMagnitude < 0.0001f)
                    continue;
                push = Vector3.ClampMagnitude(push, reach);
                Vector3 moved = fine[i] + push;
                if (!Physics.Linecast(eyeAt, moved + Vector3.up * EyeHeight) && !Physics.Linecast(fine[i] + Vector3.up * 0.6f, moved + Vector3.up * 0.6f))
                    fine[i] = moved;
            }
            return fine.ToArray();
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
            Vector3 here = PointAt(distance) + Vector3.up * EyeAt(distance);
            Vector3 look = PointAt(distance + direction * 1.8f) + Vector3.up * EyeAt(distance + direction * 1.8f) - here;
            Quaternion want = Quaternion.LookRotation(Level(look), Vector3.up);
            if (faceEnemy && enemy != null)
            {
                // Look at the enemy itself, so one standing lower or higher on stairs stays in view.
                look = enemyFoot + Vector3.up * Mathf.Min(1.2f, heights.ContainsKey(enemyShown) ? heights[enemyShown] * 0.6f : 0.6f) + Vector3.up * enemyLift - here;
                look.y *= 0.5f;
                if (look.sqrMagnitude > 0.01f)
                    want = Quaternion.LookRotation(look, Vector3.up);
            }
            if (snap || (here - eye.transform.position).magnitude > 6f)
                eye.transform.rotation = want;
            else
            {
                eye.transform.rotation = Quaternion.Slerp(eye.transform.rotation, want, Mathf.Clamp01(dt * 4f));
                // Step up and down stairs a little softly, as the game's own player does.
                here.y = Mathf.Lerp(eye.transform.position.y, here.y, Mathf.Clamp01(dt * 14f));
            }
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
        // Where on the route an enemy should stand so the hero can see it: ahead if possible, closer or behind when
        // a corner or a wall is in the way.
        public float EnemySpot(float distance, int direction, float gap)
        {
            if (!Ready)
                return distance;
            Vector3 from = PointAt(distance) + Vector3.up * EyeHeight;
            for (int tries = 0; tries < 6; tries++)
            {
                float reach = gap * (1f - 0.25f * (tries / 2));
                float at = distance + ((tries % 2 == 0) ? direction : -direction) * reach;
                if (at < 0f || at > Length)
                    continue;
                if (!Physics.Linecast(from, PointAt(at) + Vector3.up * 0.5f))
                    return at;
            }
            float ahead = distance + direction * gap;
            return (ahead < 0f || ahead > Length) ? distance - direction * gap : ahead;
        }

        // True while a killed enemy is still dropping (and a moment after), so the hero watches it fall.
        public bool Falling
        {
            get { return enemy != null && enemy.activeSelf && dyingId >= 0 && Time.unscaledTime - diedAt < DeathSeconds + 0.35f; }
        }

        public bool EnemyPlaced(int id)
        {
            return enemy != null && enemy.activeSelf && enemyShown == id;
        }

        // mode: 0 standing, 1 attacking, 2 just struck.
        public void ShowEnemy(int id, float distance, int mode, Color tint, float grow)
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
                    hurtFrames[id] = null;
                    try
                    {
                        hurtFrames[id] = Frames(file, 10);
                    }
                    catch (Exception)
                    {
                    }
                }
                catch (Exception ex)
                {
                    Debug.Log("[Idle] VIEW sprite for enemy " + id + ": " + ex.Message);
                    idle = null;
                    attackFrames[id] = null;
                    hurtFrames[id] = null;
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
                Debug.Log("[Idle] VIEW enemy shader " + enemyMaterial.shader.name + ", tint " + (enemyMaterial.HasProperty("_Color") ? "yes" : "no"));
                enemy.GetComponent<MeshRenderer>().sharedMaterial = enemyMaterial;
            }
            Texture2D[] frames = idle;
            Texture2D[] other = (mode == 1) ? attackFrames[id] : (mode == 2) ? hurtFrames[id] : null;
            if (other != null && other.Length > 0)
                frames = other;
            Texture2D frame = frames[(int)(Time.unscaledTime * 7f) % frames.Length];
            enemyMaterial.mainTexture = frame;
            if (enemyMaterial.HasProperty("_Color"))
                enemyMaterial.color = tint;
            dyingId = -1;

            float tall = heights[id] * grow;
            float wide = tall * frame.width / Mathf.Max(1, frame.height);
            if (enemyShown != id || !enemy.activeSelf)
            {
                // Flying things hover at chest height instead of standing on the floor.
                enemyLift = (IdleSim.Enemy(id).def.Behaviour == MobileBehaviour.Flying) ? 0.8f : 0f;
                enemyFoot = PointAt(distance);
                enemyShown = id;
                enemy.SetActive(true);
            }
            enemy.transform.position = enemyFoot + Vector3.up * (tall / 2f + enemyLift);
            enemy.transform.localScale = new Vector3(wide, tall, 1f);
            FaceEye();
        }

        void FaceEye()
        {
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
            dyingId = -1;
        }

        // The enemy on show has been killed: it drops, then its corpse stays where it fell.
        public void KillEnemy()
        {
            if (enemy == null || !enemy.activeSelf || enemyShown < 0)
                return;
            dyingId = enemyShown;
            enemyShown = -1;                // the next enemy gets its own place, even if it is the same kind
            diedAt = Time.unscaledTime;
            if (!corpses.ContainsKey(dyingId))
            {
                Texture2D texture = null;
                float tall = 0.4f;
                try
                {
                    int packed = IdleSim.Enemy(dyingId).def.CorpseTexture;
                    string file = TextureFile.IndexToFileName(packed >> 16);
                    Texture2D[] frames = Frames(file, packed & 0xffff);
                    if (frames != null && frames.Length > 0)
                    {
                        texture = frames[0];
                        tall = SpriteHeight(file, packed & 0xffff, texture.height);
                    }
                }
                catch (Exception ex)
                {
                    Debug.Log("[Idle] VIEW corpse for enemy " + dyingId + ": " + ex.Message);
                }
                corpses[dyingId] = texture;
                Debug.Log("[Idle] VIEW corpse for enemy " + dyingId + ": " + ((texture != null) ? texture.width + "x" + texture.height + ", " + tall.ToString("0.00") + " m" : "none"));
                corpseHeights[dyingId] = tall;
            }
        }

        // Called while no fight is on: plays out a death, keeps a corpse in place, hides anything else.
        public void RestEnemy()
        {
            if (enemy == null || !enemy.activeSelf)
                return;
            if (dyingId < 0)
            {
                HideEnemy();
                return;
            }
            float t = (Time.unscaledTime - diedAt) / DeathSeconds;
            Texture2D corpse = corpses[dyingId];
            if (t < 1f)
            {
                // Falling: the struck frame, squashed towards the floor.
                Texture2D[] hurt = hurtFrames[dyingId];
                Texture2D frame = (hurt != null && hurt.Length > 0) ? hurt[0] : idleFrames[dyingId][0];
                enemyMaterial.mainTexture = frame;
                float tall = heights[dyingId] * (1f - 0.65f * t);
                float wide = heights[dyingId] * frame.width / Mathf.Max(1, frame.height);
                enemy.transform.localScale = new Vector3(wide, tall, 1f);
                enemy.transform.position = enemyFoot + Vector3.up * (tall / 2f + enemyLift * (1f - t));
            }
            else if (corpse != null)
            {
                float tall = corpseHeights[dyingId];
                enemyMaterial.mainTexture = corpse;
                enemy.transform.localScale = new Vector3(tall * corpse.width / Mathf.Max(1, corpse.height), tall, 1f);
                enemy.transform.position = enemyFoot + Vector3.up * (tall / 2f);
            }
            else
            {
                HideEnemy();
                return;
            }
            FaceEye();
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
        const float FadeOut = 0.45f;
        const float FadeIn = 0.9f;

        class Floater
        {
            public string text;
            public float born;
            public bool onHero;
            public bool heal;
            public bool spell;
        }

        ViewCanvas canvas;
        RailsScene scene;
        int loadedSeed;
        int loadedFloor = -1;
        int loadIn;
        long seen;
        float swingUntil;
        float clinkAt;                      // mining: when the swing reaches the rock
        float enemyAttackUntil;
        float hurtUntil;
        float enemyHurtUntil;
        float leaveAt;                      // when the fade to black before a new floor ends, 0 when not fading out
        float curtainFrom = -10f;           // when the last fade from black started
        bool showing;                       // the dungeon was on screen last frame
        int lastEnemy = -1;
        float soundAt;
        float stepAt;
        int stepCount;
        readonly List<Floater> floaters = new List<Floater>();
        WeaponTypes weaponType = WeaponTypes.None;
        Texture2D weaponIdle;
        Texture2D[] weaponSwing;
        float castFrom = -10f;              // when the hero last started casting
        int castElement = -1;
        static readonly Dictionary<int, Texture2D[]> castFrames = new Dictionary<int, Texture2D[]>();
        static readonly string[] castFiles = { "FIRE00C6.CIF", "FRST00C6.CIF", "POIS00C6.CIF", "SHOK00C6.CIF", "MJIC00C6.CIF" };
        static readonly int[] castOrder = { 0, 1, 2, 3, 4, 5, 0 };     // the game's order: starts and ends on frame 0
        const float CastFrameSeconds = 0.06f;                          // the game uses 0.04

        // The game's casting hands for an element (FPSSpellCasting): one frame per record of a CIF file.
        static Texture2D[] CastFrames(int element)
        {
            element = Mathf.Clamp(element, 0, castFiles.Length - 1);
            Texture2D[] frames;
            if (castFrames.TryGetValue(element, out frames))
                return frames;
            frames = null;
            try
            {
                string path = System.IO.Path.Combine(DaggerfallUnity.Instance.Arena2Path, castFiles[element]);
                CifRciFile cif = new CifRciFile();
                if (cif.Load(path, FileUsage.UseMemory, true))
                {
                    cif.Palette.Load(System.IO.Path.Combine(DaggerfallUnity.Instance.Arena2Path, cif.PaletteName));
                    frames = new Texture2D[cif.RecordCount];
                    for (int record = 0; record < cif.RecordCount; record++)
                    {
                        DFSize size;
                        Color32[] colors = cif.GetColor32(record, 0, 0, 0, out size);
                        Texture2D texture = new Texture2D(size.Width, size.Height, TextureFormat.ARGB32, false);
                        texture.SetPixels32(colors);
                        texture.Apply(false);
                        texture.filterMode = FilterMode.Point;
                        frames[record] = texture;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.Log("[Idle] Could not load casting hands " + castFiles[element] + ": " + ex.Message);
                frames = null;
            }
            castFrames[element] = frames;
            return frames;
        }

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
            if (w.s.phase == IdleData.PhaseExplore && w.s.miningMetal > 0)
                want = WeaponTypes.Warhammer;       // the pickaxe: the game's warhammer, which strikes straight down
            else if (item != null && item.currentCondition > 0)
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
            if (Input.GetKeyDown(KeyCode.X))
            {
                // Test key: the casting hands of the next element, without a cast in the simulation.
                castFrom = Time.unscaledTime;
                castElement = (castElement + 1) % 5;
            }
            if (Input.GetKeyDown(KeyCode.B) && scene != null)
            {
                scene.TestBlocks();
                loadedFloor = -1;
            }

            // A new floor: the old one fades to black, the new one is built on a dark frame, then it fades in.
            float now = Time.unscaledTime;
            bool dungeon = InDungeon(s) && scene != null;
            bool stale = dungeon && (loadedFloor != s.floor || loadedSeed != s.dungeonSeed);
            if (stale)
            {
                if (leaveAt == 0f)
                    leaveAt = now + (showing ? FadeOut : 0.001f);
                if (now >= leaveAt)
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
                    leaveAt = 0f;
                    stale = false;
                    curtainFrom = Time.unscaledTime;
                    now = Time.unscaledTime;
                }
            }
            else
            {
                leaveAt = 0f;
            }
            bool onShow = dungeon && !stale && scene.Ready;
            if (showing && !dungeon)
                curtainFrom = now;
            showing = onShow;

            IdleClock.Tick();
            if (s.phase == IdleData.PhaseFight && s.enemyId >= 0)
                lastEnemy = s.enemyId;

            // What happened since the last frame.
            long fresh = w.eventTotal - seen;
            seen = w.eventTotal;
            int first = (int)Math.Max(0, w.events.Count - fresh);
            for (int i = first; i < w.events.Count; i++)
            {
                IdleEvent e = w.events[i];
                if (e.type == IdleData.EvHeroHit || e.type == IdleData.EvHeroMiss)
                    swingUntil = now + 0.45f;
                if (e.type == IdleData.EvCast || e.type == IdleData.EvCastSelf)
                {
                    castFrom = now;
                    castElement = (e.element >= 0) ? e.element : IdleData.ElementMagic;
                    swingUntil = 0f;
                }
                if (e.type == IdleData.EvEnemyHit || e.type == IdleData.EvEnemyMiss || e.type == IdleData.EvEnemyCast)
                    enemyAttackUntil = now + 0.5f;
                if (e.type == IdleData.EvEnemyHit || (e.type == IdleData.EvEnemyCast && e.value > 0))
                    hurtUntil = now + 0.25f;
                if ((e.type == IdleData.EvPotion || (e.type == IdleData.EvCastSelf && e.value > 0)) && floaters.Count < 12)
                {
                    Floater f = new Floater();
                    f.text = "+" + e.value;
                    f.born = now;
                    f.onHero = true;
                    f.heal = true;
                    floaters.Add(f);
                }
                if (e.type == IdleData.EvHeroHit || (e.type == IdleData.EvCast && e.value > 0))
                    enemyHurtUntil = now + 0.22f;
                if (e.type == IdleData.EvKill && scene != null)
                    scene.KillEnemy();
                if (onShow)
                    Sound(e, now);
                bool spellHit = (e.type == IdleData.EvCast || e.type == IdleData.EvEnemyCast) && e.value > 0;
                if ((e.type == IdleData.EvHeroHit || e.type == IdleData.EvEnemyHit || spellHit) && floaters.Count < 12)
                {
                    Floater f = new Floater();
                    f.text = (e.value > 0) ? "-" + e.value : "0";
                    f.born = now;
                    f.onHero = e.type == IdleData.EvEnemyHit || e.type == IdleData.EvEnemyCast;
                    f.spell = spellHit;
                    floaters.Add(f);
                }
            }
            for (int i = floaters.Count - 1; i >= 0; i--)
                if (now - floaters[i].born > 1.1f)
                    floaters.RemoveAt(i);

            if (!onShow)
                return;
            PickWeapon(w);

            // Footsteps, at normal speed only.
            if (s.phase == IdleData.PhaseExplore && s.miningMetal > 0)
            {
                // Mining: the hero stands and brings the pick down on the vein; the clink comes when it lands.
                if (now >= stepAt)
                {
                    stepAt = now + 0.9f / Mathf.Min(4, IdleClock.Speed);
                    stepCount++;
                    swingUntil = now + 0.45f;
                    clinkAt = now + 0.45f * 0.55f;
                    DaggerfallUI.Instance.PlayOneShot(SoundClips.SwingLowPitch);
                }
                if (clinkAt > 0f && now >= clinkAt)
                {
                    clinkAt = 0f;
                    DaggerfallUI.Instance.PlayOneShot((SoundClips)((int)SoundClips.Parry1 + stepCount % 9));
                }
            }
            else if (s.phase == IdleData.PhaseExplore && IdleClock.Speed == 1 && now >= stepAt)
            {
                stepAt = now + 0.48f;
                stepCount++;
                DaggerfallUI.Instance.PlayOneShot((stepCount % 2 == 0) ? SoundClips.PlayerFootstepStone1 : SoundClips.PlayerFootstepStone2);
            }

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
                float at = scene.EnemyPlaced(s.enemyId) ? 0f : scene.EnemySpot(distance, direction, FightGap);
                scene.ShowEnemy(s.enemyId, at, (now < enemyAttackUntil) ? 1 : (now < enemyHurtUntil) ? 2 : 0,
                    IdleUi.TraitColor(s.enemyTraits), ((s.enemyTraits & IdleData.TraitBoss) != 0) ? 1.25f : 1f);
            }
            else
            {
                scene.RestEnemy();
            }
            scene.PlaceEye(distance, direction, fighting || scene.Falling, Mathf.Min(0.1f, Time.unscaledDeltaTime) * Mathf.Min(4, IdleClock.Speed));
        }

        // The game's own sounds for what just happened. Faster speeds play fewer of them.
        void Sound(IdleEvent e, float now)
        {
            int clip = -1;
            bool always = false;
            MobileEnemy def = new MobileEnemy();
            bool known = lastEnemy >= 0;
            if (known)
                def = IdleSim.Enemy(lastEnemy).def;
            switch (e.type)
            {
                case IdleData.EvFight:
                    if (IdleStore.W.s.enemyId >= 0)
                        clip = IdleSim.Enemy(IdleStore.W.s.enemyId).def.BarkSound;
                    break;
                case IdleData.EvHeroHit:
                    clip = (int)SoundClips.Hit1 + UnityEngine.Random.Range(0, 5);
                    break;
                case IdleData.EvHeroMiss:
                    clip = (known && def.ParrySounds) ? (int)SoundClips.Parry1 + UnityEngine.Random.Range(0, 9) : (int)SoundClips.SwingMediumPitch;
                    break;
                case IdleData.EvEnemyHit:
                    clip = (int)SoundClips.Hit1 + UnityEngine.Random.Range(0, 5);
                    break;
                case IdleData.EvEnemyMiss:
                    if (known)
                        clip = def.AttackSound;
                    break;
                case IdleData.EvKill:
                case IdleData.EvDied:
                    clip = (int)SoundClips.BodyFall;
                    always = true;
                    break;
                case IdleData.EvLoot:
                    if (e.text.StartsWith("Found"))
                        clip = (int)SoundClips.GoldPieces;
                    break;
                case IdleData.EvPotion:
                    clip = (int)SoundClips.MakePotion;
                    always = true;
                    break;
                case IdleData.EvCast:
                case IdleData.EvEnemyCast:
                    // The game's impact sound for the spell's element.
                    switch (e.element)
                    {
                        case IdleData.ElementFire: clip = (int)SoundClips.SpellImpactFire; break;
                        case IdleData.ElementCold: clip = (int)SoundClips.SpellImpactCold; break;
                        case IdleData.ElementPoison: clip = (int)SoundClips.SpellImpactPoison; break;
                        case IdleData.ElementShock: clip = (int)SoundClips.SpellImpactShock; break;
                        default: clip = (int)SoundClips.SpellImpactMagic; break;
                    }
                    break;
                case IdleData.EvCastSelf:
                    clip = (int)SoundClips.CastSpell1 + UnityEngine.Random.Range(0, 5);
                    always = true;
                    break;
                case IdleData.EvLevel:
                    clip = (int)SoundClips.LevelUp;
                    always = true;
                    break;
                case IdleData.EvFloor:
                    clip = (int)SoundClips.DungeonDoorOpen;
                    always = true;
                    break;
            }
            if (clip <= 0)
                return;
            if (!always && (IdleClock.Speed > 4 || now < soundAt))
                return;
            soundAt = now + 0.07f * IdleClock.Speed;
            DaggerfallUI.Instance.PlayOneShot((SoundClips)clip);
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
                bool fadingOut = o.leaveAt > 0f && now < o.leaveAt && o.loadIn == 0;
                bool dungeon = InDungeon(s) && o.scene != null && o.scene.Ready
                    && ((o.loadedFloor == s.floor && o.loadedSeed == s.dungeonSeed) || fadingOut);
                if (dungeon)
                {
                    GUI.DrawTexture(new Rect(0, 0, W, H), o.scene.Target, ScaleMode.StretchToFill, false);

                    // The weapon, drawn the way the game draws it: a flat image over the view, sized from a 320x200 screen.
                    bool swinging = now < o.swingUntil && o.weaponSwing != null && o.weaponSwing.Length > 0;
                    Texture2D weapon = o.weaponIdle;

                    // Casting: the game's two hands from the bottom corners, the weapon put away meanwhile.
                    int castStep = (int)((now - o.castFrom) / CastFrameSeconds);
                    Texture2D[] hands = (castStep >= 0 && castStep < castOrder.Length) ? CastFrames(o.castElement) : null;
                    if (hands != null && hands.Length > 0)
                    {
                        int frame = Mathf.Clamp(castOrder[castStep], 0, hands.Length - 1);
                        Texture2D hand = hands[frame];
                        if (hand != null)
                        {
                            // Laid out on a 320-wide strip in the middle, as the game lays it out on its 320x200 screen.
                            float stripX = W * 0.5f - 160f * unit;
                            bool small = frame == 0 || frame == 5 || (o.castElement == IdleData.ElementFire && frame == 4);
                            float inset = small ? 320f * unit * 0.134f : 0f;
                            float handW = hand.width * unit;
                            float handH = hand.height * unit;
                            GUI.DrawTextureWithTexCoords(new Rect(stripX + inset, H - handH, handW, handH), hand, new Rect(0, 0, 1, 1), true);
                            GUI.DrawTextureWithTexCoords(new Rect(stripX + 320f * unit - inset - handW, H - handH, handW, handH), hand, new Rect(1, 0, -1, 1), true);
                        }
                        weapon = null;
                        swinging = false;
                    }
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
                        Color c = f.heal ? IdleUi.Good : f.spell ? IdleUi.Blue : f.onHero ? IdleUi.Bad : IdleUi.White;
                        c.a = Mathf.Clamp01(1.4f - age * 1.3f);
                        IdleUi.Centred(f.text, fx, fy, sc * 1.5f, c);
                    }

                    if (s.phase == IdleData.PhaseFight && s.enemyId >= 0)
                    {
                        string name = IdleSim.Foe(s, IdleSim.Enemy(s.enemyId));
                        if ((s.enemyTraits & IdleData.TraitBoss) != 0)
                            name = "Boss: " + name;
                        float bw = Mathf.Round(W * 0.26f);
                        IdleUi.Centred(name, W * 0.5f, pad, sc * 1.2f, IdleSim.IsChampion(s) ? IdleUi.TraitColor(s.enemyTraits) : IdleUi.Gold);
                        IdleUi.Bar(new Rect((W - bw) / 2f, pad + line * 1.25f, bw, Mathf.Round(line * 0.35f)), (float)s.enemyHealth / Mathf.Max(1, s.enemyMaxHealth), IdleUi.Line);
                    }
                    IdleUi.Text("Floor " + s.floor + " of " + s.targetFloor + "    " + ((s.miningMetal > 0) ? "Mining " + IdleData.OreNames[s.miningMetal - 1].ToLower() : s.roomsLeft + " fight(s) left"), pad, pad, sc, IdleUi.Gold);
                    if (s.miningMetal > 0 && s.phase == IdleData.PhaseExplore)
                    {
                        // How far through the vein: a bar under the crosshair, like the travel bar.
                        string what = "Mining " + IdleData.OreNames[s.miningMetal - 1].ToLower() + "...";
                        float bw = Mathf.Round(W * 0.26f);
                        IdleUi.Centred(what, W * 0.5f, H * 0.62f, sc * 1.1f, IdleUi.Gold);
                        IdleUi.Bar(new Rect((W - bw) / 2f, H * 0.62f + line * 1.3f, bw, Mathf.Round(line * 0.35f)),
                            1f - (float)s.phaseLeft / Mathf.Max(1, s.phaseTotal), IdleUi.Line);
                    }
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

                // The fade: to black before a new floor, from black after one and when leaving the dungeon.
                float dark = fadingOut ? 1f - (o.leaveAt - now) / FadeOut : 1f - (now - o.curtainFrom) / FadeIn;
                if (dark > 0f)
                    IdleUi.Box(new Rect(0, 0, W, H), new Color(0f, 0f, 0f, Mathf.Clamp01(dark)));

                // Hero health, bottom left.
                float hw = Mathf.Round(W * 0.22f);
                float tall = Mathf.Round(line * 1.25f);
                float hy = H - pad - tall - line * 1.9f;
                IdleUi.Text(s.heroName + "  " + Mathf.Max(0, s.health) + "/" + s.maxHealth + "    Potions " + s.potions, pad, hy, sc, IdleUi.White);
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
