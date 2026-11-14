// Idle Adventurer - the simulation. No Unity time, no UI: IdleSim.Advance moves the world forward by a number of
// ticks and records what happened. The same code runs a watched fight, a fast-forward and the time spent away.

using System;
using System.Collections.Generic;
using UnityEngine;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Items;
using DaggerfallWorkshop.Game.Serialization;
using DaggerfallWorkshop.Game.Utility;
using DaggerfallWorkshop.Utility;

namespace IdleAdventurer
{
    // Tuning numbers and the int codes used in the save (the mod compiler has no enums of our own).
    public static class IdleData
    {
        public const int SaveVersion = 2;
        public const int TicksPerSecond = 10;

        public const int PhaseTown = 0;
        public const int PhaseOut = 1;
        public const int PhaseExplore = 2;
        public const int PhaseFight = 3;
        public const int PhaseBack = 4;

        // Gear slots 0-6 are the game's body parts (head, right arm, left arm, chest, hands, legs, feet).
        public const int SlotShield = 7;
        public const int SlotWeapon = 8;
        public const int SlotCount = 9;

        public const int EvInfo = 0;
        public const int EvFight = 1;
        public const int EvHeroHit = 2;
        public const int EvHeroMiss = 3;
        public const int EvEnemyHit = 4;
        public const int EvEnemyMiss = 5;
        public const int EvKill = 6;
        public const int EvLoot = 7;
        public const int EvFloor = 8;
        public const int EvRetreat = 9;
        public const int EvDied = 10;
        public const int EvHome = 11;
        public const int EvLevel = 12;
        public const int EvSkill = 13;

        public const int StatStrength = 0;
        public const int StatAgility = 3;
        public const int StatEndurance = 4;
        public const int StatSpeed = 6;
        public const int StatLuck = 7;

        public const int TravelSeconds = 20;
        public const int ExploreMinSeconds = 8;
        public const int ExploreMaxSeconds = 15;
        public const int HitPointsPerLevel = 18;
        public const int DefaultDungeonType = 1;        // DFRegion.DungeonTypes.OrcStronghold
        public const int DeathGoldLossPercent = 10;
        public const int DeathWearPercent = 10;
        public const int OfflineCapSeconds = 12 * 3600;
        public const int SaleDivisor = 4;               // loot sells for a quarter of its listed value

        public static int RoomsOnFloor(int floor)
        {
            return Mathf.Min(6, 3 + floor / 3);
        }

        // Seconds between attacks. Speed 50 gives 2.0 s.
        public static int AttackInterval(int speed)
        {
            return Mathf.Clamp(1800 / (40 + Mathf.Max(0, speed)), 8, 40);
        }

        public static int SkillMultiplier(int skill)
        {
            if (skill == (int)DFCareer.Skills.CriticalStrike) return 8;
            if (skill == (int)DFCareer.Skills.Dodging) return 4;
            if (skill == (int)DFCareer.Skills.Medical) return 2;
            return 2;
        }
    }

    // The mode's own save file. Flat on purpose: JsonUtility drops nested classes from runtime-compiled mods.
    [Serializable]
    public class IdleSave
    {
        public int version = IdleData.SaveVersion;
        public string heroName = "Adventurer";
        public int opened;
        public long lastSeenUtcTicks;

        // Items are stored the way DFU stores them, as serialised ItemData_v1 arrays.
        public string gearBlob;
        public string haulBlob;

        public int rng = 1;
        public long clock;                  // ticks since the hero was created

        public int gender;
        public int race = 1;
        public int level = 1;
        public int skillRises;
        public int unspentPoints;
        public int spendTurn;
        public int[] stats;
        public int[] skills;
        public int[] skillUses;
        public int health;
        public int maxHealth;
        public int healCarry;
        public int gold;

        public int phase;
        public int phaseLeft;
        public int dungeonType = IdleData.DefaultDungeonType;
        public int floor;
        public int roomsLeft;
        public int carried;                 // 1 while the hero is being carried home after dying

        public int enemyId = -1;
        public int enemyHealth;
        public int enemyMaxHealth;
        public int enemyOver;               // how far past the end of the encounter table this enemy was rolled
        public int heroTimer;
        public int enemyTimer;

        // Orders.
        public int targetFloor = 2;
        public int retreatPercent = 35;
        public int fleePercent = 25;        // below this the hero tries to run from a fight in progress
        public int autoRepeat;
        public int autoDeeper = 1;
        public int autoEquip = 1;
        public int autoSpend = 1;

        // This run and lifetime tallies.
        public int runKills;
        public int runGold;
        public int runItems;
        public int expeditions;
        public int cleared;
        public int retreats;
        public int fled;
        public int deaths;
        public int kills;
        public int deepestFloor;
        public int leftBehind;
        public long goldEarned;
    }

    public class IdleEvent
    {
        public int type;
        public string text;
    }

    public class EnemyInfo
    {
        public int id;
        public MobileEnemy def;
        public DFCareer career;
        public bool isClass;
        public string name;
    }

    // A save plus the live objects that go with it.
    public class IdleWorld
    {
        public IdleSave s;
        public DaggerfallUnityItem[] gear = new DaggerfallUnityItem[IdleData.SlotCount];
        public List<DaggerfallUnityItem> haul = new List<DaggerfallUnityItem>();
        public List<IdleEvent> events = new List<IdleEvent>();
        public bool record;
        public int stopAfter;               // test runs: stop in town once this many expeditions are done

        public void Emit(int type, string text)
        {
            IdleEvent e = new IdleEvent();
            e.type = type;
            e.text = text;
            events.Add(e);
            if (events.Count > 400)
                events.RemoveRange(0, 200);
        }
    }

    public static class IdleSim
    {
        static readonly Dictionary<int, EnemyInfo> enemies = new Dictionary<int, EnemyInfo>();
        static readonly int[] bodyParts = { 0, 0, 1, 1, 1, 2, 2, 2, 3, 3, 3, 3, 4, 4, 4, 4, 5, 5, 5, 6 };
        static readonly int[] spendOrder = { IdleData.StatStrength, IdleData.StatAgility, IdleData.StatEndurance, IdleData.StatSpeed };

        // ------------------------------------------------------------------ random numbers

        // Xorshift, with its state in the save so a run can be replayed exactly.
        static int Next(IdleSave s)
        {
            uint x = (uint)s.rng;
            if (x == 0)
                x = 2463534242u;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            s.rng = (int)x;
            return (int)(x & 0x7fffffff);
        }

        static int Range(IdleSave s, int min, int maxInclusive)
        {
            if (maxInclusive <= min)
                return min;
            return min + Next(s) % (maxInclusive - min + 1);
        }

        static bool Roll(IdleSave s, int percent)
        {
            return Range(s, 0, 99) < percent;
        }

        // ------------------------------------------------------------------ setting up

        public static IdleWorld NewHero(int seed, string name)
        {
            IdleWorld w = new IdleWorld();
            IdleSave s = new IdleSave();
            w.s = s;
            s.heroName = name;
            s.rng = (seed == 0) ? 1 : seed;
            s.stats = new int[] { 60, 45, 45, 55, 60, 40, 50, 50 };
            s.skills = new int[(int)DFCareer.Skills.Count];
            s.skillUses = new int[(int)DFCareer.Skills.Count];
            for (int i = 0; i < s.skills.Length; i++)
                s.skills[i] = 5;
            s.skills[(int)DFCareer.Skills.LongBlade] = 30;
            s.skills[(int)DFCareer.Skills.Dodging] = 25;
            s.skills[(int)DFCareer.Skills.CriticalStrike] = 20;
            s.skills[(int)DFCareer.Skills.Medical] = 20;
            s.skills[(int)DFCareer.Skills.ShortBlade] = 18;
            s.skills[(int)DFCareer.Skills.Axe] = 18;
            s.skills[(int)DFCareer.Skills.BluntWeapon] = 18;
            s.skills[(int)DFCareer.Skills.HandToHand] = 18;
            s.maxHealth = 25 + IdleData.HitPointsPerLevel;
            s.health = s.maxHealth;
            s.gold = 100;

            Genders g = (Genders)s.gender;
            Races r = (Races)s.race;
            Equip(w, ItemBuilder.CreateWeapon(Weapons.Broadsword, WeaponMaterialTypes.Iron));
            Equip(w, ItemBuilder.CreateArmor(g, r, Armor.Cuirass, ArmorMaterialTypes.Leather));
            Equip(w, ItemBuilder.CreateArmor(g, r, Armor.Greaves, ArmorMaterialTypes.Leather));
            Equip(w, ItemBuilder.CreateArmor(g, r, Armor.Boots, ArmorMaterialTypes.Leather));
            Equip(w, ItemBuilder.CreateArmor(g, r, Armor.Helm, ArmorMaterialTypes.Leather));
            Equip(w, ItemBuilder.CreateArmor(g, r, Armor.Gauntlets, ArmorMaterialTypes.Leather));
            Equip(w, ItemBuilder.CreateArmor(g, r, Armor.Left_Pauldron, ArmorMaterialTypes.Leather));
            Equip(w, ItemBuilder.CreateArmor(g, r, Armor.Right_Pauldron, ArmorMaterialTypes.Leather));
            Equip(w, ItemBuilder.CreateArmor(g, r, Armor.Buckler, ArmorMaterialTypes.Leather));
            return w;
        }

        static void Equip(IdleWorld w, DaggerfallUnityItem item)
        {
            int slot = SlotFor(item);
            if (slot >= 0)
                w.gear[slot] = item;
        }

        public static int SlotFor(DaggerfallUnityItem item)
        {
            if (item == null)
                return -1;
            if (item.ItemGroup == ItemGroups.Weapons)
                return (item.TemplateIndex == (int)Weapons.Arrow) ? -1 : IdleData.SlotWeapon;
            if (item.ItemGroup != ItemGroups.Armor)
                return -1;
            switch (item.TemplateIndex)
            {
                case (int)Armor.Helm: return 0;
                case (int)Armor.Right_Pauldron: return 1;
                case (int)Armor.Left_Pauldron: return 2;
                case (int)Armor.Cuirass: return 3;
                case (int)Armor.Gauntlets: return 4;
                case (int)Armor.Greaves: return 5;
                case (int)Armor.Boots: return 6;
                case (int)Armor.Buckler:
                case (int)Armor.Round_Shield:
                case (int)Armor.Kite_Shield:
                case (int)Armor.Tower_Shield:
                    return IdleData.SlotShield;
            }
            return -1;
        }

        static string Pack(List<DaggerfallUnityItem> items)
        {
            ItemCollection bag = new ItemCollection();
            for (int i = 0; i < items.Count; i++)
                bag.AddItem(items[i], ItemCollection.AddPosition.Back, true);
            return SaveLoadManager.Serialize(typeof(ItemData_v1[]), bag.SerializeItems(), false);
        }

        static List<DaggerfallUnityItem> Unpack(string blob)
        {
            List<DaggerfallUnityItem> items = new List<DaggerfallUnityItem>();
            if (string.IsNullOrEmpty(blob))
                return items;
            ItemData_v1[] data = (ItemData_v1[])SaveLoadManager.Deserialize(typeof(ItemData_v1[]), blob);
            ItemCollection bag = new ItemCollection();
            bag.DeserializeItems(data);
            for (int i = 0; i < bag.Count; i++)
                items.Add(bag.GetItem(i));
            return items;
        }

        public static void ToSave(IdleWorld w)
        {
            List<DaggerfallUnityItem> worn = new List<DaggerfallUnityItem>();
            for (int i = 0; i < w.gear.Length; i++)
                if (w.gear[i] != null)
                    worn.Add(w.gear[i]);
            w.s.gearBlob = Pack(worn);
            w.s.haulBlob = Pack(w.haul);
        }

        public static IdleWorld FromSave(IdleSave s)
        {
            IdleWorld w = new IdleWorld();
            w.s = s;
            List<DaggerfallUnityItem> worn = Unpack(s.gearBlob);
            for (int i = 0; i < worn.Count; i++)
                Equip(w, worn[i]);
            w.haul = Unpack(s.haulBlob);
            return w;
        }

        public static bool Usable(IdleSave s)
        {
            return s != null && s.version == IdleData.SaveVersion && s.stats != null && s.stats.Length == 8
                && s.skills != null && s.skills.Length == (int)DFCareer.Skills.Count
                && s.skillUses != null && s.skillUses.Length == s.skills.Length && s.maxHealth > 0;
        }

        // ------------------------------------------------------------------ enemies

        public static EnemyInfo Enemy(int id)
        {
            EnemyInfo e;
            if (enemies.TryGetValue(id, out e))
                return e;
            e = new EnemyInfo();
            e.id = id;
            EnemyBasics.GetEnemy((MobileTypes)id, out e.def);
            e.isClass = id >= 128;
            e.career = e.isClass
                ? DaggerfallEntity.GetClassCareerTemplate((ClassCareers)(id - 128))
                : DaggerfallEntity.GetMonsterCareerTemplate((MonsterCareers)id);
            e.name = (e.career != null && !string.IsNullOrEmpty(e.career.Name)) ? e.career.Name : ((MobileTypes)id).ToString();
            enemies[id] = e;
            return e;
        }

        static MobileTypes[] Table(int dungeonType)
        {
            RandomEncounterTable[] tables = RandomEncounters.EncounterTables;
            for (int i = 0; i < tables.Length; i++)
                if ((int)tables[i].DungeonType == dungeonType)
                    return tables[i].Enemies;
            return tables[0].Enemies;
        }

        static int EnemyLevel(IdleSave s, EnemyInfo e)
        {
            return (e.isClass ? s.level : e.def.Level) + s.enemyOver;
        }

        static int Scaled(IdleSave s, int value)
        {
            // Past the end of the encounter table every step makes the enemy 12% tougher, compounding.
            int steps = Mathf.Min(s.enemyOver, 60);
            for (int i = 0; i < steps; i++)
                value = value * 112 / 100 + 1;
            return value;
        }

        // ------------------------------------------------------------------ the hero's numbers

        public static string PhaseName(IdleSave s)
        {
            switch (s.phase)
            {
                case IdleData.PhaseOut: return "Travelling to the dungeon";
                case IdleData.PhaseExplore: return "Exploring floor " + s.floor;
                case IdleData.PhaseFight: return "Fighting on floor " + s.floor;
                case IdleData.PhaseBack: return (s.carried != 0) ? "Being carried home" : "Heading home";
            }
            return "In town";
        }

        static bool Works(DaggerfallUnityItem item)
        {
            return item != null && item.currentCondition > 0;
        }

        static bool IsBow(DaggerfallUnityItem item)
        {
            return item.TemplateIndex == (int)Weapons.Short_Bow || item.TemplateIndex == (int)Weapons.Long_Bow;
        }

        // The game's armor number for a body part: 100 unarmored, 5 lower per point of armor. It is added to the
        // attacker's chance to hit.
        public static int ArmorAt(IdleWorld w, int part)
        {
            int value = 100;
            DaggerfallUnityItem piece = w.gear[part];
            if (Works(piece))
                value -= piece.GetMaterialArmorValue() * 5;
            DaggerfallUnityItem shield = w.gear[IdleData.SlotShield];
            if (Works(shield))
            {
                BodyParts[] covered = shield.GetShieldProtectedBodyParts();
                for (int i = 0; i < covered.Length; i++)
                    if ((int)covered[i] == part)
                        value -= shield.GetShieldArmorValue() * 5;
            }
            return value;
        }

        static int StrengthBonus(IdleSave s)
        {
            return (int)Mathf.Floor((s.stats[IdleData.StatStrength] - 50) / 5f);
        }

        static float CarriedWeight(IdleWorld w)
        {
            float kg = 0f;
            for (int i = 0; i < w.gear.Length; i++)
                if (w.gear[i] != null)
                    kg += w.gear[i].weightInKg * w.gear[i].stackCount;
            for (int i = 0; i < w.haul.Count; i++)
                kg += w.haul[i].weightInKg * w.haul[i].stackCount;
            return kg;
        }

        // Rough worth of a weapon in the hero's hands: average damage times a hit chance against a plain target.
        static float WeaponScore(IdleSave s, DaggerfallUnityItem item)
        {
            if (item == null || !Works(item) || IsBow(item))
                return 0f;
            int skill = item.GetWeaponSkillIDAsShort();
            if (skill < 0 || skill >= s.skills.Length)
                return 0f;
            int mod = item.GetWeaponMaterialModifier();
            float damage = (item.GetBaseDamageMin() + item.GetBaseDamageMax()) / 2f + StrengthBonus(s) + mod;
            float chance = Mathf.Clamp(s.skills[skill] + mod * 10 + 20, 3, 97);
            return Mathf.Max(0f, damage) * chance + item.NativeMaterialValue * 0.01f;
        }

        static void Tally(IdleWorld w, int skill)
        {
            IdleSave s = w.s;
            if (skill < 0 || skill >= s.skills.Length || s.skills[skill] >= 100)
                return;
            s.skillUses[skill]++;
            int needed = (int)Math.Floor(s.skills[skill] * IdleData.SkillMultiplier(skill) * Math.Pow(1.04, s.level) * 2 / 5 + 1);
            if (s.skillUses[skill] < needed)
                return;
            s.skillUses[skill] = 0;
            s.skills[skill]++;
            s.skillRises++;
            if (w.record)
                w.Emit(IdleData.EvSkill, ((DFCareer.Skills)skill) + " rose to " + s.skills[skill]);

            // The game's level formula: one level for every 15 skill points gained.
            while ((s.skillRises + 28) / 15 > s.level)
            {
                s.level++;
                int gain = Range(s, IdleData.HitPointsPerLevel / 2, IdleData.HitPointsPerLevel) + s.stats[IdleData.StatEndurance] / 10 - 5;
                s.maxHealth += Mathf.Max(1, gain);
                s.unspentPoints += Range(s, 4, 6);
                if (s.autoSpend != 0)
                    SpendPoints(s);
                if (w.record)
                    w.Emit(IdleData.EvLevel, s.heroName + " reached level " + s.level);
            }
        }

        // Automatic attribute spending: strength, agility, endurance and speed in turn.
        public static void SpendPoints(IdleSave s)
        {
            int skipped = 0;
            while (s.unspentPoints > 0 && skipped < spendOrder.Length)
            {
                int stat = spendOrder[s.spendTurn % spendOrder.Length];
                s.spendTurn++;
                if (s.stats[stat] >= 100)
                {
                    skipped++;
                    continue;
                }
                skipped = 0;
                s.stats[stat]++;
                s.unspentPoints--;
            }
        }

        // ------------------------------------------------------------------ the loop

        public static void StartExpedition(IdleWorld w)
        {
            IdleSave s = w.s;
            if (s.phase != IdleData.PhaseTown)
                return;
            s.phase = IdleData.PhaseOut;
            s.phaseLeft = IdleData.TravelSeconds * IdleData.TicksPerSecond;
            s.floor = 0;
            s.carried = 0;
            s.runKills = 0;
            s.runGold = 0;
            s.runItems = 0;
            if (w.record)
                w.Emit(IdleData.EvInfo, s.heroName + " sets out, aiming for floor " + s.targetFloor);
        }

        public static void Advance(IdleWorld w, long ticks)
        {
            IdleSave s = w.s;
            while (ticks > 0)
            {
                if (s.phase == IdleData.PhaseTown)
                {
                    if (s.autoRepeat == 0 || (w.stopAfter > 0 && s.expeditions >= w.stopAfter))
                        return;
                    StartExpedition(w);
                }

                if (s.phase == IdleData.PhaseFight)
                {
                    int step = Mathf.Max(1, Mathf.Min(s.heroTimer, s.enemyTimer));
                    if (step > ticks)
                        step = (int)ticks;
                    s.heroTimer -= step;
                    s.enemyTimer -= step;
                    s.clock += step;
                    ticks -= step;
                    if (s.heroTimer <= 0)
                        HeroAttacks(w);
                    if (s.phase == IdleData.PhaseFight && s.enemyTimer <= 0)
                        EnemyAttacks(w);
                }
                else
                {
                    int step = (int)Math.Min(ticks, (long)Mathf.Max(1, s.phaseLeft));
                    s.phaseLeft -= step;
                    s.clock += step;
                    ticks -= step;
                    if (s.phase == IdleData.PhaseExplore)
                        Rest(s, step);
                    if (s.phaseLeft <= 0)
                        PhaseEnds(w);
                }
            }
        }

        // Between rooms the hero patches themselves up: Medical decides how fast.
        static void Rest(IdleSave s, int ticks)
        {
            if (s.health >= s.maxHealth)
                return;
            int perSecondPerMille = 10 + s.skills[(int)DFCareer.Skills.Medical] / 2;
            s.healCarry += ticks * s.maxHealth * perSecondPerMille;
            int unit = 1000 * IdleData.TicksPerSecond;
            int heal = s.healCarry / unit;
            s.healCarry -= heal * unit;
            s.health = Mathf.Min(s.maxHealth, s.health + heal);
        }

        static void Explore(IdleSave s)
        {
            s.phase = IdleData.PhaseExplore;
            s.phaseLeft = Range(s, IdleData.ExploreMinSeconds, IdleData.ExploreMaxSeconds) * IdleData.TicksPerSecond;
        }

        static void PhaseEnds(IdleWorld w)
        {
            IdleSave s = w.s;
            if (s.phase == IdleData.PhaseOut)
            {
                s.floor = 1;
                s.roomsLeft = IdleData.RoomsOnFloor(1);
                Explore(s);
                if (w.record)
                    w.Emit(IdleData.EvFloor, "Entered the dungeon");
            }
            else if (s.phase == IdleData.PhaseExplore)
            {
                Tally(w, (int)DFCareer.Skills.Medical);
                if (s.roomsLeft > 0)
                {
                    StartFight(w);
                }
                else if (s.floor >= s.targetFloor)
                {
                    s.cleared++;
                    if (s.autoDeeper != 0)
                        s.targetFloor++;
                    GoHome(w, "Floor " + s.floor + " cleared, heading home");
                }
                else
                {
                    s.floor++;
                    s.roomsLeft = IdleData.RoomsOnFloor(s.floor);
                    Explore(s);
                    if (w.record)
                        w.Emit(IdleData.EvFloor, "Down to floor " + s.floor);
                }
            }
            else if (s.phase == IdleData.PhaseBack)
            {
                ArriveHome(w);
            }
        }

        static void GoHome(IdleWorld w, string why)
        {
            IdleSave s = w.s;
            s.phase = IdleData.PhaseBack;
            s.phaseLeft = IdleData.TravelSeconds * IdleData.TicksPerSecond;
            s.enemyId = -1;
            if (w.record)
                w.Emit(IdleData.EvRetreat, why);
        }

        static void StartFight(IdleWorld w)
        {
            IdleSave s = w.s;
            MobileTypes[] table = Table(s.dungeonType);
            if (s.floor > s.deepestFloor)
                s.deepestFloor = s.floor;

            // Floor 1 rolls from the first two entries; each floor moves a three-wide window one step down the table.
            int index = Mathf.Max(0, s.floor - 2 + Range(s, 0, 2));
            s.enemyOver = Mathf.Max(0, index - (table.Length - 1));
            index = Mathf.Min(index, table.Length - 1);
            s.enemyId = (int)table[index];

            EnemyInfo e = Enemy(s.enemyId);
            int health;
            if (e.isClass)
            {
                health = 10;
                int perLevel = (e.career != null) ? Mathf.Max(1, e.career.HitPointsPerLevel) : 10;
                for (int i = 0; i < EnemyLevel(s, e); i++)
                    health += Range(s, 1, perLevel);
            }
            else
            {
                health = Scaled(s, Range(s, e.def.MinHealth, e.def.MaxHealth));
            }
            s.enemyMaxHealth = Mathf.Max(1, health);
            s.enemyHealth = s.enemyMaxHealth;

            int enemySpeed = (e.career != null) ? e.career.Speed : 50;
            s.heroTimer = Range(s, 3, IdleData.AttackInterval(s.stats[IdleData.StatSpeed]));
            s.enemyTimer = Range(s, 3, IdleData.AttackInterval(enemySpeed));
            s.phase = IdleData.PhaseFight;
            if (w.record)
                w.Emit(IdleData.EvFight, "A " + e.name + " attacks (" + s.enemyHealth + " health)");
        }

        // The game's to-hit sum: skill, the armor number of the struck part, luck and agility differences, the
        // defender's dodging, a critical strike bonus, +40 against monsters, and a flat -50.
        static bool Hits(IdleSave s, int chance, int armor, int luckDiff, int agilityDiff, int dodging, int critical, bool targetIsMonster)
        {
            chance += armor + luckDiff / 10 + agilityDiff / 10 - dodging / 4;
            if (Roll(s, critical))
                chance += critical / 10;
            if (targetIsMonster)
                chance += 40;
            chance -= 50;
            return Roll(s, Mathf.Clamp(chance, 3, 97));
        }

        static void Wear(IdleSave s, DaggerfallUnityItem item, int damage)
        {
            if (item == null || damage <= 0)
                return;
            int amount = (10 * damage + 50) / 100;
            if (amount == 0 && Roll(s, 20))
                amount = 1;
            item.currentCondition = Mathf.Max(0, item.currentCondition - amount);
        }

        static void HeroAttacks(IdleWorld w)
        {
            IdleSave s = w.s;
            EnemyInfo e = Enemy(s.enemyId);
            s.heroTimer = IdleData.AttackInterval(s.stats[IdleData.StatSpeed]);

            // Close to death the hero spends the turn trying to get away. A failed try costs the attack.
            if (s.health * 100 < s.maxHealth * s.fleePercent)
            {
                int enemySpeed = (e.career != null) ? e.career.Speed : 50;
                int escape = Mathf.Clamp(50 + (s.stats[IdleData.StatSpeed] - enemySpeed) / 2 + s.stats[IdleData.StatLuck] / 10, 15, 90);
                if (Roll(s, escape))
                {
                    s.retreats++;
                    s.fled++;
                    if (s.autoDeeper != 0 && s.targetFloor > 1)
                        s.targetFloor--;
                    GoHome(w, s.heroName + " flees from the " + e.name);
                }
                else if (w.record)
                {
                    w.Emit(IdleData.EvHeroMiss, s.heroName + " tries to flee and fails");
                }
                return;
            }

            // A weapon that is broken, or too soft a metal for this enemy, is put away and the hero uses fists.
            DaggerfallUnityItem weapon = w.gear[IdleData.SlotWeapon];
            if (weapon != null && (!Works(weapon) || IsBow(weapon) || (int)e.def.MinMetalToHit > weapon.NativeMaterialValue))
                weapon = null;

            int skill = (weapon != null) ? weapon.GetWeaponSkillIDAsShort() : (int)DFCareer.Skills.HandToHand;
            if (skill < 0 || skill >= s.skills.Length)
                skill = (int)DFCareer.Skills.HandToHand;
            int chance = s.skills[skill];
            if (weapon != null)
                chance += weapon.GetWeaponMaterialModifier() * 10;

            int enemySkill = Mathf.Min(100, EnemyLevel(s, e) * 5 + 30);
            int enemyLuck = (e.career != null) ? e.career.Luck : 50;
            int enemyAgility = (e.career != null) ? e.career.Agility : 50;
            int enemyArmor = e.isClass ? Mathf.Max(20, 100 - EnemyLevel(s, e) * 3) : e.def.ArmorValue * 5;
            int critical = s.skills[(int)DFCareer.Skills.CriticalStrike];

            Tally(w, skill);
            bool hit = Hits(s, chance, enemyArmor, s.stats[IdleData.StatLuck] - enemyLuck, s.stats[IdleData.StatAgility] - enemyAgility,
                enemySkill, critical, !e.isClass);
            if (!hit)
            {
                if (w.record)
                    w.Emit(IdleData.EvHeroMiss, s.heroName + " misses");
                return;
            }
            Tally(w, (int)DFCareer.Skills.CriticalStrike);

            int damage;
            if (weapon != null)
            {
                damage = Range(s, weapon.GetBaseDamageMin(), weapon.GetBaseDamageMax());
                if (s.enemyId == (int)MobileTypes.SkeletalWarrior)
                {
                    if ((weapon.flags & 0x10) == 0)
                        damage /= 2;
                    if (weapon.NativeMaterialValue == (int)WeaponMaterialTypes.Silver)
                        damage *= 2;
                }
                damage += StrengthBonus(s) + weapon.GetWeaponMaterialModifier();
                Wear(s, weapon, damage);
            }
            else
            {
                int hand = s.skills[(int)DFCareer.Skills.HandToHand];
                damage = Range(s, hand / 10 + 1, hand / 5 + 1) + StrengthBonus(s);
            }
            if (damage < 1)
                damage = 0;

            s.enemyHealth -= damage;
            if (w.record)
                w.Emit(IdleData.EvHeroHit, s.heroName + " hits the " + e.name + " for " + damage
                    + ((weapon == null && w.gear[IdleData.SlotWeapon] != null) ? " with bare hands" : ""));
            if (s.enemyHealth <= 0)
                Kill(w, e);
        }

        static void EnemyAttacks(IdleWorld w)
        {
            IdleSave s = w.s;
            EnemyInfo e = Enemy(s.enemyId);
            s.enemyTimer = IdleData.AttackInterval((e.career != null) ? e.career.Speed : 50);

            int skill = Mathf.Min(100, EnemyLevel(s, e) * 5 + 30);
            int part = bodyParts[Range(s, 0, bodyParts.Length - 1)];
            int armor = ArmorAt(w, part);
            int luckDiff = ((e.career != null) ? e.career.Luck : 50) - s.stats[IdleData.StatLuck];
            int agilityDiff = ((e.career != null) ? e.career.Agility : 50) - s.stats[IdleData.StatAgility];
            int dodging = s.skills[(int)DFCareer.Skills.Dodging];
            Tally(w, (int)DFCareer.Skills.Dodging);

            int damage = 0;
            if (e.isClass)
            {
                if (Hits(s, skill, armor, luckDiff, agilityDiff, dodging, skill, false))
                    damage = Range(s, skill / 10 + 1, skill / 5 + 1);
            }
            else
            {
                // Monsters have up to three attacks a round, each landing half the time before the to-hit roll.
                for (int n = 0; n < 3; n++)
                {
                    int min = (n == 0) ? e.def.MinDamage : (n == 1) ? e.def.MinDamage2 : e.def.MinDamage3;
                    int max = (n == 0) ? e.def.MaxDamage : (n == 1) ? e.def.MaxDamage2 : e.def.MaxDamage3;
                    if (min <= 0)
                        continue;
                    if (Roll(s, 50) && Hits(s, skill, armor, luckDiff, agilityDiff, dodging, skill, false))
                        damage += Scaled(s, Range(s, min, max));
                }
            }

            if (damage <= 0)
            {
                if (w.record)
                    w.Emit(IdleData.EvEnemyMiss, "The " + e.name + " misses");
                return;
            }

            DaggerfallUnityItem shield = w.gear[IdleData.SlotShield];
            bool onShield = false;
            if (Works(shield))
            {
                BodyParts[] covered = shield.GetShieldProtectedBodyParts();
                for (int i = 0; i < covered.Length; i++)
                    if ((int)covered[i] == part)
                        onShield = true;
            }
            Wear(s, onShield ? shield : w.gear[part], damage);

            s.health -= damage;
            if (w.record)
                w.Emit(IdleData.EvEnemyHit, "The " + e.name + " hits for " + damage + " (" + Mathf.Max(0, s.health) + "/" + s.maxHealth + ")");
            if (s.health <= 0)
                Die(w, e);
        }

        static void Kill(IdleWorld w, EnemyInfo e)
        {
            IdleSave s = w.s;
            s.kills++;
            s.runKills++;
            s.roomsLeft--;
            if (w.record)
                w.Emit(IdleData.EvKill, "The " + e.name + " is dead");
            RollLoot(w, e);
            s.enemyId = -1;
            if (s.health * 100 < s.maxHealth * s.retreatPercent)
            {
                s.retreats++;
                if (s.autoDeeper != 0 && s.targetFloor > 1 && s.floor < s.targetFloor)
                    s.targetFloor--;
                GoHome(w, "Badly hurt, retreating from floor " + s.floor);
            }
            else
            {
                Explore(s);
            }
        }

        static void Die(IdleWorld w, EnemyInfo e)
        {
            IdleSave s = w.s;
            s.deaths++;
            s.health = 0;
            s.carried = 1;
            int lost = s.gold * IdleData.DeathGoldLossPercent / 100;
            s.gold -= lost;
            w.haul.Clear();
            s.runGold = 0;
            s.runItems = 0;
            for (int i = 0; i < w.gear.Length; i++)
                if (w.gear[i] != null)
                    w.gear[i].currentCondition = Mathf.Max(0, w.gear[i].currentCondition - w.gear[i].maxCondition * IdleData.DeathWearPercent / 100);
            if (s.autoDeeper != 0 && s.targetFloor > 1)
                s.targetFloor--;
            if (w.record)
                w.Emit(IdleData.EvDied, s.heroName + " was killed by the " + e.name + " on floor " + s.floor + ". Loot lost, " + lost + " gold lost");
            GoHome(w, "Carried back to town");
        }

        // ------------------------------------------------------------------ loot and town

        // The game's loot roll for an enemy's table, with our own level in place of the player's. The item builders
        // use Unity's random numbers, so those are seeded from ours for the roll and put back afterwards.
        static void RollLoot(IdleWorld w, EnemyInfo e)
        {
            IdleSave s = w.s;
            if (string.IsNullOrEmpty(e.def.LootTableKey))
                return;
            LootChanceMatrix m = LootTables.GetMatrix(e.def.LootTableKey);
            int level = Mathf.Max(1, (s.level + s.floor + 1) / 2);
            Genders g = (Genders)s.gender;
            Races r = (Races)s.race;

            int gold = Range(s, m.MinGold, m.MaxGold) * level;
            if (gold > 0)
            {
                s.runGold += gold;
                if (w.record)
                    w.Emit(IdleData.EvLoot, "Found " + gold + " gold");
            }

            UnityEngine.Random.State keep = UnityEngine.Random.state;
            UnityEngine.Random.InitState(Next(s));
            try
            {
                float c;
                for (c = m.WP; Dice100.SuccessRoll((int)c); c *= 0.5f) Take(w, ItemBuilder.CreateRandomWeapon(level));
                for (c = m.AM; Dice100.SuccessRoll((int)c); c *= 0.5f) Take(w, ItemBuilder.CreateRandomArmor(level, g, r));
                for (c = m.C1 * level; Dice100.SuccessRoll((int)c); c *= 0.5f) Take(w, ItemBuilder.CreateRandomIngredient(ItemGroups.CreatureIngredients1));
                for (c = m.C2 * level; Dice100.SuccessRoll((int)c); c *= 0.5f) Take(w, ItemBuilder.CreateRandomIngredient(ItemGroups.CreatureIngredients2));
                for (c = m.C3; Dice100.SuccessRoll((int)c); c *= 0.5f) Take(w, ItemBuilder.CreateRandomIngredient(ItemGroups.CreatureIngredients3));
                for (c = m.P1 * level; Dice100.SuccessRoll((int)c); c *= 0.5f) Take(w, ItemBuilder.CreateRandomIngredient(ItemGroups.PlantIngredients1));
                for (c = m.P2 * level; Dice100.SuccessRoll((int)c); c *= 0.5f) Take(w, ItemBuilder.CreateRandomIngredient(ItemGroups.PlantIngredients2));
                for (c = m.M1; Dice100.SuccessRoll((int)c); c *= 0.5f) Take(w, ItemBuilder.CreateRandomIngredient(ItemGroups.MiscellaneousIngredients1));
                for (c = m.M2; Dice100.SuccessRoll((int)c); c *= 0.5f) Take(w, ItemBuilder.CreateRandomIngredient(ItemGroups.MiscellaneousIngredients2));
                for (c = m.MI; Dice100.SuccessRoll((int)c); c *= 0.5f) Take(w, ItemBuilder.CreateRandomMagicItem(level, g, r));
                for (c = m.CL; Dice100.SuccessRoll((int)c); c *= 0.5f) Take(w, ItemBuilder.CreateRandomClothing(g, r));
                for (c = m.BK; Dice100.SuccessRoll((int)c); c *= 0.5f) Take(w, ItemBuilder.CreateRandomBook());
                for (c = m.RL; Dice100.SuccessRoll((int)c); c *= 0.5f) Take(w, ItemBuilder.CreateRandomReligiousItem());
            }
            finally
            {
                UnityEngine.Random.state = keep;
            }
        }

        static float Worth(DaggerfallUnityItem item)
        {
            return item.value * item.stackCount / Mathf.Max(0.05f, item.weightInKg * item.stackCount);
        }

        // When the pack is full the hero keeps whatever is worth most for its weight.
        static void Take(IdleWorld w, DaggerfallUnityItem item)
        {
            IdleSave s = w.s;
            if (item == null)
                return;
            float need = item.weightInKg * item.stackCount;
            float room = s.stats[IdleData.StatStrength] * 1.5f - CarriedWeight(w);
            while (need > room)
            {
                int worst = -1;
                for (int i = 0; i < w.haul.Count; i++)
                    if (w.haul[i].weightInKg > 0f && (worst < 0 || Worth(w.haul[i]) < Worth(w.haul[worst])))
                        worst = i;
                if (worst < 0 || Worth(w.haul[worst]) >= Worth(item))
                {
                    s.leftBehind++;
                    if (w.record)
                        w.Emit(IdleData.EvLoot, "Too heavy to carry: " + item.LongName);
                    return;
                }
                room += w.haul[worst].weightInKg * w.haul[worst].stackCount;
                s.leftBehind++;
                s.runItems--;
                if (w.record)
                    w.Emit(IdleData.EvLoot, "Dropped " + w.haul[worst].LongName + " to make room");
                w.haul.RemoveAt(worst);
            }
            w.haul.Add(item);
            s.runItems++;
            if (w.record)
                w.Emit(IdleData.EvLoot, "Picked up " + item.LongName);
        }

        static bool IsUpgrade(IdleWorld w, DaggerfallUnityItem item, int slot)
        {
            IdleSave s = w.s;
            DaggerfallUnityItem old = w.gear[slot];
            if (slot == IdleData.SlotWeapon)
            {
                if (WeaponScore(s, item) <= WeaponScore(s, old))
                    return false;
                // A two-handed weapon is only worth it if nothing is on the shield arm.
                return ItemEquipTable.GetItemHands(item) != ItemHands.Both || w.gear[IdleData.SlotShield] == null;
            }
            if (slot == IdleData.SlotShield)
            {
                DaggerfallUnityItem weapon = w.gear[IdleData.SlotWeapon];
                if (weapon != null && ItemEquipTable.GetItemHands(weapon) == ItemHands.Both)
                    return false;
                return old == null || !Works(old) || item.GetShieldArmorValue() > old.GetShieldArmorValue();
            }
            return old == null || !Works(old) || item.GetMaterialArmorValue() > old.GetMaterialArmorValue();
        }

        static void ArriveHome(IdleWorld w)
        {
            IdleSave s = w.s;
            s.expeditions++;
            s.phase = IdleData.PhaseTown;
            s.phaseLeft = 0;
            s.health = s.maxHealth;
            s.healCarry = 0;

            int sold = 0;
            int equipped = 0;
            for (int i = 0; i < w.haul.Count; i++)
            {
                DaggerfallUnityItem item = w.haul[i];
                int slot = SlotFor(item);
                if (s.autoEquip != 0 && slot >= 0 && Works(item) && IsUpgrade(w, item, slot))
                {
                    DaggerfallUnityItem old = w.gear[slot];
                    w.gear[slot] = item;
                    equipped++;
                    if (w.record)
                        w.Emit(IdleData.EvHome, "Now using " + item.LongName);
                    item = old;
                }
                if (item != null)
                    sold += Mathf.Max(1, item.value * item.stackCount / IdleData.SaleDivisor);
            }
            w.haul.Clear();

            int income = s.runGold + sold;
            s.gold += income;
            s.goldEarned += income;

            // Repairs, cheapest first come first served: a quarter of the item's value for a full repair.
            int repairs = 0;
            for (int i = 0; i < w.gear.Length; i++)
            {
                DaggerfallUnityItem item = w.gear[i];
                if (item == null || item.maxCondition <= 0 || item.currentCondition >= item.maxCondition)
                    continue;
                int cost = Mathf.Max(1, (int)((long)item.value * (item.maxCondition - item.currentCondition) / item.maxCondition / 4));
                if (cost > s.gold)
                    continue;
                s.gold -= cost;
                repairs += cost;
                item.currentCondition = item.maxCondition;
            }

            if (w.record)
                w.Emit(IdleData.EvHome, "Back in town: " + s.runKills + " kills, " + s.runGold + " gold found, loot sold for " + sold
                    + ", repairs " + repairs + ((equipped > 0) ? ", " + equipped + " upgrade(s)" : ""));
            s.carried = 0;
        }

        // ------------------------------------------------------------------ test harness

        public static string Summary(IdleWorld w)
        {
            IdleSave s = w.s;
            string gear = "";
            for (int i = 0; i < w.gear.Length; i++)
                if (w.gear[i] != null)
                    gear += (gear.Length > 0 ? ", " : "") + w.gear[i].LongName;
            return "level " + s.level + ", health " + s.maxHealth + ", gold " + s.gold + ", runs " + s.expeditions + " (cleared " + s.cleared
                + ", retreated " + s.retreats + ", died " + s.deaths + "), kills " + s.kills + ", deepest floor " + s.deepestFloor
                + ", fled " + s.fled + ", left behind " + s.leftBehind + ", time " + Hours(s) + " h, gold earned per hour "
                + (long)(s.goldEarned / Mathf.Max(0.05f, s.clock / 36000f)) + " | " + gear;
        }

        static string Hours(IdleSave s)
        {
            return (s.clock / 36000f).ToString("0.0");
        }

        // Runs a fresh hero through a number of expeditions without touching the real save.
        public static IdleWorld Simulate(int seed, int expeditions, bool deeper)
        {
            IdleWorld w = NewHero(seed, "Test");
            w.s.autoRepeat = 1;
            w.s.autoDeeper = deeper ? 1 : 0;
            if (!deeper)
                w.s.targetFloor = 1;
            w.stopAfter = expeditions;
            for (int guard = 0; guard < 4000 && w.s.expeditions < expeditions; guard++)
                Advance(w, 36000);
            return w;
        }
    }
}
