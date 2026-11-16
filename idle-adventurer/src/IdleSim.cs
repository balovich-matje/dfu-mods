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
        public const int EvPotion = 14;
        public const int EvCast = 15;           // the hero's spell at the enemy; value is the damage
        public const int EvCastSelf = 16;       // the hero's spell on themselves; value is health healed
        public const int EvEnemyCast = 17;      // the enemy's spell; value is the damage to the hero
        public const int EvMine = 18;           // mining: a vein found, mined, a pickaxe broken

        // Champion and boss traits, as bits in IdleSave.enemyTraits.
        public const int TraitSwift = 1;            // attacks 40% more often
        public const int TraitArmoured = 2;         // the hero's chance to hit is 25 lower
        public const int TraitRegenerating = 4;     // heals 4% of its health every time it attacks
        public const int TraitVenomous = 8;         // hits do 40% more damage
        public const int TraitVampiric = 16;        // heals half the damage it does
        public const int TraitEnraged = 32;         // does 60% more damage below half health
        public const int TraitBoss = 64;
        public const int TraitKinds = 6;
        public static readonly string[] TraitNames = { "Swift", "Armoured", "Regenerating", "Venomous", "Vampiric", "Enraged" };
        public const int BossEvery = 5;             // the last fight of every fifth floor is a boss

        // Orders: "if CONDITION then ACTION", first match wins, checked on every one of the hero's turns.
        public const int RuleSlots = 6;
        public const int IfAlways = 0, IfHealth50 = 1, IfHealth30 = 2, IfHealth15 = 3, IfCannotHurt = 4, IfChampion = 5, IfEnemyLow = 6, IfOff = 7,
            IfCaster = 8, IfMagicka = 9;
        public const int IfKinds = 10;
        public static readonly string[] IfNames = { "always", "my health is below 50%", "my health is below 30%", "my health is below 15%",
            "my weapon cannot hurt it", "it is a champion or boss", "its health is below 30%", "(rule off)", "it casts spells",
            "my magicka is above half" };
        // The order the orders screen steps through the conditions; "rule off" stays last.
        public static readonly int[] IfOrder = { IfAlways, IfHealth50, IfHealth30, IfHealth15, IfCannotHurt, IfChampion, IfEnemyLow, IfCaster, IfMagicka, IfOff };
        public const int DoAttack = 0, DoPotion = 1, DoFlee = 2, DoPower = 3, DoGuard = 4, DoCast = 5, DoHeal = 6, DoShield = 7, DoParalyse = 8, DoWard = 9;
        public const int DoKinds = 10;
        public static readonly string[] DoNames = { "attack", "drink a healing potion", "flee", "power attack", "guard",
            "cast a damage spell", "cast Heal", "cast Shield", "cast Paralysis", "cast Spell Shield" };
        public static readonly int[] DefaultIf = { IfHealth30, IfHealth50, IfHealth30, IfCannotHurt, IfCannotHurt, IfChampion };
        public static readonly int[] DefaultDo = { DoPotion, DoHeal, DoFlee, DoCast, DoFlee, DoCast };
        // The defaults before spells, so saves still using them get the new ones.
        public static readonly int[] OldDefaultIf = { IfHealth30, IfHealth30, IfCannotHurt, IfAlways, IfOff, IfOff };
        public static readonly int[] OldDefaultDo = { DoPotion, DoFlee, DoFlee, DoAttack, DoAttack, DoAttack };

        // Spells. Names, schools, elements and magnitudes are the game's own (SPELLS.STD); a magnitude is
        // low-high plus low-high again for every caster level. Shield's magnitude is raised: the game's 1-12 plus 1
        // per level absorbs almost nothing here. Costs are ours, tied to the average magnitude.
        public const int SpellDamage = 0, SpellHeal = 1, SpellShieldKind = 2, SpellParalyse = 3, SpellWard = 4;
        public const int ElementFire = 0, ElementCold = 1, ElementPoison = 2, ElementShock = 3, ElementMagic = 4;
        public static readonly string[] ElementNames = { "fire", "frost", "poison", "shock", "magic" };
        public const int SpellShock = 0, SpellHealIndex = 5, SpellShieldIndex = 6, SpellParalysisIndex = 7, SpellWardIndex = 8;
        public static readonly string[] SpellNames = { "Shock", "Wizard's Fire", "Ice Bolt", "Toxic Cloud", "Force Bolt", "Heal", "Shield", "Paralysis", "Spell Shield" };
        public static readonly int[] SpellKind = { SpellDamage, SpellDamage, SpellDamage, SpellDamage, SpellDamage, SpellHeal, SpellShieldKind, SpellParalyse, SpellWard };
        public static readonly int[] SpellElement = { ElementShock, ElementFire, ElementCold, ElementPoison, ElementMagic, -1, -1, -1, -1 };
        public static readonly int[] SpellSchool = { (int)DFCareer.Skills.Destruction, (int)DFCareer.Skills.Destruction, (int)DFCareer.Skills.Destruction,
            (int)DFCareer.Skills.Destruction, (int)DFCareer.Skills.Destruction, (int)DFCareer.Skills.Restoration, (int)DFCareer.Skills.Alteration,
            (int)DFCareer.Skills.Alteration, (int)DFCareer.Skills.Thaumaturgy };
        public static readonly int[] SpellBaseLow = { 1, 1, 1, 1, 1, 1, 5, 0, 0 };
        public static readonly int[] SpellBaseHigh = { 20, 15, 35, 25, 30, 10, 15, 0, 0 };
        public static readonly int[] SpellLevelLow = { 1, 1, 1, 1, 1, 5, 2, 0, 0 };
        public static readonly int[] SpellLevelHigh = { 4, 4, 5, 5, 10, 5, 4, 0, 0 };
        public static readonly int[] SpellPrice = { 0, 120, 900, 700, 3000, 300, 450, 1500, 1200 };
        public const int SpellKinds = 9;

        // Affixes: a prefix and a suffix that dropped weapons and armor can roll. Values are at floor 1 and grow 6%
        // a floor. Kept beside the item in our own save, never inside the item.
        public const int AffixKeen = 0, AffixCruel = 1, AffixLeech = 2, AffixSwift = 3, AffixSturdy = 4, AffixTough = 5,
            AffixMending = 6, AffixNimble = 7, AffixWarding = 8, AffixSorcery = 9, AffixArcane = 10, AffixFortune = 11;
        public const int AffixKinds = 12;       // Fortune only comes from amber, never as an enchantment
        public static readonly string[] AffixWords = { "Keen", "Cruel", "of the Leech", "of Swiftness", "Sturdy", "of Toughness",
            "of Mending", "Nimble", "of Warding", "of Sorcery", "Arcane", "of Fortune" };
        public static readonly bool[] AffixPrefix = { true, true, false, false, true, false, false, true, false, false, true, false };
        public static readonly int[] AffixOn = { 1, 1, 1, 1, 0, 0, 0, 0, 0, 2, 2, -1 };    // 1 weapons, 0 armor, 2 either, -1 never rolled
        public static readonly int[] AffixMin = { 3, 1, 2, 4, 1, 1, 10, 2, 2, 3, 3, 0 };
        public static readonly int[] AffixMax = { 8, 3, 6, 10, 3, 2, 30, 6, 6, 8, 10, 0 };
        public static readonly int[] AffixCap = { 60, 40, 100, 40, 99, 15, 200, 40, 40, 50, 200, 200 };
        public static readonly string[] AffixText = { "+{0} to hit", "+{0} damage", "heals {0} for every kill", "attacks {0}% faster",
            "+{0} armor", "{0} less damage from every hit", "heals {0}% faster between fights", "+{0} dodging",
            "+{0}% to resist spells", "spells cost {0}% less", "+{0} magicka", "+{0}% gold found" };
        public const int MaxEnchants = 3;

        // Gems: socketed into weapons and armor, levels 1 to 10. What a gem does depends on where it sits; values are
        // tenths per gem level. The jeweller adds sockets and raises gem levels for gold (from level 6 a second gem of
        // the same kind is used up too).
        public const int GemKinds = 8, GemTop = 10, GemCodes = 16 * 8;
        public static readonly string[] GemNames = { "Ruby", "Sapphire", "Diamond", "Emerald", "Turquoise", "Jade", "Malachite", "Amber" };
        public static readonly string[] GemLevelNames = { "", "Chipped", "Flawed", "Rough", "Plain", "Polished", "Fine", "Flawless", "Radiant", "Perfect", "Royal" };
        public static readonly int[] GemWeapon = { AffixCruel, AffixSorcery, AffixKeen, AffixLeech, AffixSwift, AffixWarding, AffixArcane, AffixFortune };
        public static readonly int[] GemWeaponTenths = { 10, 20, 30, 10, 20, 30, 40, 30 };
        public static readonly int[] GemArmor = { AffixTough, AffixArcane, AffixSturdy, AffixMending, AffixNimble, AffixWarding, AffixSorcery, AffixFortune };
        public static readonly int[] GemArmorTenths = { 5, 40, 10, 80, 20, 30, 20, 30 };
        public static readonly int[] GemWeaponOrder = { 0, 2, 4, 3, 7, 6, 1, 5 };      // which gem auto-socketing prefers
        public static readonly int[] GemArmorOrder = { 2, 0, 3, 4, 5, 7, 1, 6 };
        public static readonly int[] MaxSockets = { 1, 1, 1, 3, 1, 2, 1, 2, 3 };        // per slot, as SlotNames
        public const int GemSecondFrom = 6;
        public static int GemUpgradeGold(int level) { return 200 << Math.Max(0, level - 1); }
        public static int GemRemoveGold(int level) { return 50 * level * level; }
        public static int SocketGold(int have, int rung) { int n = have + 1; return 300 * n * n * n * (3 + rung) / 3; }

        // Soul gems: bought empty at the Mages Guild, filled by trapping the soul of a slain creature (not people). The
        // soul's strength comes from the creature's level. The enchanter spends a filled gem and gold to add an
        // enchantment to a piece with a free slot, or to roll one of its enchantments anew.
        public const int SoulTiers = 5;
        public static readonly string[] SoulNames = { "Petty", "Lesser", "Common", "Greater", "Grand" };
        public static readonly int[] SoulPower = { 70, 90, 110, 135, 165 };        // percent of an enchantment's usual value
        public const int SoulGemPrice = 150;
        public const int SoulTrapChance = 30;
        public static int SoulTier(int enemyLevel) { return Math.Min(SoulTiers - 1, Math.Max(0, enemyLevel / 5)); }
        public static int EnchantGold(int existing, int tier) { return 250 * (tier + 1) * (tier + 1) * (existing + 1) * (existing + 1); }
        public static int RerollGold(int tier) { return 150 * (tier + 1) * (tier + 1); }

        // Callings: what a new hero starts as. Warriors wear plate, mages carry half again the magicka and start with
        // spells, rogues land poison more often and start knowing the first poison.
        public const int ClassWarrior = 0, ClassMage = 1, ClassRogue = 2, Classes = 3;
        public static readonly string[] ClassNames = { "Warrior", "Mage", "Rogue" };
        public static readonly string[] ClassText = {
            "Long blades, shield and heavy armor. Strong and hardy; the default.",
            "A staff and light armor; half again the magicka, starts knowing Shock, Wizard's Fire and Heal, and casts first.",
            "A short blade and leather; quick and lucky, poisons land far more often, starts knowing Nux Vomica." };

        // Spell ranks I to X: each rank adds 15% to the spell's strength and takes 4% off its cost.
        public const int RankTop = 10;
        public static int RankGold(int spell, int rank) { return (SpellPrice[spell] + 250) * rank * rank; }

        // Poisons, the game's own: a recipe learned once at the alchemist, then a vial bought before each run coats
        // the weapon for that run. A coated hit sometimes adds the poison's damage; undead, daedra, atronachs and
        // gargoyles shrug it off.
        public static readonly string[] PoisonNames = { "Nux Vomica", "Arsenic", "Moonseed", "Drothweed", "Pyrrhic Acid", "Magebane" };
        public static readonly int[] PoisonRecipe = { 300, 1500, 6000, 20000, 60000, 150000 };
        public static readonly int[] PoisonVial = { 40, 120, 350, 900, 2200, 5500 };
        public static readonly int[] PoisonDamage = { 3, 6, 10, 16, 24, 35 };
        public const int PoisonKinds = 6;
        public const int PoisonChance = 25, RoguePoisonChance = 45;
        public static readonly string[] EnchantNames = { "Plain", "Enchanted", "Twice enchanted", "Thrice enchanted" };

        // Reinforcement (+1 to +25): every level adds 6% of a weapon's base damage, or 1 armor every 2 levels. The
        // metal sets the highest level. Steps cost gold, growing with the square of the level, and ingots of the
        // item's own metal. Weapons climb iron to daedric; armor climbs leather and chain (both use iron ingots),
        // then plate iron to daedric.
        public const int Metals = 10;
        public static readonly string[] MetalNames = { "Iron", "Steel", "Silver", "Elven", "Dwarven", "Mithril", "Adamantium", "Ebony", "Orcish", "Daedric" };
        public static readonly int[] MetalCap = { 6, 8, 10, 12, 14, 16, 18, 20, 22, 25 };
        public const int LeatherCap = 4, ChainCap = 5;
        public static readonly int[] MetalGold = { 10, 15, 25, 40, 60, 90, 130, 180, 250, 400 };        // times (level + 1) squared
        public static readonly int[] IngotPrice = { 25, 50, 80, 120, 180, 260, 380, 550, 800, 1200 };
        public const int LevelDamagePercent = 6;
        public const int ReforgeGold = 30;          // times the new metal's MetalGold

        // Mining. Veins only appear in mines, natural caves and volcanic caves. Ores by metal (steel is smelted from
        // iron ore and coal, daedric from ebony ore and a daedra heart). A pickaxe mines ore up to one metal above
        // its own. The smith only sells iron and steel ingots; the rest are mined, smelted or salvaged.
        public const int DungeonMine = 5, DungeonNaturalCave = 6, DungeonVolcanicCaves = 16;
        public static readonly string[] OreNames = { "Iron ore", "", "Silver ore", "Moonstone", "Dwarven ore", "Mithril ore",
            "Adamantium ore", "Ebony ore", "Orichalcum", "" };
        public static readonly int[] OreMetals = { 0, 2, 3, 4, 5, 6, 7, 8 };
        public static readonly int[] OreFloor = { 1, 4, 8, 12, 17, 22, 28, 34 };     // first floor where each ore shows
        public const int SoldIngots = 2;            // iron and steel
        public const int Unbuyable = 1 << 28;       // the price of something the smith does not sell
        public const int PickPrice = 60;            // an iron pickaxe at the smith
        public const int PickIngots = 3;
        public const int OrePerIngot = 2;
        public const int SalvageChance = 20;        // percent: an ingot from selling a metal weapon or armor piece
        public static int PickGold(int metal) { return 50 * (metal + 1); }
        public static int PickUses(int metal) { return 8 + 3 * metal; }
        public static int MineSeconds(int metal) { return 15 + 3 * metal; }
        public static int SmeltSeconds(int metal) { return 12 + 6 * metal; }
        public static int SmeltFee(int metal) { return 4 + 6 * metal; }
        public static int RushGold(int metal, int secondsLeft) { return Math.Max(1, secondsLeft * (2 + metal) / 2); }
        public static bool IsCave(int dungeonType)
        {
            return dungeonType == DungeonMine || dungeonType == DungeonNaturalCave || dungeonType == DungeonVolcanicCaves;
        }

        // What spells did to the current fight.
        public const int StatusHidden = 1;          // the enemy is invisible or a chameleon: the hero's swings -30 to hit
        public const int StatusEnemyWard = 2;       // the enemy's Spell Shield: the hero's spells do half
        public const int StatusSilenced = 4;        // the hero cannot cast
        public const int StatusWard = 8;            // the hero's Spell Shield: enemy spells resisted more, halved
        public const int StatusParalysed = 16;      // the enemy was paralysed this fight (once per fight)

        // Average magnitude of a spell at a caster level.
        public static int SpellAverage(int spell, int level)
        {
            return (SpellBaseLow[spell] + SpellBaseHigh[spell] + (SpellLevelLow[spell] + SpellLevelHigh[spell]) * Mathf.Max(1, level)) / 2;
        }

        // Magicka for one cast: tied to the average magnitude, then lowered by the spell's school skill (a third off at 50).
        public static int SpellCost(int spell, int level, int schoolSkill)
        {
            int cost;
            switch (SpellKind[spell])
            {
                case SpellParalyse: cost = 15 + level; break;
                case SpellWard: cost = 12 + level; break;
                default: cost = 5 + SpellAverage(spell, level) * 2 / 5; break;
            }
            return Mathf.Max(1, cost * (150 - Mathf.Clamp(schoolSkill, 0, 100)) / 150);
        }

        public static int MaxMagicka(int intelligence, int level)
        {
            return intelligence + 4 * level;
        }

        public static int PotionPrice(int level)
        {
            return 15 + 3 * level;
        }

        public const int StatStrength = 0;
        public const int StatIntelligence = 1;
        public const int StatWillpower = 2;
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
        public const int StashSize = 24;                // spare weapons and armor kept in town

        public const int DungeonTypes = 19;             // DFRegion.DungeonTypes 0-18
        public const int RunFloors = 3;                 // with "skip cleared floors" a run covers this many floors
        public const int ShopSize = 8;
        public const int PackStepKg = 25;
        public const int PackLevels = 8;

        public static readonly string[] DungeonNames = { "Crypt", "Orc stronghold", "Human stronghold", "Prison", "Desecrated temple", "Mine",
            "Natural cave", "Coven", "Vampire haunt", "Laboratory", "Harpy nest", "Ruined castle", "Spider nest", "Giant stronghold",
            "Dragon's den", "Barbarian stronghold", "Volcanic caves", "Scorpion nest", "Cemetery" };

        public static int PackCost(int level)
        {
            int cost = 400;
            for (int i = 0; i < level; i++)
                cost *= 4;
            return cost;
        }

        public static readonly string[] SlotNames = { "Head", "Right arm", "Left arm", "Chest", "Hands", "Legs", "Feet", "Shield", "Weapon" };

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
        public string stashBlob;
        public string shopBlob;

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
        public int phaseTotal;              // length of the current timed phase, for progress bars
        public int dungeonSeed;             // picks the layout of each floor in the first-person view
        public int floorTicks;              // time spent walking on this floor; the view turns it into a position
        public int recall;                  // 1 when the player has ordered the hero home
        public int dungeonType = IdleData.DefaultDungeonType;
        public string destName = "";        // the dungeon's name on the map
        public int travelSeconds = IdleData.TravelSeconds;
        public int nextType = -1;           // a destination chosen while the hero is away; used from the next run on
        public string nextName = "";
        public int nextTravel;
        public int[] clearedFloors;               // per dungeon type: the deepest floor cleared
        public int[] targets;               // per dungeon type: the target floor last used there
        public int skipCleared = 1;         // start a run a few floors above the target instead of at floor 1
        public int startFloor = 1;
        public int packLevel;
        public int floor;
        public int roomsLeft;
        public int carried;                 // 1 while the hero is being carried home after dying

        public int enemyId = -1;
        public int enemyHealth;
        public int enemyMaxHealth;
        public int enemyOver;               // how far past the end of the encounter table this enemy was rolled
        public int enemyTraits;             // champion and boss bits, IdleData.Trait*
        public int guarding;                // 1 after the hero chose to guard, until the enemy's next attack
        public int heroTimer;
        public int enemyTimer;

        // Orders.
        public int targetFloor = 2;
        public int retreatPercent = 35;
        public int fleePercent = 25;        // no longer used: replaced by the orders (ruleIf / ruleDo)
        public int autoRepeat;
        public int autoDeeper = 1;
        public int autoEquip = 1;
        public int autoSpend = 1;
        public int[] ruleIf;
        public int[] ruleDo;
        public int potions = 2;
        public int potionsToCarry = 3;
        public int heroClass;               // IdleData class; 0 warrior for heroes from before callings
        public int classChosen;             // 1 once the player picked a calling
        public int[] spellRanks;            // per spell, 0 for rank I
        public int poisonsKnown;            // how many poison recipes, in order
        public bool noPoison;               // true: do not coat the weapon
        public int coated;                  // poison tier + 1 on the weapon this run; 0 none
        public int poisonHits, vialsUsed;
        public long alchemyGold, spellRankGold;
        public int soulGemsToCarry = 2;     // empty soul gems bought before each run
        public int soulGemsEmpty;
        public int[] souls;                 // filled soul gems per IdleData soul tier
        public int soulsTrapped, enchantsAdded, rerolls;
        public long enchantGold;
        public int potionsDrunk;
        public int championsKilled;
        public int bossesKilled;
        public int magicka = -1;            // -1 until first filled
        public int magickaCarry;
        public int[] spellsKnown;           // per IdleData spell: 1 when known
        public int heroShield;              // damage the hero's Shield spell still absorbs this fight
        public int fightStatus;             // IdleData.Status* bits for the current fight
        public int enemyCasts;              // spells the enemy can still cast this fight
        public int spellsCast;
        public int enemySpellsCast;
        public string gearAffixes;          // affixes of the items in gearBlob, haulBlob, stashBlob, shopBlob, in the same order
        public string haulAffixes;
        public string stashAffixes;
        public string shopAffixes;
        public int affixesFound;
        public int[] ingots;                // per IdleData metal, in the materials pouch
        public bool noAutoSmith;            // true: the smith only works when told to
        public int levelsBought;            // reinforcement steps paid for
        public int reforges;
        public long smithGold;              // gold spent at the smith on reinforcing and reforging
        public int[] ore;                   // per IdleData metal, in the materials pouch
        public int coal;
        public int hearts;                  // daedra hearts
        public int pick;                    // pickaxe metal + 1; 0 none
        public int pickUses;                // veins left before it breaks (a daedric one never does)
        public int vein;                    // this floor's vein: ore metal + 1; 0 none or done
        public int miningMetal;             // ore metal + 1 while mining; 0 not mining
        public int[] smeltQueue;            // ingots waiting per metal, already paid for
        public int smeltMetal;              // metal + 1 being smelted; 0 idle
        public int smeltLeft;               // ticks left on the current ingot
        public int veinsMined, oreMined, ingotsSmelted, ingotsSalvaged, picksBroken;
        public int[] gems;                  // the pouch's gems: count per code (kind * 16 + level)
        public int gemsFound, socketsAdded, gemUpgrades;
        public long jewelGold;
        [NonSerialized] public int[] bonus = new int[IdleData.AffixKinds];     // worn affixes, summed and capped

        // This run and lifetime tallies.
        public int runKills;
        public int runGold;
        public int runItems;
        public int expeditions;
        public int clears;
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
        public int value;
        public string text;
        public int element = -1;            // for spells: IdleData.Element*, or -1
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
        public List<DaggerfallUnityItem> stash = new List<DaggerfallUnityItem>();
        public List<DaggerfallUnityItem> shop = new List<DaggerfallUnityItem>();
        public List<IdleEvent> events = new List<IdleEvent>();
        public bool record;
        public int keepEvents = 400;
        public long eventTotal;             // events ever emitted, so a viewer can tell which ones are new
        public int stopAfter;               // test runs: stop in town once this many expeditions are done

        public void Emit(int type, string text, int value = 0)
        {
            IdleEvent e = new IdleEvent();
            e.type = type;
            e.text = text;
            e.value = value;
            events.Add(e);
            eventTotal++;
            if (events.Count > keepEvents)
                events.RemoveRange(0, keepEvents / 2);
        }

        public void EmitSpell(int type, string text, int value, int element)
        {
            Emit(type, text, value);
            events[events.Count - 1].element = element;
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
            return NewHero(seed, name, IdleData.ClassWarrior);
        }

        public static IdleWorld NewHero(int seed, string name, int heroClass)
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
            s.heroClass = Mathf.Clamp(heroClass, 0, IdleData.Classes - 1);
            Arrays(s);
            if (s.heroClass == IdleData.ClassMage)
            {
                s.stats = new int[] { 40, 70, 60, 50, 45, 45, 50, 50 };
                s.skills[(int)DFCareer.Skills.LongBlade] = 10;
                s.skills[(int)DFCareer.Skills.BluntWeapon] = 25;
                s.skills[(int)DFCareer.Skills.Destruction] = 35;
                s.skills[(int)DFCareer.Skills.Restoration] = 30;
                s.skills[(int)DFCareer.Skills.Alteration] = 25;
                s.skills[(int)DFCareer.Skills.Thaumaturgy] = 20;
                s.skills[(int)DFCareer.Skills.Dodging] = 20;
                s.spellsKnown[1] = 1;
                s.spellsKnown[IdleData.SpellHealIndex] = 1;
                s.ruleIf = new int[] { IdleData.IfHealth30, IdleData.IfHealth50, IdleData.IfHealth15, IdleData.IfAlways, IdleData.IfCannotHurt, IdleData.IfOff };
                s.ruleDo = new int[] { IdleData.DoPotion, IdleData.DoHeal, IdleData.DoFlee, IdleData.DoCast, IdleData.DoFlee, IdleData.DoAttack };
                Equip(w, ItemBuilder.CreateWeapon(Weapons.Staff, WeaponMaterialTypes.Iron));
                Equip(w, ItemBuilder.CreateArmor(g, r, Armor.Cuirass, ArmorMaterialTypes.Leather));
                Equip(w, ItemBuilder.CreateArmor(g, r, Armor.Greaves, ArmorMaterialTypes.Leather));
                Equip(w, ItemBuilder.CreateArmor(g, r, Armor.Boots, ArmorMaterialTypes.Leather));
                s.gold = 150;
            }
            else if (s.heroClass == IdleData.ClassRogue)
            {
                s.stats = new int[] { 45, 45, 45, 65, 50, 45, 60, 55 };
                s.skills[(int)DFCareer.Skills.LongBlade] = 15;
                s.skills[(int)DFCareer.Skills.ShortBlade] = 35;
                s.skills[(int)DFCareer.Skills.CriticalStrike] = 30;
                s.skills[(int)DFCareer.Skills.Dodging] = 30;
                s.poisonsKnown = 1;
                Equip(w, ItemBuilder.CreateWeapon(Weapons.Shortsword, WeaponMaterialTypes.Iron));
                Equip(w, ItemBuilder.CreateArmor(g, r, Armor.Cuirass, ArmorMaterialTypes.Leather));
                Equip(w, ItemBuilder.CreateArmor(g, r, Armor.Greaves, ArmorMaterialTypes.Leather));
                Equip(w, ItemBuilder.CreateArmor(g, r, Armor.Boots, ArmorMaterialTypes.Leather));
                Equip(w, ItemBuilder.CreateArmor(g, r, Armor.Helm, ArmorMaterialTypes.Leather));
                Equip(w, ItemBuilder.CreateArmor(g, r, Armor.Gauntlets, ArmorMaterialTypes.Leather));
                s.gold = 120;
            }
            else
            {
                Equip(w, ItemBuilder.CreateWeapon(Weapons.Broadsword, WeaponMaterialTypes.Iron));
                Equip(w, ItemBuilder.CreateArmor(g, r, Armor.Cuirass, ArmorMaterialTypes.Leather));
                Equip(w, ItemBuilder.CreateArmor(g, r, Armor.Greaves, ArmorMaterialTypes.Leather));
                Equip(w, ItemBuilder.CreateArmor(g, r, Armor.Boots, ArmorMaterialTypes.Leather));
                Equip(w, ItemBuilder.CreateArmor(g, r, Armor.Helm, ArmorMaterialTypes.Leather));
                Equip(w, ItemBuilder.CreateArmor(g, r, Armor.Gauntlets, ArmorMaterialTypes.Leather));
                Equip(w, ItemBuilder.CreateArmor(g, r, Armor.Left_Pauldron, ArmorMaterialTypes.Leather));
                Equip(w, ItemBuilder.CreateArmor(g, r, Armor.Right_Pauldron, ArmorMaterialTypes.Leather));
                Equip(w, ItemBuilder.CreateArmor(g, r, Armor.Buckler, ArmorMaterialTypes.Leather));
            }
            s.maxHealth = 25 + IdleData.HitPointsPerLevel;
            s.health = s.maxHealth;
            Arrays(s);
            Restock(w);
            RefreshBonus(w);
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

        // Items are written one by one. The game's own item bag is keyed by item ID, and IDs start again every time
        // the game is launched, so two of the hero's items can share one and the bag would silently drop one of them.
        static string Pack(List<DaggerfallUnityItem> items)
        {
            ItemData_v1[] data = new ItemData_v1[items.Count];
            for (int i = 0; i < items.Count; i++)
                data[i] = items[i].GetSaveData();
            return SaveLoadManager.Serialize(typeof(ItemData_v1[]), data, false);
        }

        static List<DaggerfallUnityItem> Unpack(string blob)
        {
            List<DaggerfallUnityItem> items = new List<DaggerfallUnityItem>();
            if (string.IsNullOrEmpty(blob))
                return items;
            ItemData_v1[] data = (ItemData_v1[])SaveLoadManager.Deserialize(typeof(ItemData_v1[]), blob);
            for (int i = 0; data != null && i < data.Length; i++)
                items.Add(new DaggerfallUnityItem(data[i]));
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
            w.s.stashBlob = Pack(w.stash);
            w.s.shopBlob = Pack(w.shop);
            w.s.gearAffixes = PackAffixes(worn);
            w.s.haulAffixes = PackAffixes(w.haul);
            w.s.stashAffixes = PackAffixes(w.stash);
            w.s.shopAffixes = PackAffixes(w.shop);

            // Forget affixes of items that are gone (sold, dropped, left behind).
            Dictionary<DaggerfallUnityItem, int[]> keep = new Dictionary<DaggerfallUnityItem, int[]>();
            List<DaggerfallUnityItem> all = new List<DaggerfallUnityItem>(worn);
            all.AddRange(w.haul);
            all.AddRange(w.stash);
            all.AddRange(w.shop);
            for (int i = 0; i < all.Count; i++)
                if (AffixesOf(all[i]) != null)
                    keep[all[i]] = AffixesOf(all[i]);
            affixes.Clear();
            foreach (KeyValuePair<DaggerfallUnityItem, int[]> pair in keep)
                affixes[pair.Key] = pair.Value;
        }

        public static IdleWorld FromSave(IdleSave s)
        {
            IdleWorld w = new IdleWorld();
            w.s = s;
            List<DaggerfallUnityItem> worn = Unpack(s.gearBlob);
            UnpackAffixes(worn, s.gearAffixes);
            for (int i = 0; i < worn.Count; i++)
                Equip(w, worn[i]);
            w.haul = Unpack(s.haulBlob);
            w.stash = Unpack(s.stashBlob);
            w.shop = Unpack(s.shopBlob);
            UnpackAffixes(w.haul, s.haulAffixes);
            UnpackAffixes(w.stash, s.stashAffixes);
            UnpackAffixes(w.shop, s.shopAffixes);
            Arrays(s);
            RefreshBonus(w);
            return w;
        }

        // Saves from before destinations existed have no per-dungeon lists.
        static void Arrays(IdleSave s)
        {
            if (s.clearedFloors == null || s.clearedFloors.Length != IdleData.DungeonTypes)
                s.clearedFloors = new int[IdleData.DungeonTypes];
            if (s.ingots == null || s.ingots.Length != IdleData.Metals)
                s.ingots = new int[IdleData.Metals];
            if (s.spellRanks == null || s.spellRanks.Length != IdleData.SpellKinds)
                s.spellRanks = new int[IdleData.SpellKinds];
            if (s.souls == null || s.souls.Length != IdleData.SoulTiers)
                s.souls = new int[IdleData.SoulTiers];
            if (s.gems == null || s.gems.Length != IdleData.GemCodes)
                s.gems = new int[IdleData.GemCodes];
            if (s.ore == null || s.ore.Length != IdleData.Metals)
                s.ore = new int[IdleData.Metals];
            if (s.smeltQueue == null || s.smeltQueue.Length != IdleData.Metals)
                s.smeltQueue = new int[IdleData.Metals];
            if (s.targets == null || s.targets.Length != IdleData.DungeonTypes)
                s.targets = new int[IdleData.DungeonTypes];
            if (s.dungeonType < 0 || s.dungeonType >= IdleData.DungeonTypes)
                s.dungeonType = IdleData.DefaultDungeonType;
            if (s.travelSeconds <= 0)
                s.travelSeconds = IdleData.TravelSeconds;
            if (s.startFloor < 1)
                s.startFloor = 1;
            if (s.ruleIf == null || s.ruleIf.Length != IdleData.RuleSlots || s.ruleDo == null || s.ruleDo.Length != IdleData.RuleSlots
                || (Same(s.ruleIf, IdleData.OldDefaultIf) && Same(s.ruleDo, IdleData.OldDefaultDo)))
            {
                s.ruleIf = (int[])IdleData.DefaultIf.Clone();
                s.ruleDo = (int[])IdleData.DefaultDo.Clone();
            }
            if (s.spellsKnown == null || s.spellsKnown.Length != IdleData.SpellKinds)
            {
                int[] known = new int[IdleData.SpellKinds];
                for (int i = 0; s.spellsKnown != null && i < s.spellsKnown.Length && i < known.Length; i++)
                    known[i] = s.spellsKnown[i];
                s.spellsKnown = known;
            }
            s.spellsKnown[IdleData.SpellShock] = 1;     // every hero starts with Shock
            if (s.stats != null && s.stats.Length > IdleData.StatIntelligence && (s.magicka < 0 || s.magicka > MaxMagicka(s)))
                s.magicka = MaxMagicka(s);
        }

        static bool Same(int[] a, int[] b)
        {
            if (a == null || b == null || a.Length != b.Length)
                return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i])
                    return false;
            return true;
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

        public static MobileTypes[] Table(int dungeonType)
        {
            // The first 19 tables are the dungeon types, in order.
            RandomEncounterTable[] tables = RandomEncounters.EncounterTables;
            return tables[(dungeonType >= 0 && dungeonType < IdleData.DungeonTypes) ? dungeonType : 0].Enemies;
        }

        // The enemy's name as shown: "Swift Orc", "Orc Warlord, boss".
        public static string Foe(IdleSave s, EnemyInfo e)
        {
            string name = e.name;
            for (int i = 0; i < IdleData.TraitKinds; i++)
                if ((s.enemyTraits & (1 << i)) != 0)
                    name = IdleData.TraitNames[i] + " " + name;
            return name;
        }

        public static bool IsChampion(IdleSave s)
        {
            return s.enemyTraits != 0;
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

        public static bool Works(DaggerfallUnityItem item)
        {
            return item != null && item.currentCondition > 0;
        }

        public static bool IsBow(DaggerfallUnityItem item)
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
                value -= ArmorValue(piece, false) * 5;
            DaggerfallUnityItem shield = w.gear[IdleData.SlotShield];
            if (Works(shield))
            {
                BodyParts[] covered = shield.GetShieldProtectedBodyParts();
                for (int i = 0; i < covered.Length; i++)
                    if ((int)covered[i] == part)
                        value -= ArmorValue(shield, true) * 5;
            }
            return value;
        }

        public static int StrengthBonus(IdleSave s)
        {
            return (int)Mathf.Floor((s.stats[IdleData.StatStrength] - 50) / 5f);
        }

        public static float Capacity(IdleSave s)
        {
            return s.stats[IdleData.StatStrength] * 1.5f + s.packLevel * IdleData.PackStepKg;
        }

        public static float CarriedWeight(IdleWorld w)
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
            float damage = (item.GetBaseDamageMin() + item.GetBaseDamageMax()) / 2f * (100 + IdleData.LevelDamagePercent * Level(item)) / 100f + StrengthBonus(s) + mod + AffixValue(item, IdleData.AffixCruel);
            float chance = Mathf.Clamp(s.skills[skill] + mod * 10 + 20 + AffixValue(item, IdleData.AffixKeen), 3, 97);
            float speed = 100f / Mathf.Max(50f, 100f - AffixValue(item, IdleData.AffixSwift));
            return Mathf.Max(0f, damage) * chance * speed + AffixValue(item, IdleData.AffixLeech) * 20f + item.NativeMaterialValue * 0.01f;
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

        public static string Destination(IdleSave s)
        {
            string kind = IdleData.DungeonNames[Mathf.Clamp(s.dungeonType, 0, IdleData.DungeonTypes - 1)];
            return string.IsNullOrEmpty(s.destName) ? "the " + kind.ToLower() : s.destName;
        }

        public static void StartExpedition(IdleWorld w)
        {
            IdleSave s = w.s;
            if (s.phase != IdleData.PhaseTown)
                return;
            if (s.nextType >= 0 && s.nextType < IdleData.DungeonTypes)
            {
                s.targets[s.dungeonType] = s.targetFloor;
                s.dungeonType = s.nextType;
                s.destName = s.nextName;
                s.travelSeconds = Mathf.Max(5, s.nextTravel);
                s.targetFloor = Mathf.Max(1, s.targets[s.dungeonType] > 0 ? s.targets[s.dungeonType] : 2);
            }
            s.nextType = -1;
            s.startFloor = 1;
            if (s.skipCleared != 0)
                s.startFloor = Mathf.Clamp(s.targetFloor - (IdleData.RunFloors - 1), 1, s.clearedFloors[s.dungeonType] + 1);
            s.phase = IdleData.PhaseOut;
            s.phaseLeft = s.travelSeconds * IdleData.TicksPerSecond;
            s.phaseTotal = s.phaseLeft;
            s.dungeonSeed = Next(s);
            s.floor = 0;
            s.floorTicks = 0;
            s.recall = 0;
            s.carried = 0;
            s.runKills = 0;
            s.runGold = 0;
            s.runItems = 0;
            s.vein = 0;
            s.miningMetal = 0;
            Coat(w);
            if (w.record)
                w.Emit(IdleData.EvInfo, s.heroName + " sets out for " + Destination(s) + ", floors " + s.startFloor + " to " + s.targetFloor);
        }

        public static void Advance(IdleWorld w, long ticks)
        {
            IdleSave s = w.s;
            while (ticks > 0)
            {
                if (s.phase == IdleData.PhaseTown)
                {
                    if (s.autoRepeat == 0 || (w.stopAfter > 0 && s.expeditions >= w.stopAfter))
                    {
                        Smelt(w, ticks);
                        return;
                    }
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
                    Smelt(w, step);
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
                    Smelt(w, step);
                    if (s.phase == IdleData.PhaseExplore)
                    {
                        Rest(s, step);
                        if (s.miningMetal == 0)
                            s.floorTicks += step;       // the hero stands still while mining
                    }
                    if (s.phaseLeft <= 0)
                        PhaseEnds(w);
                }
            }
        }

        // Between rooms the hero patches themselves up: Medical decides how fast.
        static void Rest(IdleSave s, int ticks)
        {
            // Magicka comes back while walking: 1.5% a second, more with Willpower.
            int most = MaxMagicka(s);
            if (s.magicka < most)
            {
                s.magickaCarry += ticks * most * (15 + s.stats[IdleData.StatWillpower] / 4);
                int perUnit = 1000 * IdleData.TicksPerSecond;
                int gain = s.magickaCarry / perUnit;
                s.magickaCarry -= gain * perUnit;
                s.magicka = Mathf.Min(most, s.magicka + gain);
                if (s.magicka >= most)
                    s.magickaCarry = 0;         // leftovers past full would depend on how time was cut into steps
            }
            if (s.health >= s.maxHealth)
                return;
            int perSecondPerMille = (10 + s.skills[(int)DFCareer.Skills.Medical] / 2) * (100 + s.bonus[IdleData.AffixMending]) / 100;
            s.healCarry += ticks * s.maxHealth * perSecondPerMille;
            int unit = 1000 * IdleData.TicksPerSecond;
            int heal = s.healCarry / unit;
            s.healCarry -= heal * unit;
            s.health = Mathf.Min(s.maxHealth, s.health + heal);
            if (s.health >= s.maxHealth)
                s.healCarry = 0;
        }

        static void Explore(IdleSave s)
        {
            s.phase = IdleData.PhaseExplore;
            s.phaseLeft = Range(s, IdleData.ExploreMinSeconds, IdleData.ExploreMaxSeconds) * IdleData.TicksPerSecond;
            s.phaseTotal = s.phaseLeft;
        }

        static void PhaseEnds(IdleWorld w)
        {
            IdleSave s = w.s;
            if (s.phase == IdleData.PhaseOut)
            {
                s.floor = Mathf.Max(1, s.startFloor);
                s.floorTicks = 0;
                s.roomsLeft = IdleData.RoomsOnFloor(s.floor);
                Explore(s);
                RollVein(s);
                if (w.record)
                    w.Emit(IdleData.EvFloor, (s.floor > 1) ? "Entered the dungeon and went straight down to floor " + s.floor : "Entered the dungeon");
            }
            else if (s.phase == IdleData.PhaseExplore)
            {
                Tally(w, (int)DFCareer.Skills.Medical);
                if (s.miningMetal > 0)
                    FinishMining(w);
                else if (s.recall == 0 && s.roomsLeft <= 0 && s.vein > 0 && StartMining(w))
                    return;
                if (s.recall != 0)
                {
                    s.retreats++;
                    GoHome(w, "Called back from floor " + s.floor);
                }
                else if (s.roomsLeft > 0)
                {
                    StartFight(w);
                }
                else if (s.floor >= s.targetFloor)
                {
                    s.clearedFloors[s.dungeonType] = Mathf.Max(s.clearedFloors[s.dungeonType], s.floor);
                    s.clears++;
                    if (s.autoDeeper != 0)
                        s.targetFloor++;
                    GoHome(w, "Floor " + s.floor + " cleared, heading home");
                }
                else
                {
                    s.clearedFloors[s.dungeonType] = Mathf.Max(s.clearedFloors[s.dungeonType], s.floor);
                    s.floor++;
                    s.floorTicks = 0;
                    s.roomsLeft = IdleData.RoomsOnFloor(s.floor);
                    Explore(s);
                    RollVein(s);
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
            s.phaseLeft = s.travelSeconds * IdleData.TicksPerSecond;
            s.phaseTotal = s.phaseLeft;
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
            // The last fight on every fifth floor is a boss from two steps further down, with two traits.
            bool boss = s.roomsLeft == 1 && s.floor % IdleData.BossEvery == 0;
            int index = boss ? s.floor + 2 : Mathf.Max(0, s.floor - 2 + Range(s, 0, 2));
            s.enemyTraits = 0;
            if (boss)
            {
                int first = Range(s, 0, IdleData.TraitKinds - 1);
                int second = (first + Range(s, 1, IdleData.TraitKinds - 1)) % IdleData.TraitKinds;
                s.enemyTraits = IdleData.TraitBoss | (1 << first) | (1 << second);
            }
            else if (Roll(s, Mathf.Min(25, 4 + s.floor)))
            {
                s.enemyTraits = 1 << Range(s, 0, IdleData.TraitKinds - 1);
            }
            s.guarding = 0;
            s.heroShield = 0;
            s.fightStatus = 0;
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
            if ((s.enemyTraits & IdleData.TraitBoss) != 0)
                health *= 3;
            else if (s.enemyTraits != 0)
                health = health * 8 / 5;
            s.enemyMaxHealth = Mathf.Max(1, health);
            s.enemyHealth = s.enemyMaxHealth;
            s.enemyCasts = (EnemySpells(e, EnemyLevel(s, e)).Length > 0) ? 1 + EnemyLevel(s, e) / 6 + (((s.enemyTraits & IdleData.TraitBoss) != 0) ? 2 : 0) : 0;

            int enemySpeed = (e.career != null) ? e.career.Speed : 50;
            RefreshBonus(w);
            s.heroTimer = Range(s, 3, HeroInterval(s));
            s.enemyTimer = Range(s, 3, EnemyInterval(s, e));
            s.phase = IdleData.PhaseFight;
            if (w.record)
                w.Emit(IdleData.EvFight, (((s.enemyTraits & IdleData.TraitBoss) != 0) ? "Boss: the " : IsChampion(s) ? "Champion: a " : "A ")
                    + Foe(s, e) + " attacks (" + s.enemyHealth + " health)");
        }

        static int EnemyInterval(IdleSave s, EnemyInfo e)
        {
            int interval = IdleData.AttackInterval((e.career != null) ? e.career.Speed : 50);
            if ((s.enemyTraits & IdleData.TraitSwift) != 0)
                interval = Mathf.Max(5, interval * 6 / 10);
            return interval;
        }

        public static bool CanHurt(IdleWorld w, EnemyInfo e)
        {
            DaggerfallUnityItem weapon = w.gear[IdleData.SlotWeapon];
            return weapon != null && Works(weapon) && !IsBow(weapon) && (int)e.def.MinMetalToHit <= weapon.NativeMaterialValue
                || (int)e.def.MinMetalToHit == 0;
        }

        static bool Applies(IdleWorld w, EnemyInfo e, int condition)
        {
            IdleSave s = w.s;
            switch (condition)
            {
                case IdleData.IfAlways: return true;
                case IdleData.IfHealth50: return s.health * 100 < s.maxHealth * 50;
                case IdleData.IfHealth30: return s.health * 100 < s.maxHealth * 30;
                case IdleData.IfHealth15: return s.health * 100 < s.maxHealth * 15;
                case IdleData.IfCannotHurt: return !CanHurt(w, e);
                case IdleData.IfChampion: return IsChampion(s);
                case IdleData.IfEnemyLow: return s.enemyHealth * 100 < s.enemyMaxHealth * 30;
                case IdleData.IfCaster: return EnemySpells(e, EnemyLevel(s, e)).Length > 0;
                case IdleData.IfMagicka: return s.magicka * 2 > MaxMagicka(s);
            }
            return false;
        }

        // The first order whose condition holds and whose action can be done. Attack when none does.
        static int Choose(IdleWorld w, EnemyInfo e)
        {
            IdleSave s = w.s;
            for (int i = 0; i < IdleData.RuleSlots; i++)
            {
                if (!Applies(w, e, s.ruleIf[i]))
                    continue;
                int action = s.ruleDo[i];
                if (action == IdleData.DoPotion && (s.potions <= 0 || s.health >= s.maxHealth))
                    continue;
                if (action >= IdleData.DoCast && SpellFor(w, e, action) < 0)
                    continue;
                return action;
            }
            return IdleData.DoAttack;
        }

        // ------------------------------------------------------------------ magic

        // Enemy spells: the game's spell IDs from its enemy spell lists, reduced to what they do here.
        const int EDamage = 0, EDrain = 1, EParalyse = 2, ESilence = 3, EAbsorb = 4, EHidden = 5, EWard = 6;
        static readonly int[] enemySpellIds = { 0x06, 0x07, 0x08, 0x0E, 0x10, 0x14, 0x16, 0x17, 0x19, 0x1C, 0x1D, 0x1F, 0x22, 0x2C, 0x32, 0x33, 0x34, 0x43 };
        static readonly string[] enemySpellNames = { "Invisibility", "Wizard's Fire", "Shock", "Fireball", "Ice Bolt", "Ice Storm", "Spell Shield",
            "Silence", "Fire Storm", "Far Silence", "Toxic Cloud", "Lightning", "Wizard Rend", "Chameleon", "Paralysis", "Sleep", "Vampiric Touch", "Energy Leech" };
        static readonly int[] enemySpellKinds = { EHidden, EDamage, EDamage, EDamage, EDamage, EDamage, EWard, ESilence, EDamage, ESilence, EDamage,
            EDamage, EDrain, EHidden, EParalyse, EParalyse, EAbsorb, EAbsorb };
        static readonly int[] enemySpellElements = { -1, 0, 3, 0, 1, 1, -1, -1, 0, -1, 2, 3, 4, -1, -1, -1, 4, 4 };
        // low, high, per-level low, per-level high
        static readonly int[] enemySpellMagnitudes = { 0,0,0,0, 1,15,1,4, 1,20,1,4, 1,1,1,7, 1,35,1,5, 1,30,1,5, 0,0,0,0, 0,0,0,0, 1,20,1,5, 0,0,0,0,
            1,25,1,5, 1,25,1,5, 1,14,1,1, 0,0,0,0, 0,0,0,0, 0,0,0,0, 1,15,1,5, 1,10,1,3 };

        static readonly int[] impSpells = { 0x07, 0x1D, 0x2C };
        static readonly int[] ghostSpells = { 0x22 };
        static readonly int[] orcShamanSpells = { 0x06, 0x07, 0x16, 0x19, 0x1F };
        static readonly int[] wraithSpells = { 0x1C, 0x1F };
        static readonly int[] frostDaedraSpells = { 0x10, 0x14 };
        static readonly int[] fireDaedraSpells = { 0x0E, 0x19 };
        static readonly int[] daedrothSpells = { 0x16, 0x17, 0x1F };
        static readonly int[] vampireSpells = { 0x33 };
        static readonly int[] seducerSpells = { 0x34, 0x43 };
        static readonly int[] vampireAncientSpells = { 0x08, 0x32 };
        static readonly int[] daedraLordSpells = { 0x08, 0x0E, 0x43 };
        static readonly int[] lichSpells = { 0x08, 0x0E, 0x22 };
        static readonly int[] ancientLichSpells = { 0x08, 0x0E, 0x1D, 0x1F, 0x22 };
        static readonly int[] noSpells = new int[0];
        static readonly int[][] classSpells = { frostDaedraSpells, daedrothSpells, orcShamanSpells, vampireAncientSpells, daedraLordSpells, lichSpells, ancientLichSpells };

        // The game's own spell list for this enemy (EnemyEntity), without the ones that do nothing here
        // (Free Action, Balyna's Antidote).
        public static int[] EnemySpells(EnemyInfo e, int level)
        {
            if (e.isClass)
                return e.def.CastsMagic ? classSpells[Mathf.Clamp(level / 3, 0, classSpells.Length - 1)] : noSpells;
            switch ((MonsterCareers)e.id)
            {
                case MonsterCareers.Imp: return impSpells;
                case MonsterCareers.Ghost: return ghostSpells;
                case MonsterCareers.OrcShaman: return orcShamanSpells;
                case MonsterCareers.Wraith: return wraithSpells;
                case MonsterCareers.FrostDaedra: return frostDaedraSpells;
                case MonsterCareers.FireDaedra: return fireDaedraSpells;
                case MonsterCareers.Daedroth: return daedrothSpells;
                case MonsterCareers.Vampire: return vampireSpells;
                case MonsterCareers.DaedraSeducer: return seducerSpells;
                case MonsterCareers.VampireAncient: return vampireAncientSpells;
                case MonsterCareers.DaedraLord: return daedraLordSpells;
                case MonsterCareers.Lich: return lichSpells;
                case MonsterCareers.AncientLich: return ancientLichSpells;
            }
            return noSpells;
        }

        public static int MaxMagicka(IdleSave s)
        {
            int most = IdleData.MaxMagicka(s.stats[IdleData.StatIntelligence], s.level);
            if (s.heroClass == IdleData.ClassMage)
                most = most * 3 / 2;
            return most + s.bonus[IdleData.AffixArcane];
        }

        public static int Cost(IdleSave s, int spell)
        {
            int cost = IdleData.SpellCost(spell, s.level, s.skills[IdleData.SpellSchool[spell]]) * (100 - s.bonus[IdleData.AffixSorcery]) / 100;
            return Mathf.Max(1, cost * (100 - 4 * Rank(s, spell)) / 100);
        }

        // Percent of a spell's damage an enemy takes, from its career's tolerances.
        public static int Tolerance(EnemyInfo e, int element)
        {
            if (e.career == null || element < 0)
                return 100;
            DFCareer.Tolerance t;
            switch (element)
            {
                case IdleData.ElementFire: t = e.career.Fire; break;
                case IdleData.ElementCold: t = e.career.Frost; break;
                case IdleData.ElementPoison: t = e.career.Poison; break;
                case IdleData.ElementShock: t = e.career.Shock; break;
                default: t = e.career.Magic; break;
            }
            switch (t)
            {
                case DFCareer.Tolerance.Immune: return 0;
                case DFCareer.Tolerance.Resistant: return 50;
                case DFCareer.Tolerance.LowTolerance: return 150;
                case DFCareer.Tolerance.CriticalWeakness: return 200;
            }
            return 100;
        }

        static int Magnitude(IdleSave s, int low, int high, int levelLow, int levelHigh, int level)
        {
            return Range(s, low, high) + Range(s, levelLow * level, levelHigh * level);
        }

        // A spell's rank from 0 (I) to 9 (X), and its strength in percent.
        public static int Rank(IdleSave s, int spell)
        {
            return (s.spellRanks != null && spell >= 0 && spell < s.spellRanks.Length) ? s.spellRanks[spell] : 0;
        }

        public static int RankPower(IdleSave s, int spell)
        {
            return 100 + 15 * Rank(s, spell);
        }

        public static string CannotRankUp(IdleSave s, int spell)
        {
            if (s.spellsKnown[spell] == 0)
                return "Learn the spell first.";
            if (Rank(s, spell) + 1 >= IdleData.RankTop)
                return IdleData.SpellNames[spell] + " is at rank X, the highest.";
            if (IdleData.RankGold(spell, Rank(s, spell) + 1) > s.gold)
                return "Not enough gold: rank " + Roman(Rank(s, spell) + 2) + " costs " + IdleData.RankGold(spell, Rank(s, spell) + 1) + ".";
            return null;
        }

        public static bool RankUp(IdleSave s, int spell)
        {
            if (CannotRankUp(s, spell) != null)
                return false;
            int cost = IdleData.RankGold(spell, Rank(s, spell) + 1);
            s.gold -= cost;
            s.spellRankGold += cost;
            s.spellRanks[spell]++;
            return true;
        }

        public static string Roman(int n)
        {
            string[] r = { "", "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X" };
            return r[Mathf.Clamp(n, 0, 10)];
        }

        // At home (under auto-smith): the cheapest rank for a known spell the orders use, while it costs at most a
        // quarter of the gold. Mages put more in: up to half.
        static int AutoRank(IdleWorld w)
        {
            IdleSave s = w.s;
            if (s.noAutoSmith)
                return 0;
            long before = s.spellRankGold;
            for (int guard = 0; guard < 40; guard++)
            {
                int best = -1, bestCost = int.MaxValue;
                for (int k = 0; k < IdleData.SpellKinds; k++)
                    if (Used(s, k) && CannotRankUp(s, k) == null && IdleData.RankGold(k, Rank(s, k) + 1) < bestCost)
                    {
                        best = k;
                        bestCost = IdleData.RankGold(k, Rank(s, k) + 1);
                    }
                long share = (s.heroClass == IdleData.ClassMage) ? 2 : 4;
                if (best < 0 || bestCost * share > s.gold || !RankUp(s, best))
                    break;
            }
            return (int)(s.spellRankGold - before);
        }

        // Whether the orders can cast a spell: damage spells through "cast a damage spell", the others by name.
        static bool Used(IdleSave s, int spell)
        {
            if (s.spellsKnown[spell] == 0)
                return false;
            int want = (IdleData.SpellKind[spell] == IdleData.SpellDamage) ? IdleData.DoCast
                : (spell == IdleData.SpellHealIndex) ? IdleData.DoHeal : (spell == IdleData.SpellShieldIndex) ? IdleData.DoShield
                : (spell == IdleData.SpellParalysisIndex) ? IdleData.DoParalyse : IdleData.DoWard;
            if (spell == IdleData.SpellHealIndex)
                return true;            // Heal is also cast between fights
            for (int i = 0; i < s.ruleDo.Length; i++)
                if (s.ruleDo[i] == want && s.ruleIf[i] != IdleData.IfOff)
                    return true;
            return false;
        }

        // ------------------------------------------------------------------ poisons

        static bool ShrugsPoison(int id)
        {
            return id == (int)MobileTypes.SkeletalWarrior || id == (int)MobileTypes.Zombie || id == (int)MobileTypes.Ghost
                || id == (int)MobileTypes.Mummy || id == (int)MobileTypes.Wraith || id == (int)MobileTypes.Vampire
                || id == (int)MobileTypes.VampireAncient || id == (int)MobileTypes.Lich || id == (int)MobileTypes.AncientLich
                || id == (int)MobileTypes.Gargoyle || id == (int)MobileTypes.FireAtronach || id == (int)MobileTypes.IronAtronach
                || id == (int)MobileTypes.FleshAtronach || id == (int)MobileTypes.IceAtronach || IsDaedra(id);
        }

        public static int PoisonChance(IdleSave s)
        {
            return (s.heroClass == IdleData.ClassRogue) ? IdleData.RoguePoisonChance : IdleData.PoisonChance;
        }

        public static int PoisonHit(IdleSave s, int tier)
        {
            return IdleData.PoisonDamage[tier] * (100 + 5 * s.floor) / 100;
        }

        public static string CannotLearnPoison(IdleSave s, int tier)
        {
            if (tier < s.poisonsKnown)
                return IdleData.PoisonNames[tier] + " is known already.";
            if (tier > s.poisonsKnown)
                return "Learn " + IdleData.PoisonNames[s.poisonsKnown] + " first.";
            if (IdleData.PoisonRecipe[tier] > s.gold)
                return "Not enough gold: the recipe costs " + IdleData.PoisonRecipe[tier] + ".";
            return null;
        }

        public static bool LearnPoison(IdleSave s, int tier)
        {
            if (CannotLearnPoison(s, tier) != null)
                return false;
            s.gold -= IdleData.PoisonRecipe[tier];
            s.alchemyGold += IdleData.PoisonRecipe[tier];
            s.poisonsKnown++;
            return true;
        }

        // Before a run: a vial of the best known poison the purse allows (keeping twice its price), else none.
        static void Coat(IdleWorld w)
        {
            IdleSave s = w.s;
            s.coated = 0;
            if (s.noPoison)
                return;
            for (int tier = s.poisonsKnown - 1; tier >= 0; tier--)
                if (s.gold >= IdleData.PoisonVial[tier] * 3)
                {
                    s.gold -= IdleData.PoisonVial[tier];
                    s.alchemyGold += IdleData.PoisonVial[tier];
                    s.coated = tier + 1;
                    s.vialsUsed++;
                    if (w.record)
                        w.Emit(IdleData.EvInfo, s.heroName + " coats the weapon with " + IdleData.PoisonNames[tier]);
                    return;
                }
        }

        // At home (under auto-smith): the next recipe once a run's worth of its vials is affordable many times over.
        static void AutoAlchemy(IdleWorld w)
        {
            IdleSave s = w.s;
            if (s.noAutoSmith || s.noPoison || s.poisonsKnown >= IdleData.PoisonKinds)
                return;
            if (s.poisonsKnown == 0 && s.heroClass != IdleData.ClassRogue)
                return;             // only rogues take up poison on their own
            int next = s.poisonsKnown;
            if ((long)IdleData.PoisonRecipe[next] * 2 <= s.gold && IdleData.PoisonVial[next] * 40 <= s.gold)
                LearnPoison(s, next);
        }

        // Which spell an order means right now, or -1 when it cannot be cast (unknown, no magicka, silenced, pointless).
        public static int SpellFor(IdleWorld w, EnemyInfo e, int action)
        {
            IdleSave s = w.s;
            if ((s.fightStatus & IdleData.StatusSilenced) != 0)
                return -1;
            int spell = -1;
            switch (action)
            {
                case IdleData.DoCast:
                    int best = 0;
                    for (int i = 0; i < IdleData.SpellKinds; i++)
                    {
                        if (IdleData.SpellKind[i] != IdleData.SpellDamage || s.spellsKnown[i] == 0 || s.magicka < Cost(s, i))
                            continue;
                        int worth = IdleData.SpellAverage(i, s.level) * Tolerance(e, IdleData.SpellElement[i]);
                        if (worth > best)
                        {
                            best = worth;
                            spell = i;
                        }
                    }
                    return spell;
                case IdleData.DoHeal:
                    spell = IdleData.SpellHealIndex;
                    if (s.health >= s.maxHealth)
                        return -1;
                    break;
                case IdleData.DoShield:
                    spell = IdleData.SpellShieldIndex;
                    if (s.heroShield > 0)
                        return -1;
                    break;
                case IdleData.DoParalyse:
                    spell = IdleData.SpellParalysisIndex;
                    if ((s.fightStatus & IdleData.StatusParalysed) != 0 || (e.career != null && e.career.Paralysis == DFCareer.Tolerance.Immune))
                        return -1;
                    break;
                case IdleData.DoWard:
                    spell = IdleData.SpellWardIndex;
                    if ((s.fightStatus & IdleData.StatusWard) != 0)
                        return -1;
                    break;
            }
            if (spell < 0 || s.spellsKnown[spell] == 0 || s.magicka < Cost(s, spell))
                return -1;
            return spell;
        }

        static void Cast(IdleWorld w, EnemyInfo e, int spell)
        {
            IdleSave s = w.s;
            if (IdleData.SpellKind[spell] == IdleData.SpellHeal)
            {
                CastHeal(w);
                return;
            }
            s.magicka -= Cost(s, spell);
            s.spellsCast++;
            Tally(w, IdleData.SpellSchool[spell]);
            string name = IdleData.SpellNames[spell];
            switch (IdleData.SpellKind[spell])
            {
                case IdleData.SpellDamage:
                {
                    int element = IdleData.SpellElement[spell];
                    int damage = RankPower(s, spell) * Magnitude(s, IdleData.SpellBaseLow[spell], IdleData.SpellBaseHigh[spell], IdleData.SpellLevelLow[spell],
                        IdleData.SpellLevelHigh[spell], s.level) / 100 * Tolerance(e, element) / 100;
                    if ((s.fightStatus & IdleData.StatusEnemyWard) != 0)
                        damage /= 2;
                    s.enemyHealth -= damage;
                    if (w.record)
                        w.EmitSpell(IdleData.EvCast, s.heroName + " casts " + name + " at the " + Foe(s, e) + " for " + damage
                            + ((Tolerance(e, element) < 100) ? " (it resists " + IdleData.ElementNames[element] + ")" : (Tolerance(e, element) > 100) ? " (weak to " + IdleData.ElementNames[element] + ")" : ""),
                            damage, element);
                    if (s.enemyHealth <= 0)
                        Kill(w, e);
                    break;
                }
                case IdleData.SpellShieldKind:
                    s.heroShield = RankPower(s, spell) * Magnitude(s, IdleData.SpellBaseLow[spell], IdleData.SpellBaseHigh[spell], IdleData.SpellLevelLow[spell],
                        IdleData.SpellLevelHigh[spell], s.level) / 100;
                    if (w.record)
                        w.EmitSpell(IdleData.EvCastSelf, s.heroName + " casts Shield (absorbs " + s.heroShield + ")", 0, IdleData.ElementMagic);
                    break;
                case IdleData.SpellWard:
                    s.fightStatus |= IdleData.StatusWard;
                    if (w.record)
                        w.EmitSpell(IdleData.EvCastSelf, s.heroName + " casts Spell Shield", 0, IdleData.ElementMagic);
                    break;
                case IdleData.SpellParalyse:
                {
                    s.fightStatus |= IdleData.StatusParalysed;
                    int chance = 55 + s.skills[IdleData.SpellSchool[spell]] / 4 - EnemyLevel(s, e) * 2;
                    if (e.career != null && e.career.Paralysis == DFCareer.Tolerance.Resistant)
                        chance /= 2;
                    if ((s.enemyTraits & IdleData.TraitBoss) != 0)
                        chance /= 2;
                    bool held = Roll(s, Mathf.Clamp(chance, 5, 90));
                    if (held)
                        s.enemyTimer += 2 * EnemyInterval(s, e);
                    if (w.record)
                        w.EmitSpell(IdleData.EvCast, s.heroName + " casts Paralysis: the " + Foe(s, e) + (held ? " is held fast" : " shakes it off"), 0, IdleData.ElementMagic);
                    break;
                }
            }
        }

        static void CastHeal(IdleWorld w)
        {
            IdleSave s = w.s;
            int spell = IdleData.SpellHealIndex;
            s.magicka -= Cost(s, spell);
            s.spellsCast++;
            Tally(w, IdleData.SpellSchool[spell]);
            int heal = Mathf.Max(1, RankPower(s, spell) * Magnitude(s, IdleData.SpellBaseLow[spell], IdleData.SpellBaseHigh[spell], IdleData.SpellLevelLow[spell],
                IdleData.SpellLevelHigh[spell], s.level) / 100);
            s.health = Mathf.Min(s.maxHealth, s.health + heal);
            if (w.record)
                w.EmitSpell(IdleData.EvCastSelf, s.heroName + " casts Heal (" + s.health + "/" + s.maxHealth + ")", heal, IdleData.ElementMagic);
        }

        // The hero's Shield takes damage first.
        static int Absorb(IdleSave s, int damage)
        {
            int absorbed = Mathf.Min(s.heroShield, Mathf.Max(0, damage));
            s.heroShield -= absorbed;
            return absorbed;
        }

        // The enemy casts one of its spells instead of attacking. False when it has nothing useful to cast.
        static bool EnemyCasts(IdleWorld w, EnemyInfo e)
        {
            IdleSave s = w.s;
            int level = EnemyLevel(s, e);
            int[] list = EnemySpells(e, level);
            if (list.Length == 0)
                return false;
            int id = list[Range(s, 0, list.Length - 1)];
            int k = Array.IndexOf(enemySpellIds, id);
            if (k < 0)
                return false;
            int kind = enemySpellKinds[k];
            if ((kind == EHidden && (s.fightStatus & IdleData.StatusHidden) != 0) || (kind == EWard && (s.fightStatus & IdleData.StatusEnemyWard) != 0)
                || (kind == ESilence && (s.fightStatus & IdleData.StatusSilenced) != 0))
                return false;
            s.enemyCasts--;
            s.enemySpellsCast++;
            string who = "The " + Foe(s, e);
            string name = enemySpellNames[k];
            int element = enemySpellElements[k];
            if (kind == EHidden || kind == EWard)
            {
                s.fightStatus |= (kind == EHidden) ? IdleData.StatusHidden : IdleData.StatusEnemyWard;
                if (w.record)
                    w.EmitSpell(IdleData.EvEnemyCast, who + " casts " + name + ((kind == EHidden) ? ": harder to hit" : ": the hero's spells do half"), 0, IdleData.ElementMagic);
                return true;
            }

            // The hero's saving throw: Willpower and Luck, much better under Spell Shield. Effects that are not
            // damage are resisted twice as often.
            bool ward = (s.fightStatus & IdleData.StatusWard) != 0;
            int save = 10 + (s.stats[IdleData.StatWillpower] - 50) / 2 + s.stats[IdleData.StatLuck] / 10 + (ward ? 40 : 0) + s.bonus[IdleData.AffixWarding];
            if (kind != EDamage && kind != EAbsorb)
                save *= 2;
            if (Roll(s, Mathf.Clamp(save, 3, 90)))
            {
                if (w.record)
                    w.EmitSpell(IdleData.EvEnemyCast, who + " casts " + name + ", resisted", 0, (element >= 0) ? element : IdleData.ElementMagic);
                return true;
            }
            if (kind == EParalyse)
            {
                s.heroTimer += 2 * IdleData.AttackInterval(s.stats[IdleData.StatSpeed]);
                if (w.record)
                    w.EmitSpell(IdleData.EvEnemyCast, who + " casts " + name + ": " + s.heroName + " cannot move", 0, IdleData.ElementMagic);
                return true;
            }
            if (kind == ESilence)
            {
                s.fightStatus |= IdleData.StatusSilenced;
                if (w.record)
                    w.EmitSpell(IdleData.EvEnemyCast, who + " casts " + name + ": " + s.heroName + " cannot cast", 0, IdleData.ElementMagic);
                return true;
            }
            int m = k * 4;
            int amount = Magnitude(s, enemySpellMagnitudes[m], enemySpellMagnitudes[m + 1], enemySpellMagnitudes[m + 2], enemySpellMagnitudes[m + 3], level);
            if (ward)
                amount /= 2;
            if (kind == EDrain)
            {
                amount = Mathf.Min(amount, s.magicka);
                s.magicka -= amount;
                if (w.record)
                    w.EmitSpell(IdleData.EvEnemyCast, who + " casts " + name + ": " + amount + " magicka drained", 0, IdleData.ElementMagic);
                return true;
            }
            int absorbed = Absorb(s, amount);
            amount -= absorbed;
            s.health -= amount;
            if (kind == EAbsorb)
                s.enemyHealth = Mathf.Min(s.enemyMaxHealth, s.enemyHealth + amount);
            if (w.record)
                w.EmitSpell(IdleData.EvEnemyCast, who + " casts " + name + " for " + amount + ((absorbed > 0) ? " (Shield took " + absorbed + ")" : "")
                    + " (" + Mathf.Max(0, s.health) + "/" + s.maxHealth + ")", amount, element);
            if (s.health <= 0)
                Die(w, e);
            return true;
        }

        public static bool LearnSpell(IdleWorld w, int spell)
        {
            IdleSave s = w.s;
            if (spell < 0 || spell >= IdleData.SpellKinds || s.spellsKnown[spell] != 0 || s.gold < IdleData.SpellPrice[spell])
                return false;
            s.gold -= IdleData.SpellPrice[spell];
            s.spellsKnown[spell] = 1;
            return true;
        }

        // What a spell does at the hero's level, for the spellbook.
        public static string SpellText(IdleSave s, int spell)
        {
            int level = Mathf.Max(1, s.level);
            int low = (IdleData.SpellBaseLow[spell] + IdleData.SpellLevelLow[spell] * level) * RankPower(s, spell) / 100;
            int high = (IdleData.SpellBaseHigh[spell] + IdleData.SpellLevelHigh[spell] * level) * RankPower(s, spell) / 100;
            switch (IdleData.SpellKind[spell])
            {
                case IdleData.SpellDamage: return low + "-" + high + " " + IdleData.ElementNames[IdleData.SpellElement[spell]] + " damage";
                case IdleData.SpellHeal: return "heals " + low + "-" + high;
                case IdleData.SpellShieldKind: return "absorbs " + low + "-" + high + " damage for the rest of the fight";
                case IdleData.SpellParalyse: return "the enemy loses two attacks; once a fight, weaker on strong enemies and bosses";
                case IdleData.SpellWard: return "for the fight: enemy spells are resisted far more often and do half";
            }
            return "";
        }

        static void Drink(IdleWorld w)
        {
            IdleSave s = w.s;
            s.potions--;
            s.potionsDrunk++;
            int heal = s.maxHealth * 3 / 10 + 10;
            s.health = Mathf.Min(s.maxHealth, s.health + heal);
            if (w.record)
                w.Emit(IdleData.EvPotion, s.heroName + " drinks a healing potion (" + s.health + "/" + s.maxHealth + ", " + s.potions + " left)", heal);
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
            s.heroTimer = HeroInterval(s);

            s.guarding = 0;
            int action = (s.recall != 0) ? IdleData.DoFlee : Choose(w, e);
            if (action == IdleData.DoPotion)
            {
                Drink(w);
                return;
            }
            if (action >= IdleData.DoCast)
            {
                Cast(w, e, SpellFor(w, e, action));
                return;
            }
            if (action == IdleData.DoGuard)
            {
                s.guarding = 1;
                if (w.record)
                    w.Emit(IdleData.EvHeroMiss, s.heroName + " raises their guard");
                return;
            }

            // Running away takes the turn. A failed try costs the attack.
            if (action == IdleData.DoFlee)
            {
                int enemySpeed = (e.career != null) ? e.career.Speed : 50;
                int escape = Mathf.Clamp(50 + (s.stats[IdleData.StatSpeed] - enemySpeed) / 2 + s.stats[IdleData.StatLuck] / 10, 15, 90);
                if (Roll(s, escape))
                {
                    s.retreats++;
                    s.fled++;
                    if (s.recall == 0 && s.autoDeeper != 0 && s.targetFloor > 1)
                        s.targetFloor--;
                    GoHome(w, s.heroName + " flees from the " + Foe(s, e));
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
                chance += weapon.GetWeaponMaterialModifier() * 10 + s.bonus[IdleData.AffixKeen];

            int enemySkill = Mathf.Min(100, EnemyLevel(s, e) * 5 + 30);
            int enemyLuck = (e.career != null) ? e.career.Luck : 50;
            int enemyAgility = (e.career != null) ? e.career.Agility : 50;
            int enemyArmor = e.isClass ? Mathf.Max(20, 100 - EnemyLevel(s, e) * 3) : e.def.ArmorValue * 5;
            int critical = s.skills[(int)DFCareer.Skills.CriticalStrike];
            bool power = action == IdleData.DoPower;
            if (power)
                chance -= 20;
            if ((s.enemyTraits & IdleData.TraitArmoured) != 0)
                chance -= 25;
            if ((s.fightStatus & IdleData.StatusHidden) != 0)
                chance -= 30;

            Tally(w, skill);
            bool hit = Hits(s, chance, enemyArmor, s.stats[IdleData.StatLuck] - enemyLuck, s.stats[IdleData.StatAgility] - enemyAgility,
                enemySkill, critical, !e.isClass);
            if (!hit)
            {
                if (w.record)
                    w.Emit(IdleData.EvHeroMiss, s.heroName + (power ? " swings hard and misses" : " misses"));
                return;
            }
            Tally(w, (int)DFCareer.Skills.CriticalStrike);

            int damage;
            if (weapon != null)
            {
                damage = Reinforced(weapon, Range(s, weapon.GetBaseDamageMin(), weapon.GetBaseDamageMax()));
                if (s.enemyId == (int)MobileTypes.SkeletalWarrior)
                {
                    if ((weapon.flags & 0x10) == 0)
                        damage /= 2;
                    if (weapon.NativeMaterialValue == (int)WeaponMaterialTypes.Silver)
                        damage *= 2;
                }
                damage += StrengthBonus(s) + weapon.GetWeaponMaterialModifier() + s.bonus[IdleData.AffixCruel];
                Wear(s, weapon, damage);
            }
            else
            {
                int hand = s.skills[(int)DFCareer.Skills.HandToHand];
                damage = Range(s, hand / 10 + 1, hand / 5 + 1) + StrengthBonus(s);
            }
            if (power)
                damage = damage * 3 / 2;
            if (damage < 1)
                damage = 0;
            int poisoned = 0;
            if (s.coated > 0 && weapon != null && !ShrugsPoison(s.enemyId) && Roll(s, PoisonChance(s)))
            {
                poisoned = PoisonHit(s, s.coated - 1);
                damage += poisoned;
                s.poisonHits++;
            }

            s.enemyHealth -= damage;
            if (w.record)
                w.Emit(IdleData.EvHeroHit, s.heroName + (power ? " power-attacks the " : " hits the ") + Foe(s, e) + " for " + damage
                    + ((weapon == null && w.gear[IdleData.SlotWeapon] != null) ? " with bare hands" : "") + ((poisoned > 0) ? " (" + poisoned + " poison)" : ""), damage);
            if (s.enemyHealth <= 0)
                Kill(w, e);
        }

        static void EnemyAttacks(IdleWorld w)
        {
            IdleSave s = w.s;
            EnemyInfo e = Enemy(s.enemyId);
            s.enemyTimer = EnemyInterval(s, e);
            if ((s.enemyTraits & IdleData.TraitRegenerating) != 0 && s.enemyHealth < s.enemyMaxHealth)
                s.enemyHealth = Mathf.Min(s.enemyMaxHealth, s.enemyHealth + Mathf.Max(1, s.enemyMaxHealth / 25));

            if (s.enemyCasts > 0 && Roll(s, 35) && EnemyCasts(w, e))
            {
                s.guarding = 0;
                return;
            }

            int skill = Mathf.Min(100, EnemyLevel(s, e) * 5 + 30);
            if (s.guarding != 0)
                skill -= 40;
            s.guarding = 0;
            int part = bodyParts[Range(s, 0, bodyParts.Length - 1)];
            int armor = ArmorAt(w, part);
            int luckDiff = ((e.career != null) ? e.career.Luck : 50) - s.stats[IdleData.StatLuck];
            int agilityDiff = ((e.career != null) ? e.career.Agility : 50) - s.stats[IdleData.StatAgility];
            int dodging = s.skills[(int)DFCareer.Skills.Dodging] + s.bonus[IdleData.AffixNimble];
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
                    w.Emit(IdleData.EvEnemyMiss, "The " + Foe(s, e) + " misses");
                return;
            }
            if ((s.enemyTraits & IdleData.TraitVenomous) != 0)
                damage = damage * 7 / 5;
            if ((s.enemyTraits & IdleData.TraitEnraged) != 0 && s.enemyHealth * 2 < s.enemyMaxHealth)
                damage = damage * 8 / 5;
            if ((s.enemyTraits & IdleData.TraitVampiric) != 0)
                s.enemyHealth = Mathf.Min(s.enemyMaxHealth, s.enemyHealth + damage / 2);
            damage = Mathf.Max(1, damage - s.bonus[IdleData.AffixTough]);

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

            int absorbed = Absorb(s, damage);
            damage -= absorbed;
            s.health -= damage;
            if (w.record)
                w.Emit(IdleData.EvEnemyHit, "The " + Foe(s, e) + " hits for " + damage + ((absorbed > 0) ? " (Shield took " + absorbed + ")" : "")
                    + " (" + Mathf.Max(0, s.health) + "/" + s.maxHealth + ")", damage);
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
                w.Emit(IdleData.EvKill, "The " + Foe(s, e) + " is dead");
            if (s.bonus[IdleData.AffixLeech] > 0 && s.health < s.maxHealth)
                s.health = Mathf.Min(s.maxHealth, s.health + s.bonus[IdleData.AffixLeech]);
            bool boss = (s.enemyTraits & IdleData.TraitBoss) != 0;
            int rolls = boss ? 3 : IsChampion(s) ? 2 : 1;
            for (int i = 0; i < rolls; i++)
                RollLoot(w, e);
            if (boss)
            {
                s.bossesKilled++;
                // A boss's own piece: reinforced to about half the floor, with one strong enchantment.
                bool weaponDrop = Roll(s, 50);
                Take(w, Affixed(w, Built(s, delegate (int level) {
                    return weaponDrop ? ItemBuilder.CreateRandomWeapon(level) : ItemBuilder.CreateRandomArmor(level, (Genders)s.gender, (Races)s.race); }),
                    1, true));
            }
            else if (IsChampion(s))
            {
                s.championsKilled++;
            }
            if (Roll(s, boss ? 60 : IsChampion(s) ? 15 : 3))
                FindGem(w);
            if (s.soulGemsEmpty > 0 && !e.isClass && Roll(s, IdleData.SoulTrapChance))
            {
                int tier = IdleData.SoulTier(EnemyLevel(s, e));
                s.soulGemsEmpty--;
                s.souls[tier]++;
                s.soulsTrapped++;
                if (w.record)
                    w.Emit(IdleData.EvLoot, "Trapped a " + IdleData.SoulNames[tier].ToLower() + " soul");
            }
            if (IsDaedra(s.enemyId) && Roll(s, 50))
            {
                s.hearts++;
                if (w.record)
                    w.Emit(IdleData.EvLoot, "Took a daedra heart");
            }
            s.enemyId = -1;
            s.enemyTraits = 0;

            // Hurt between fights: Heal if known, then potions, home if that is not enough.
            s.heroShield = 0;
            s.fightStatus = 0;
            while (s.health * 100 < s.maxHealth * s.retreatPercent && s.spellsKnown[IdleData.SpellHealIndex] != 0
                && s.magicka >= Cost(s, IdleData.SpellHealIndex))
                CastHeal(w);
            while (s.health * 100 < s.maxHealth * s.retreatPercent && s.potions > 0)
                Drink(w);
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
                w.Emit(IdleData.EvDied, s.heroName + " was killed by the " + Foe(s, e) + " on floor " + s.floor + ". Loot lost, " + lost + " gold lost");
            GoHome(w, "Carried back to town");
        }

        // ------------------------------------------------------------------ loot and town

        public delegate DaggerfallUnityItem Maker(int level);

        static DaggerfallUnityItem Built(IdleSave s, Maker make)
        {
            int level = Mathf.Max(1, (s.level + s.floor + 1) / 2) + 2;
            UnityEngine.Random.State keep = UnityEngine.Random.state;
            UnityEngine.Random.InitState(Next(s));
            try
            {
                return make(level);
            }
            finally
            {
                UnityEngine.Random.state = keep;
            }
        }

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

            int gold = Range(s, m.MinGold, m.MaxGold) * level * (IsChampion(s) ? 2 : 1) * (100 + s.bonus[IdleData.AffixFortune]) / 100;
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
                for (c = m.WP; Dice100.SuccessRoll((int)c); c *= 0.5f) Take(w, Affixed(w, ItemBuilder.CreateRandomWeapon(level), -1, false));
                for (c = m.AM; Dice100.SuccessRoll((int)c); c *= 0.5f) Take(w, Affixed(w, ItemBuilder.CreateRandomArmor(level, g, r), -1, false));
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
            return item.value * item.stackCount * WorthOf(item) / 100f / Mathf.Max(0.05f, item.weightInKg * item.stackCount);
        }

        // When the pack is full the hero keeps whatever is worth most for its weight.
        static void Take(IdleWorld w, DaggerfallUnityItem item)
        {
            IdleSave s = w.s;
            if (item == null)
                return;
            float need = item.weightInKg * item.stackCount;
            float room = Capacity(s) - CarriedWeight(w);
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
                        w.Emit(IdleData.EvLoot, "Too heavy to carry: " + Name(item));
                    return;
                }
                room += w.haul[worst].weightInKg * w.haul[worst].stackCount;
                s.leftBehind++;
                s.runItems--;
                if (w.record)
                    w.Emit(IdleData.EvLoot, "Dropped " + Name(w.haul[worst]) + " to make room");
                w.haul.RemoveAt(worst);
            }
            w.haul.Add(item);
            s.runItems++;
            if (w.record)
                w.Emit(IdleData.EvLoot, "Picked up " + Name(item));
        }

        // ------------------------------------------------------------------ mining and smelting

        static bool IsDaedra(int id)
        {
            return id == (int)MobileTypes.FrostDaedra || id == (int)MobileTypes.FireDaedra || id == (int)MobileTypes.Daedroth
                || id == (int)MobileTypes.DaedraSeducer || id == (int)MobileTypes.DaedraLord;
        }

        // The best ore a pickaxe can mine: one metal above its own (a daedric one mines anything).
        public static int PickReach(IdleSave s)
        {
            return (s.pick <= 0) ? -1 : Mathf.Min(IdleData.Metals - 1, s.pick);
        }

        public static string PickName(IdleSave s)
        {
            return (s.pick <= 0) ? "no pickaxe" : IdleData.MetalNames[s.pick - 1] + " pickaxe";
        }

        // A cave floor may hold a vein. Deeper floors roll better ore; volcanic caves lean higher, mines lower but richer.
        // The best ore (as an index into OreMetals) a floor of a dungeon type can hold.
        static int TopOre(int dungeonType, int floor)
        {
            int depth = floor + (dungeonType == IdleData.DungeonVolcanicCaves ? 6 : dungeonType == IdleData.DungeonMine ? -3 : 0);
            int top = 0;
            for (int i = 0; i < IdleData.OreFloor.Length; i++)
                if (depth >= IdleData.OreFloor[i])
                    top = i;
            return top;
        }

        static void RollVein(IdleSave s)
        {
            s.vein = 0;
            if (!IdleData.IsCave(s.dungeonType))
                return;
            if (!Roll(s, s.dungeonType == IdleData.DungeonMine ? 90 : 70))
                return;
            int top = TopOre(s.dungeonType, s.floor);
            // Mostly one of the three best ores here; sometimes any lower one, so no metal runs out deep down.
            int pick = Roll(s, 60) ? Range(s, Mathf.Max(0, top - 2), top) : Range(s, 0, top);
            s.vein = IdleData.OreMetals[pick] + 1;
        }

        // Once a floor's fights are done, the hero mines its vein if the pickaxe is good enough. True when mining starts.
        static bool StartMining(IdleWorld w)
        {
            IdleSave s = w.s;
            int metal = s.vein - 1;
            s.vein = 0;
            if (PickReach(s) < metal)
            {
                if (w.record)
                    w.Emit(IdleData.EvMine, "Found a vein of " + IdleData.OreNames[metal].ToLower() + ", but " + (s.pick <= 0 ? "has no pickaxe" : "the " + PickName(s).ToLower() + " cannot break it"));
                return false;
            }
            s.miningMetal = metal + 1;
            s.phase = IdleData.PhaseExplore;
            s.phaseLeft = IdleData.MineSeconds(metal) * IdleData.TicksPerSecond;
            s.phaseTotal = s.phaseLeft;
            if (w.record)
                w.Emit(IdleData.EvMine, "Mining a vein of " + IdleData.OreNames[metal].ToLower());
            return true;
        }

        static void FinishMining(IdleWorld w)
        {
            IdleSave s = w.s;
            int metal = s.miningMetal - 1;
            s.miningMetal = 0;
            int amount = Range(s, 3, 6) + s.floor / 3 + (s.dungeonType == IdleData.DungeonMine ? 2 : 0);
            int coal = Range(s, 0, 2) + (s.dungeonType == IdleData.DungeonMine ? 1 : 0);
            s.ore[metal] += amount;
            s.coal += coal;
            s.oreMined += amount;
            s.veinsMined++;
            if (Roll(s, 25))
                FindGem(w);
            if (w.record)
                w.Emit(IdleData.EvMine, "Mined " + amount + " " + IdleData.OreNames[metal].ToLower() + ((coal > 0) ? " and " + coal + " coal" : ""));
            if (s.pick - 1 < IdleData.Metals - 1)
            {
                s.pickUses--;
                if (s.pickUses <= 0)
                {
                    if (w.record)
                        w.Emit(IdleData.EvMine, "The " + PickName(s).ToLower() + " broke");
                    s.pick = 0;
                    s.picksBroken++;
                }
            }
        }

        // What one ingot of a metal takes from the pouch: two ore (iron ore and coal for steel, ebony ore and a heart
        // for daedric).
        public static int SourceOre(int metal)
        {
            return (metal == 1) ? 0 : (metal == IdleData.Metals - 1) ? 7 : metal;
        }

        public static bool CanSmelt(IdleSave s, int metal)
        {
            if (metal < 0 || metal >= IdleData.Metals || s.ore[SourceOre(metal)] < IdleData.OrePerIngot)
                return false;
            if (metal == 1 && s.coal < 1)
                return false;
            if (metal == IdleData.Metals - 1 && s.hearts < 1)
                return false;
            return s.gold >= IdleData.SmeltFee(metal);
        }

        public static string SmeltRecipe(int metal)
        {
            if (metal == 1)
                return "2 iron ore and 1 coal";
            if (metal == IdleData.Metals - 1)
                return "2 ebony ore and 1 daedra heart";
            return (IdleData.OreNames[metal].Length > 0) ? "2 " + IdleData.OreNames[metal].ToLower() : "";
        }

        // Puts one ingot in the smelter's queue, paying for it now.
        public static bool QueueSmelt(IdleSave s, int metal)
        {
            if (!CanSmelt(s, metal))
                return false;
            s.ore[SourceOre(metal)] -= IdleData.OrePerIngot;
            if (metal == 1)
                s.coal--;
            if (metal == IdleData.Metals - 1)
                s.hearts--;
            s.gold -= IdleData.SmeltFee(metal);
            s.smeltQueue[metal]++;
            return true;
        }

        public static int Queued(IdleSave s)
        {
            int n = 0;
            for (int m = 0; m < IdleData.Metals; m++)
                n += s.smeltQueue[m];
            return n;
        }

        // Seconds until the whole queue is done.
        public static int SmeltSecondsLeft(IdleSave s)
        {
            int ticks = (s.smeltMetal > 0) ? s.smeltLeft : 0;
            for (int m = 0; m < IdleData.Metals; m++)
            {
                int waiting = s.smeltQueue[m] - ((s.smeltMetal == m + 1) ? 1 : 0);
                ticks += waiting * IdleData.SmeltSeconds(m) * IdleData.TicksPerSecond;
            }
            return (ticks + IdleData.TicksPerSecond - 1) / IdleData.TicksPerSecond;
        }

        public static int RushCost(IdleSave s)
        {
            int gold = 0;
            for (int m = 0; m < IdleData.Metals; m++)
            {
                int waiting = s.smeltQueue[m] - ((s.smeltMetal == m + 1) ? 1 : 0);
                if (waiting > 0)
                    gold += IdleData.RushGold(m, waiting * IdleData.SmeltSeconds(m));
            }
            if (s.smeltMetal > 0)
                gold += IdleData.RushGold(s.smeltMetal - 1, s.smeltLeft / IdleData.TicksPerSecond);
            return gold;
        }

        // Pays to finish the whole queue now.
        public static bool Rush(IdleWorld w)
        {
            IdleSave s = w.s;
            int cost = RushCost(s);
            if (Queued(s) == 0 || cost > s.gold)
                return false;
            s.gold -= cost;
            s.smithGold += cost;
            for (int m = 0; m < IdleData.Metals; m++)
            {
                s.ingots[m] += s.smeltQueue[m];
                s.ingotsSmelted += s.smeltQueue[m];
                s.smeltQueue[m] = 0;
            }
            s.smeltMetal = 0;
            s.smeltLeft = 0;
            return true;
        }

        // The smelter works with the clock, at home or away, best metal first.
        static void Smelt(IdleWorld w, long ticks)
        {
            IdleSave s = w.s;
            while (ticks > 0)
            {
                if (s.smeltMetal == 0)
                {
                    for (int m = IdleData.Metals - 1; m >= 0 && s.smeltMetal == 0; m--)
                        if (s.smeltQueue[m] > 0)
                        {
                            s.smeltMetal = m + 1;
                            s.smeltLeft = IdleData.SmeltSeconds(m) * IdleData.TicksPerSecond;
                        }
                    if (s.smeltMetal == 0)
                        return;
                }
                int step = (int)Math.Min(ticks, (long)s.smeltLeft);
                s.smeltLeft -= step;
                ticks -= step;
                if (s.smeltLeft <= 0)
                {
                    int metal = s.smeltMetal - 1;
                    s.smeltQueue[metal] = Mathf.Max(0, s.smeltQueue[metal] - 1);
                    s.ingots[metal]++;
                    s.ingotsSmelted++;
                    s.smeltMetal = 0;
                }
            }
        }

        public static int PickCost(IdleSave s, int metal)
        {
            if (metal == 0 && s.ingots[0] < IdleData.PickIngots)
                return IdleData.PickPrice;
            return IdleData.PickGold(metal);
        }

        // Why a pickaxe of a metal cannot be made now, or null.
        public static string CannotMakePick(IdleSave s, int metal)
        {
            if (metal != 0 && s.ingots[metal] < IdleData.PickIngots)
                return "A " + IdleData.MetalNames[metal].ToLower() + " pickaxe takes " + IdleData.PickIngots + " " + IdleData.MetalNames[metal].ToLower() + " ingots; the pouch has " + s.ingots[metal] + ".";
            if (PickCost(s, metal) > s.gold)
                return "Not enough gold: it costs " + PickCost(s, metal) + ".";
            return null;
        }

        // Buys an iron pickaxe or forges one from three ingots. The old one is thrown away.
        public static bool MakePick(IdleSave s, int metal)
        {
            if (CannotMakePick(s, metal) != null)
                return false;
            bool bought = metal == 0 && s.ingots[0] < IdleData.PickIngots;
            if (!bought)
                s.ingots[metal] -= IdleData.PickIngots;
            int cost = PickCost(s, metal);
            s.gold -= cost;
            s.smithGold += cost;
            s.pick = metal + 1;
            s.pickUses = IdleData.PickUses(metal);
            return true;
        }

        // At home: queue every ingot the pouch allows (steel before iron and daedric before ebony while coal and hearts
        // last), and keep a pickaxe: a better one when the ingots are there, a new iron one when it broke.
        static void AutoForge(IdleWorld w)
        {
            IdleSave s = w.s;
            if (s.noAutoSmith)
                return;
            for (int m = IdleData.Metals - 1; m >= 0; m--)
                while (CanSmelt(s, m) && s.gold >= IdleData.SmeltFee(m) * 4)
                    QueueSmelt(s, m);
            // Pickaxes only for a cave, and only as good as its deepest target floor needs: ingots are better spent
            // on gear than on a pickaxe that has nothing to dig.
            int type = (s.nextType >= 0) ? s.nextType : s.dungeonType;
            if (!IdleData.IsCave(type))
                return;
            int want = Mathf.Max(0, IdleData.OreMetals[TopOre(type, s.targetFloor)] - 1);
            int best = -1;
            for (int m = want; m >= s.pick && best < 0; m--)
                if (m > 0 && CannotMakePick(s, m) == null && PickCost(s, m) * 3 <= s.gold)
                    best = m;
            if (best < 0 && s.pick <= 0 && CannotMakePick(s, 0) == null)
                best = 0;
            if (best >= 0 && best + 1 > s.pick)
            {
                MakePick(s, best);
                if (w.record)
                    w.Emit(IdleData.EvMine, "Now carrying " + PickName(s).ToLower());
            }
        }

        // ------------------------------------------------------------------ affixes and reinforcement

        // Per item: { level, kind, value, kind, value, kind, value, sockets, gem, gem, gem }, kind -1 for none, gem -1
        // for an empty socket. Keyed by the item object itself, because the game's item IDs start again every launch.
        const int EnchantEnd = 1 + IdleData.MaxEnchants * 2;
        const int SocketsAt = EnchantEnd;
        const int GemAt = SocketsAt + 1;
        const int DataLength = GemAt + 3;
        static readonly Dictionary<DaggerfallUnityItem, int[]> affixes = new Dictionary<DaggerfallUnityItem, int[]>();

        public static int[] AffixesOf(DaggerfallUnityItem item)
        {
            int[] a;
            if (item != null && affixes.TryGetValue(item, out a))
                return a;
            return null;
        }

        static int[] NewData()
        {
            int[] a = new int[DataLength];
            for (int i = 1; i < EnchantEnd; i += 2)
                a[i] = -1;
            for (int i = GemAt; i < DataLength; i++)
                a[i] = -1;
            return a;
        }

        static int[] DataFor(DaggerfallUnityItem item)
        {
            int[] a = AffixesOf(item);
            if (a == null)
            {
                a = NewData();
                affixes[item] = a;
            }
            return a;
        }

        public static int Level(DaggerfallUnityItem item)
        {
            int[] a = AffixesOf(item);
            return (a != null) ? a[0] : 0;
        }

        public static int Enchants(DaggerfallUnityItem item)
        {
            int[] a = AffixesOf(item);
            int n = 0;
            for (int i = 1; a != null && i < EnchantEnd; i += 2)
                if (a[i] >= 0)
                    n++;
            return n;
        }

        // Price multiplier in percent: enchantments and reinforcement make an item worth more.
        static int WorthOf(DaggerfallUnityItem item)
        {
            return 100 + 60 * Enchants(item) + 10 * Level(item);
        }

        public static int AffixValue(DaggerfallUnityItem item, int kind)
        {
            int[] a = AffixesOf(item);
            if (a == null)
                return 0;
            int sum = 0;
            for (int i = 1; i < EnchantEnd; i += 2)
                if (a[i] == kind)
                    sum += a[i + 1];
            if (baseScore)
                return sum;
            bool weapon = item.ItemGroup == ItemGroups.Weapons;
            for (int i = 0; i < a[SocketsAt]; i++)
            {
                int code = a[GemAt + i];
                if (code < 0)
                    continue;
                int gem = code / 16;
                if ((weapon ? IdleData.GemWeapon[gem] : IdleData.GemArmor[gem]) == kind)
                    sum += GemValue(code, weapon);
            }
            return sum;
        }

        // ------------------------------------------------------------------ the enchanter

        public static int BestSoul(IdleSave s)
        {
            for (int tier = IdleData.SoulTiers - 1; tier >= 0; tier--)
                if (s.souls[tier] > 0)
                    return tier;
            return -1;
        }

        public static int SoulCount(IdleSave s)
        {
            int n = 0;
            for (int i = 0; i < s.souls.Length; i++)
                n += s.souls[i];
            return n;
        }

        // A new enchantment for a piece: a kind it does not have yet, valued by the deepest floor reached and the soul.
        static void RollEnchant(IdleSave s, DaggerfallUnityItem item, int at, int tier)
        {
            int[] a = DataFor(item);
            bool weapon = item.ItemGroup == ItemGroups.Weapons;
            List<int> pool = new List<int>();
            for (int k = 0; k < IdleData.AffixKinds; k++)
            {
                if (IdleData.AffixOn[k] < 0 || !(IdleData.AffixOn[k] == 2 || (IdleData.AffixOn[k] == 1) == weapon))
                    continue;
                bool has = false;
                for (int i = 1; i < EnchantEnd; i += 2)
                    if (i != at && a[i] == k)
                        has = true;
                if (!has)
                    pool.Add(k);
            }
            if (pool.Count == 0)
                return;
            int kind = pool[Range(s, 0, pool.Count - 1)];
            int value = Range(s, IdleData.AffixMin[kind], IdleData.AffixMax[kind]) * (100 + 6 * Mathf.Max(0, s.deepestFloor - 1)) / 100;
            value = value * IdleData.SoulPower[tier] / 100;
            a[at] = kind;
            a[at + 1] = Mathf.Max(1, value);
        }

        public static string CannotEnchant(IdleSave s, DaggerfallUnityItem item)
        {
            if (item == null || SlotFor(item) < 0)
                return "Only weapons and armor take enchantments here.";
            if (Enchants(item) >= IdleData.MaxEnchants)
                return "Three enchantments is the most a piece holds; roll one anew instead.";
            int tier = BestSoul(s);
            if (tier < 0)
                return "No filled soul gem. Carry empty ones (+ and - here) and the hero traps creatures' souls.";
            if (IdleData.EnchantGold(Enchants(item), tier) > s.gold)
                return "Not enough gold: it costs " + IdleData.EnchantGold(Enchants(item), tier) + ".";
            return null;
        }

        public static bool Enchant(IdleWorld w, DaggerfallUnityItem item)
        {
            IdleSave s = w.s;
            if (CannotEnchant(s, item) != null)
                return false;
            int tier = BestSoul(s);
            int cost = IdleData.EnchantGold(Enchants(item), tier);
            int[] a = DataFor(item);
            int at = -1;
            for (int i = 1; i < EnchantEnd && at < 0; i += 2)
                if (a[i] < 0)
                    at = i;
            if (at < 0)
                return false;
            s.gold -= cost;
            s.enchantGold += cost;
            s.souls[tier]--;
            RollEnchant(s, item, at, tier);
            s.enchantsAdded++;
            RefreshBonus(w);
            return true;
        }

        // The enchantment in a slot (0 to 2), or -1 when the slot is empty.
        public static int EnchantKind(DaggerfallUnityItem item, int slot)
        {
            int[] a = AffixesOf(item);
            return (a != null && slot >= 0 && slot < IdleData.MaxEnchants) ? a[1 + slot * 2] : -1;
        }

        public static int EnchantValue(DaggerfallUnityItem item, int slot)
        {
            int[] a = AffixesOf(item);
            return (a != null && slot >= 0 && slot < IdleData.MaxEnchants) ? a[2 + slot * 2] : 0;
        }

        public static string CannotReroll(IdleSave s, DaggerfallUnityItem item, int slot)
        {
            if (EnchantKind(item, slot) < 0)
                return "That slot is empty.";
            int tier = BestSoul(s);
            if (tier < 0)
                return "No filled soul gem.";
            if (IdleData.RerollGold(tier) > s.gold)
                return "Not enough gold: it costs " + IdleData.RerollGold(tier) + ".";
            return null;
        }

        public static bool Reroll(IdleWorld w, DaggerfallUnityItem item, int slot)
        {
            IdleSave s = w.s;
            if (CannotReroll(s, item, slot) != null)
                return false;
            int tier = BestSoul(s);
            s.gold -= IdleData.RerollGold(tier);
            s.enchantGold += IdleData.RerollGold(tier);
            s.souls[tier]--;
            RollEnchant(s, item, 1 + slot * 2, tier);
            s.rerolls++;
            RefreshBonus(w);
            return true;
        }

        // At home: the cheapest enchantment for a worn piece with a free slot, again and again while there are souls
        // and it costs at most half the gold. Rolling anew is left to the player.
        static int AutoEnchant(IdleWorld w)
        {
            IdleSave s = w.s;
            if (s.noAutoSmith)
                return 0;
            long before = s.enchantGold;
            for (int guard = 0; guard < 50 && BestSoul(s) >= 0; guard++)
            {
                DaggerfallUnityItem best = null;
                int bestCost = int.MaxValue;
                for (int i = 0; i < w.gear.Length; i++)
                {
                    DaggerfallUnityItem item = w.gear[i];
                    if (item == null || !Works(item) || Enchants(item) >= IdleData.MaxEnchants)
                        continue;
                    int cost = IdleData.EnchantGold(Enchants(item), BestSoul(s));
                    if (cost < bestCost)
                    {
                        best = item;
                        bestCost = cost;
                    }
                }
                if (best == null || (long)bestCost * 2 > s.gold || !Enchant(w, best))
                    break;
            }
            return (int)(s.enchantGold - before);
        }

        // ------------------------------------------------------------------ gems and sockets

        // While set, item scores leave gems out: gems move to a new piece, so they should not keep an old one worn.
        static bool baseScore;

        public static int GemValue(int code, bool weapon)
        {
            int gem = code / 16, level = code % 16;
            return Mathf.Max(1, (weapon ? IdleData.GemWeaponTenths[gem] : IdleData.GemArmorTenths[gem]) * level / 10);
        }

        public static string GemName(int code)
        {
            int level = Mathf.Clamp(code % 16, 1, IdleData.GemTop);
            return IdleData.GemLevelNames[level] + " " + IdleData.GemNames[Mathf.Clamp(code / 16, 0, IdleData.GemKinds - 1)] + " (" + level + ")";
        }

        public static string GemEffect(int code, bool weapon)
        {
            int gem = code / 16;
            return string.Format(IdleData.AffixText[weapon ? IdleData.GemWeapon[gem] : IdleData.GemArmor[gem]], GemValue(code, weapon));
        }

        public static int Sockets(DaggerfallUnityItem item)
        {
            int[] a = AffixesOf(item);
            return (a != null) ? a[SocketsAt] : 0;
        }

        public static int GemIn(DaggerfallUnityItem item, int socket)
        {
            int[] a = AffixesOf(item);
            return (a != null && socket >= 0 && socket < a[SocketsAt]) ? a[GemAt + socket] : -1;
        }

        public static int MaxSockets(DaggerfallUnityItem item)
        {
            int slot = SlotFor(item);
            return (slot < 0) ? 0 : IdleData.MaxSockets[slot];
        }

        // One line per socket, for item cards.
        public static List<string> SocketLines(DaggerfallUnityItem item)
        {
            List<string> lines = new List<string>();
            bool weapon = item != null && item.ItemGroup == ItemGroups.Weapons;
            for (int i = 0; i < Sockets(item); i++)
            {
                int code = GemIn(item, i);
                lines.Add((code < 0) ? "Empty socket" : GemName(code) + ": " + GemEffect(code, weapon));
            }
            return lines;
        }

        public static int GemCount(IdleSave s)
        {
            int n = 0;
            for (int i = 0; i < s.gems.Length; i++)
                n += s.gems[i];
            return n;
        }

        // A gem from a vein or a kill, better deeper down.
        static void FindGem(IdleWorld w)
        {
            IdleSave s = w.s;
            int code = Range(s, 0, IdleData.GemKinds - 1) * 16 + Mathf.Clamp(1 + s.floor / 12, 1, 4);
            s.gems[code]++;
            s.gemsFound++;
            if (w.record)
                w.Emit(IdleData.EvLoot, "Found a " + GemName(code).ToLower());
        }

        public static int SocketCost(DaggerfallUnityItem item)
        {
            return IdleData.SocketGold(Sockets(item), Mathf.Max(0, Rung(item)));
        }

        public static string CannotAddSocket(IdleSave s, DaggerfallUnityItem item)
        {
            if (item == null || SlotFor(item) < 0)
                return "Only weapons and armor take sockets.";
            if (Sockets(item) >= MaxSockets(item))
                return "This piece takes no more than " + MaxSockets(item) + " socket(s).";
            if (SocketCost(item) > s.gold)
                return "Not enough gold: a socket costs " + SocketCost(item) + ".";
            return null;
        }

        public static bool AddSocket(IdleWorld w, DaggerfallUnityItem item)
        {
            IdleSave s = w.s;
            if (CannotAddSocket(s, item) != null)
                return false;
            int cost = SocketCost(item);
            s.gold -= cost;
            s.jewelGold += cost;
            DataFor(item)[SocketsAt]++;
            s.socketsAdded++;
            RefreshBonus(w);
            return true;
        }

        // The best gem in the pouch for a piece: highest level, then the slot's preferred kind. -1 when none.
        public static int BestPouchGem(IdleSave s, DaggerfallUnityItem item)
        {
            int[] order = (item.ItemGroup == ItemGroups.Weapons) ? IdleData.GemWeaponOrder : IdleData.GemArmorOrder;
            for (int level = IdleData.GemTop; level >= 1; level--)
                for (int i = 0; i < order.Length; i++)
                    if (s.gems[order[i] * 16 + level] > 0)
                        return order[i] * 16 + level;
            return -1;
        }

        // Puts a gem from the pouch into an empty socket.
        public static bool SetGem(IdleWorld w, DaggerfallUnityItem item, int socket, int code)
        {
            IdleSave s = w.s;
            if (code < 0 || s.gems[code] <= 0 || socket < 0 || socket >= Sockets(item) || GemIn(item, socket) >= 0)
                return false;
            s.gems[code]--;
            DataFor(item)[GemAt + socket] = code;
            RefreshBonus(w);
            return true;
        }

        public static bool RemoveGem(IdleWorld w, DaggerfallUnityItem item, int socket)
        {
            IdleSave s = w.s;
            int code = GemIn(item, socket);
            if (code < 0 || IdleData.GemRemoveGold(code % 16) > s.gold)
                return false;
            s.gold -= IdleData.GemRemoveGold(code % 16);
            s.jewelGold += IdleData.GemRemoveGold(code % 16);
            s.gems[code]++;
            DataFor(item)[GemAt + socket] = -1;
            RefreshBonus(w);
            return true;
        }

        // Gems of a piece that is sold go back to the pouch, free.
        static void ReturnGems(IdleSave s, DaggerfallUnityItem item)
        {
            int[] a = AffixesOf(item);
            for (int i = 0; a != null && i < a[SocketsAt]; i++)
                if (a[GemAt + i] >= 0)
                {
                    s.gems[a[GemAt + i]]++;
                    a[GemAt + i] = -1;
                }
        }

        // The pouch gem of the same kind used up by an upgrade from level 6 on (the lowest level there is), or -1.
        public static int SecondGem(IdleSave s, int code)
        {
            int gem = code / 16;
            for (int level = 1; level <= IdleData.GemTop; level++)
                if (s.gems[gem * 16 + level] > 0)
                    return gem * 16 + level;
            return -1;
        }

        public static string CannotUpgradeGem(IdleSave s, int code)
        {
            if (code < 0)
                return "The socket is empty.";
            int level = code % 16;
            if (level >= IdleData.GemTop)
                return "A royal gem is as good as gems get.";
            if (level >= IdleData.GemSecondFrom && SecondGem(s, code) < 0)
                return "From level " + IdleData.GemSecondFrom + " on, an upgrade also takes another " + IdleData.GemNames[code / 16].ToLower() + " from the pouch.";
            if (IdleData.GemUpgradeGold(level) > s.gold)
                return "Not enough gold: the upgrade costs " + IdleData.GemUpgradeGold(level) + ".";
            return null;
        }

        public static bool UpgradeGem(IdleWorld w, DaggerfallUnityItem item, int socket)
        {
            IdleSave s = w.s;
            int code = GemIn(item, socket);
            if (CannotUpgradeGem(s, code) != null)
                return false;
            int level = code % 16;
            if (level >= IdleData.GemSecondFrom)
                s.gems[SecondGem(s, code)]--;
            int cost = IdleData.GemUpgradeGold(level);
            s.gold -= cost;
            s.jewelGold += cost;
            DataFor(item)[GemAt + socket] = code + 1;
            s.gemUpgrades++;
            RefreshBonus(w);
            return true;
        }

        // At home: fill empty sockets of worn gear from the pouch, then keep paying for the cheapest new socket (only
        // while there are gems to fill it) or gem upgrade while it costs at most half the gold.
        static int AutoJewel(IdleWorld w)
        {
            IdleSave s = w.s;
            if (s.noAutoSmith)
                return 0;
            long before = s.jewelGold;
            for (int guard = 0; guard < 300; guard++)
            {
                for (int i = 0; i < w.gear.Length; i++)
                {
                    DaggerfallUnityItem item = w.gear[i];
                    if (item == null || !Works(item))
                        continue;
                    for (int k = 0; k < Sockets(item); k++)
                        if (GemIn(item, k) < 0)
                            SetGem(w, item, k, BestPouchGem(s, item));
                }
                DaggerfallUnityItem best = null;
                int bestSocket = -1, bestCost = int.MaxValue;
                bool haveGem = GemCount(s) > 0;
                for (int i = 0; i < w.gear.Length; i++)
                {
                    DaggerfallUnityItem item = w.gear[i];
                    if (item == null || !Works(item))
                        continue;
                    if (haveGem && Sockets(item) < MaxSockets(item) && SocketCost(item) < bestCost)
                    {
                        best = item;
                        bestSocket = -1;
                        bestCost = SocketCost(item);
                    }
                    for (int k = 0; k < Sockets(item); k++)
                    {
                        int code = GemIn(item, k);
                        if (code < 0 || code % 16 >= IdleData.GemTop)
                            continue;
                        if (code % 16 >= IdleData.GemSecondFrom && SecondGem(s, code) < 0)
                            continue;
                        if (IdleData.GemUpgradeGold(code % 16) < bestCost)
                        {
                            best = item;
                            bestSocket = k;
                            bestCost = IdleData.GemUpgradeGold(code % 16);
                        }
                    }
                }
                if (best == null || (long)bestCost * 2 > s.gold)
                    break;
                if (bestSocket < 0 ? !AddSocket(w, best) : !UpgradeGem(w, best, bestSocket))
                    break;
            }
            return (int)(s.jewelGold - before);
        }

        // Where an item stands on its ladder: weapons 0-9 (iron to daedric); armor 0 leather, 1 chain, 2-11 plate.
        public static int Rung(DaggerfallUnityItem item)
        {
            if (item == null)
                return -1;
            int v = item.NativeMaterialValue;
            if (item.ItemGroup == ItemGroups.Weapons)
                return Mathf.Clamp(v, 0, IdleData.Metals - 1);
            if (v == (int)ArmorMaterialTypes.Leather)
                return 0;
            if (v == (int)ArmorMaterialTypes.Chain || v == (int)ArmorMaterialTypes.Chain2)
                return 1;
            return 2 + Mathf.Clamp(v - (int)ArmorMaterialTypes.Iron, 0, IdleData.Metals - 1);
        }

        static int TopRung(DaggerfallUnityItem item)
        {
            return (item.ItemGroup == ItemGroups.Weapons) ? IdleData.Metals - 1 : IdleData.Metals + 1;
        }

        // The metal whose ingots reinforce an item at a rung.
        static int MetalAt(DaggerfallUnityItem item, int rung)
        {
            return (item.ItemGroup == ItemGroups.Weapons) ? rung : Mathf.Max(0, rung - 2);
        }

        public static int Metal(DaggerfallUnityItem item)
        {
            return MetalAt(item, Rung(item));
        }

        // The ladder name: Leather and Chain for the two lowest armor rungs, the metal otherwise.
        public static string MetalName(DaggerfallUnityItem item)
        {
            int rung = Rung(item);
            if (item.ItemGroup != ItemGroups.Weapons && rung == 0)
                return "Leather";
            if (item.ItemGroup != ItemGroups.Weapons && rung == 1)
                return "Chain";
            return IdleData.MetalNames[Mathf.Clamp(Metal(item), 0, IdleData.Metals - 1)];
        }

        static int CapAt(DaggerfallUnityItem item, int rung)
        {
            if (item.ItemGroup != ItemGroups.Weapons && rung == 0)
                return IdleData.LeatherCap;
            if (item.ItemGroup != ItemGroups.Weapons && rung == 1)
                return IdleData.ChainCap;
            return IdleData.MetalCap[MetalAt(item, rung)];
        }

        public static int LevelCap(DaggerfallUnityItem item)
        {
            return (item == null || SlotFor(item) < 0) ? 0 : CapAt(item, Rung(item));
        }

        // The next reinforcement step: its gold and ingots. Ingots short in the pouch are bought at the smith.
        public static int StepGold(DaggerfallUnityItem item)
        {
            int next = Level(item) + 1;
            return IdleData.MetalGold[Metal(item)] * next * next;
        }

        public static int StepIngots(DaggerfallUnityItem item)
        {
            return 1 + Level(item) / 5;
        }

        // Total gold for a number of ingots of a metal, buying whatever the pouch is short of.
        public static int IngotBill(IdleSave s, int metal, int needed)
        {
            int missing = Mathf.Max(0, needed - s.ingots[metal]);
            if (missing == 0)
                return 0;
            return (metal < IdleData.SoldIngots) ? missing * IdleData.IngotPrice[metal] : IdleData.Unbuyable;
        }

        public static string NeedIngots(IdleSave s, int metal, int needed)
        {
            return "Needs " + needed + " " + IdleData.MetalNames[metal] + " ingot(s), the pouch has " + s.ingots[metal] + ". The smith only sells iron and steel: "
                + (metal == 1 ? "" : metal == 9 ? "smelt ebony ore with a daedra heart, " : "mine " + IdleData.OreNames[metal].ToLower() + " in caves and smelt it, ")
                + "or salvage " + IdleData.MetalNames[metal].ToLower() + " gear by selling it.";
        }

        public static int StepCost(IdleSave s, DaggerfallUnityItem item)
        {
            return StepGold(item) + IngotBill(s, Metal(item), StepIngots(item));
        }

        // Why a piece cannot be reinforced now, or null when it can.
        public static string CannotReinforce(IdleSave s, DaggerfallUnityItem item)
        {
            if (item == null || SlotFor(item) < 0)
                return "Only weapons and armor can be reinforced.";
            if (Level(item) >= LevelCap(item))
                return MetalName(item) + " goes no higher than +" + LevelCap(item) + ". Reforge it into a better metal.";
            if (IngotBill(s, Metal(item), StepIngots(item)) >= IdleData.Unbuyable)
                return NeedIngots(s, Metal(item), StepIngots(item));
            if (StepCost(s, item) > s.gold)
                return "Not enough gold: the next level costs " + StepCost(s, item) + ".";
            return null;
        }

        static void Spend(IdleSave s, int metal, int needed, int gold)
        {
            int bill = IngotBill(s, metal, needed);
            s.ingots[metal] = Mathf.Max(0, s.ingots[metal] - needed);
            s.gold -= gold + bill;
            s.smithGold += gold + bill;
        }

        public static bool Reinforce(IdleWorld w, DaggerfallUnityItem item)
        {
            IdleSave s = w.s;
            if (CannotReinforce(s, item) != null)
                return false;
            Spend(s, Metal(item), StepIngots(item), StepGold(item));
            DataFor(item)[0]++;
            s.levelsBought++;
            RefreshBonus(w);
            return true;
        }

        // Reforging: the next metal up, keeping level and enchantments, for ingots of the new metal and gold.
        public static int ReforgeIngots(DaggerfallUnityItem item)
        {
            return 4 + Level(item) / 2;
        }

        public static int ReforgeGold(DaggerfallUnityItem item)
        {
            return IdleData.MetalGold[MetalAt(item, Mathf.Min(TopRung(item), Rung(item) + 1))] * IdleData.ReforgeGold;
        }

        public static int ReforgeCost(IdleSave s, DaggerfallUnityItem item)
        {
            int metal = MetalAt(item, Mathf.Min(TopRung(item), Rung(item) + 1));
            return ReforgeGold(item) + IngotBill(s, metal, ReforgeIngots(item));
        }

        public static string NextMetalName(DaggerfallUnityItem item)
        {
            int rung = Rung(item) + 1;
            if (item.ItemGroup != ItemGroups.Weapons && rung == 1)
                return "Chain";
            return IdleData.MetalNames[MetalAt(item, Mathf.Min(TopRung(item), rung))];
        }

        static bool Reforgeable(DaggerfallUnityItem item)
        {
            return !item.IsArtifact && item.TemplateIndex <= ItemHelper.LastDFTemplate;
        }

        public static string CannotReforge(IdleSave s, DaggerfallUnityItem item)
        {
            if (item == null || SlotFor(item) < 0)
                return "Only weapons and armor can be reforged.";
            if (Rung(item) >= TopRung(item))
                return "Daedric is the best metal there is.";
            if (!Reforgeable(item))
                return "Artifacts keep their own metal; they can still be reinforced.";
            int nextMetal = MetalAt(item, Rung(item) + 1);
            if (IngotBill(s, nextMetal, ReforgeIngots(item)) >= IdleData.Unbuyable)
                return NeedIngots(s, nextMetal, ReforgeIngots(item));
            if (ReforgeCost(s, item) > s.gold)
                return "Not enough gold: reforging costs " + ReforgeCost(s, item) + ".";
            return null;
        }

        // Returns the new item (the old one is dropped), or null.
        public static DaggerfallUnityItem Reforge(IdleWorld w, DaggerfallUnityItem item)
        {
            IdleSave s = w.s;
            if (CannotReforge(s, item) != null)
                return null;
            int rung = Rung(item) + 1;
            int metal = MetalAt(item, rung);
            DaggerfallUnityItem made;
            UnityEngine.Random.State keep = UnityEngine.Random.state;
            try
            {
                if (item.ItemGroup == ItemGroups.Weapons)
                    made = ItemBuilder.CreateWeapon((Weapons)item.TemplateIndex, (WeaponMaterialTypes)metal);
                else
                {
                    ArmorMaterialTypes material = (rung == 1) ? ArmorMaterialTypes.Chain : (ArmorMaterialTypes)((int)ArmorMaterialTypes.Iron + metal);
                    made = ItemBuilder.CreateArmor((Genders)s.gender, (Races)s.race, (Armor)item.TemplateIndex, material, item.CurrentVariant);
                }
            }
            finally
            {
                UnityEngine.Random.state = keep;
            }
            if (made == null)
                return null;
            // A magic item keeps its name and its magic.
            if (item.IsEnchanted)
            {
                made.shortName = item.shortName;
                made.legacyMagic = (item.legacyMagic != null) ? (DaggerfallConnect.FallExe.DaggerfallEnchantment[])item.legacyMagic.Clone() : null;
                made.customMagic = (item.customMagic != null) ? (DaggerfallWorkshop.Game.MagicAndEffects.CustomEnchantment[])item.customMagic.Clone() : null;
            }
            Spend(s, metal, ReforgeIngots(item), ReforgeGold(item));
            int[] old = AffixesOf(item);
            if (old != null)
            {
                affixes[made] = (int[])old.Clone();
                affixes.Remove(item);
            }
            for (int i = 0; i < w.gear.Length; i++)
                if (w.gear[i] == item)
                    w.gear[i] = made;
            int at = w.stash.IndexOf(item);
            if (at >= 0)
                w.stash[at] = made;
            s.reforges++;
            RefreshBonus(w);
            return made;
        }

        // At home: spend up to half the gold on the cheapest next step for worn gear, again and again. A piece at
        // its metal's top is reforged instead. Off when the orders say so.
        static int AutoSmith(IdleWorld w)
        {
            IdleSave s = w.s;
            if (s.noAutoSmith)
                return 0;
            int spent = 0;
            for (int guard = 0; guard < 200; guard++)
            {
                DaggerfallUnityItem best = null;
                bool reforge = false;
                int bestCost = int.MaxValue;
                for (int i = 0; i < w.gear.Length; i++)
                {
                    DaggerfallUnityItem item = w.gear[i];
                    if (item == null || !Works(item))
                        continue;
                    bool atTop = Level(item) >= LevelCap(item);
                    if (atTop && Rung(item) >= TopRung(item))
                        continue;
                    if (atTop && !Reforgeable(item))
                        continue;           // an artifact at its top stays as it is
                    int cost = atTop ? ReforgeCost(s, item) : StepCost(s, item);
                    if (cost < bestCost)
                    {
                        best = item;
                        bestCost = cost;
                        reforge = atTop;
                    }
                }
                if (best == null || bestCost * 2 > s.gold)
                    break;
                int before = s.gold;
                if (reforge ? Reforge(w, best) == null : !Reinforce(w, best))
                    break;
                spent += before - s.gold;
            }
            return spent;
        }

        // "+7 Keen Steel Longsword of the Leech".
        public static string Name(DaggerfallUnityItem item)
        {
            if (item == null)
                return "";
            int[] a = AffixesOf(item);
            if (a == null)
                return item.LongName;
            string before = "", after = "";
            for (int i = 1; i < EnchantEnd; i += 2)
            {
                if (a[i] < 0)
                    continue;
                if (IdleData.AffixPrefix[a[i]])
                    before += IdleData.AffixWords[a[i]] + " ";
                else
                    after += " " + IdleData.AffixWords[a[i]];
            }
            return ((a[0] > 0) ? "+" + a[0] + " " : "") + before + item.LongName + after;
        }

        // What the affixes do, one line each, for item cards.
        public static List<string> AffixLines(DaggerfallUnityItem item)
        {
            List<string> lines = new List<string>();
            int[] a = AffixesOf(item);
            for (int i = 1; a != null && i < EnchantEnd; i += 2)
                if (a[i] >= 0)
                    lines.Add(string.Format(IdleData.AffixText[a[i]], a[i + 1]));
            return lines;
        }

        // What the reinforcement level adds, for item cards.
        public static string LevelLine(DaggerfallUnityItem item)
        {
            int level = Level(item);
            string top = " (" + MetalName(item) + " reaches +" + LevelCap(item) + ")";
            if (item.ItemGroup == ItemGroups.Weapons)
                return "+" + level + ": " + (level * IdleData.LevelDamagePercent) + "% more base damage" + top;
            return "+" + level + ": +" + (level / 2) + " armor" + top;
        }

        // Rolls a dropped weapon or piece of armor: enchantments (deeper floors and champions give more) and, deeper
        // down, a few reinforcement levels. A boss's piece comes at about half the floor in levels with one strong
        // enchantment. enchants -1 rolls them.
        static DaggerfallUnityItem Affixed(IdleWorld w, DaggerfallUnityItem item, int enchants, bool boss)
        {
            IdleSave s = w.s;
            int slot = SlotFor(item);
            if (item == null || slot < 0)
                return item;
            int level = 0;
            if (boss)
                level = Mathf.Max(1, s.floor / 2);
            else if (enchants < 0 && Roll(s, Mathf.Min(30, s.floor * 2)))
                level = Range(s, 1, Mathf.Min(3, 1 + s.floor / 8));
            if (enchants < 0)
            {
                int chance = Mathf.Min(40, 8 + s.floor) + (IsChampion(s) ? 20 : 0);
                enchants = !Roll(s, chance) ? 0 : Roll(s, 25 + (IsChampion(s) ? 25 : 0)) ? 2 : 1;
            }
            level = Mathf.Min(level, LevelCap(item));
            int sockets = (boss && Roll(s, 50)) ? 1 : (IsChampion(s) && Roll(s, 5)) ? 1 : 0;
            if (level == 0 && enchants == 0 && sockets == 0)
                return item;
            bool weapon = slot == IdleData.SlotWeapon;
            int[] a = DataFor(item);
            a[0] = level;
            a[SocketsAt] = Mathf.Min(sockets, MaxSockets(item));
            // One enchantment is a prefix or a suffix; two are one of each.
            bool prefixFirst = Roll(s, 50);
            for (int n = 0; n < enchants && n < 2; n++)
            {
                bool prefix = (enchants == 2) ? (n == 0) : prefixFirst;
                List<int> pool = new List<int>();
                for (int k = 0; k < IdleData.AffixKinds; k++)
                    if (IdleData.AffixOn[k] >= 0 && IdleData.AffixPrefix[k] == prefix && (IdleData.AffixOn[k] == 2 || (IdleData.AffixOn[k] == 1) == weapon))
                        pool.Add(k);
                if (pool.Count == 0)
                    continue;
                int kind = pool[Range(s, 0, pool.Count - 1)];
                int value = Range(s, IdleData.AffixMin[kind], IdleData.AffixMax[kind]) * (100 + 6 * Mathf.Max(0, s.floor - 1)) / 100;
                if (boss)
                    value = value * 3 / 2;
                a[1 + n * 2] = kind;
                a[2 + n * 2] = Mathf.Max(1, value);
            }
            if (enchants > 0)
                s.affixesFound++;
            return item;
        }

        // Sums the affixes of everything worn and working, with caps.
        public static void RefreshBonus(IdleWorld w)
        {
            IdleSave s = w.s;
            if (s.bonus == null || s.bonus.Length != IdleData.AffixKinds)
                s.bonus = new int[IdleData.AffixKinds];
            for (int k = 0; k < s.bonus.Length; k++)
                s.bonus[k] = 0;
            for (int i = 0; i < w.gear.Length; i++)
            {
                if (AffixesOf(w.gear[i]) == null || !Works(w.gear[i]))
                    continue;
                for (int k = 0; k < IdleData.AffixKinds; k++)
                    if (k != IdleData.AffixSturdy)          // Sturdy counts on its own body part
                        s.bonus[k] += AffixValue(w.gear[i], k);
            }
            for (int k = 0; k < s.bonus.Length; k++)
                s.bonus[k] = Mathf.Min(s.bonus[k], IdleData.AffixCap[k]);
        }

        public static int HeroInterval(IdleSave s)
        {
            return Mathf.Max(5, IdleData.AttackInterval(s.stats[IdleData.StatSpeed]) * (100 - s.bonus[IdleData.AffixSwift]) / 100);
        }

        // A piece's armor points: the game's own number, Sturdy, and one for every two reinforcement levels.
        public static int ArmorValue(DaggerfallUnityItem item, bool shield)
        {
            if (item == null)
                return 0;
            return (shield ? item.GetShieldArmorValue() : item.GetMaterialArmorValue()) + AffixValue(item, IdleData.AffixSturdy) + Level(item) / 2;
        }

        // A weapon's base damage roll, raised by its reinforcement level.
        static int Reinforced(DaggerfallUnityItem weapon, int damage)
        {
            return damage * (100 + IdleData.LevelDamagePercent * Level(weapon)) / 100;
        }

        // Armor worth for auto-equip: armor points first, other affixes break ties and small gaps.
        static float ArmorScore(DaggerfallUnityItem item, bool shield)
        {
            if (item == null)
                return 0f;
            float armor = ArmorValue(item, shield);
            int[] a = AffixesOf(item);
            int others = 0;
            for (int i = 1; a != null && i < EnchantEnd; i += 2)
                if (a[i] >= 0 && a[i] != IdleData.AffixSturdy)
                    others++;
            for (int i = 0; i < Sockets(item); i++)
                others += (GemIn(item, i) >= 0 && !baseScore) ? 2 : 1;
            return armor * 10f + others * 6f + (Level(item) % 2) * 0.5f;
        }

        // "l,k,v,k,v,k,v" per item, items separated by ';' and in the same order as the item list.
        static string PackAffixes(List<DaggerfallUnityItem> items)
        {
            System.Text.StringBuilder b = new System.Text.StringBuilder();
            for (int i = 0; i < items.Count; i++)
            {
                if (i > 0)
                    b.Append(';');
                int[] a = AffixesOf(items[i]);
                for (int j = 0; a != null && j < a.Length; j++)
                {
                    if (j > 0)
                        b.Append(',');
                    b.Append(a[j]);
                }
            }
            return b.ToString();
        }

        // Also reads the older "rarity,k,v,k,v" form: Fine and Rare keep their affixes, Heroic becomes +3.
        static void UnpackAffixes(List<DaggerfallUnityItem> items, string text)
        {
            if (string.IsNullOrEmpty(text))
                return;
            string[] parts = text.Split(';');
            for (int i = 0; i < parts.Length && i < items.Count; i++)
            {
                if (parts[i].Length == 0)
                    continue;
                string[] fields = parts[i].Split(',');
                if (fields.Length != 5 && fields.Length != EnchantEnd && fields.Length != DataLength)
                    continue;
                int[] read = new int[fields.Length];
                bool ok = true;
                for (int j = 0; j < fields.Length; j++)
                    ok &= int.TryParse(fields[j], out read[j]);
                if (!ok || read[0] < 0)
                    continue;
                int[] a = NewData();
                for (int j = 1; j < read.Length; j++)
                    a[j] = read[j];
                a[0] = (fields.Length == 5) ? ((read[0] >= 3) ? 3 : 0) : read[0];
                for (int j = 1; j < EnchantEnd; j += 2)
                    if (a[j] >= IdleData.AffixKinds)
                        ok = false;
                a[SocketsAt] = Mathf.Clamp(a[SocketsAt], 0, 3);
                for (int j = GemAt; j < DataLength; j++)
                    if (a[j] >= IdleData.GemCodes || (a[j] >= 0 && a[j] % 16 == 0))
                        a[j] = -1;
                if (ok)
                {
                    a[0] = Mathf.Min(a[0], LevelCap(items[i]));
                    affixes[items[i]] = a;
                }
            }
        }

        public static bool IsUpgrade(IdleWorld w, DaggerfallUnityItem item, int slot)
        {
            baseScore = true;
            try
            {
                return BetterThanWorn(w, item, slot);
            }
            finally
            {
                baseScore = false;
            }
        }

        static bool BetterThanWorn(IdleWorld w, DaggerfallUnityItem item, int slot)
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
                return old == null || !Works(old) || ArmorScore(item, true) > ArmorScore(old, true);
            }
            return old == null || !Works(old) || ArmorScore(item, false) > ArmorScore(old, false);
        }

        public static int SalePrice(DaggerfallUnityItem item)
        {
            return Mathf.Max(1, (int)((long)item.value * item.stackCount * WorthOf(item) / 100 / IdleData.SaleDivisor));
        }

        // Puts a spare weapon or piece of armor in the stash. When the stash is full the cheapest thing in it is
        // sold, and the gold from that is returned.
        // Selling a metal weapon or armor piece sometimes leaves an ingot of its metal with the smith's scrap.
        static void Salvage(IdleWorld w, DaggerfallUnityItem item)
        {
            if (SlotFor(item) < 0 || (item.ItemGroup != ItemGroups.Weapons && Rung(item) == 0) || IsBow(item))
                return;
            if (!Roll(w.s, IdleData.SalvageChance))
                return;
            w.s.ingots[Metal(item)]++;
            w.s.ingotsSalvaged++;
        }

        static int Store(IdleWorld w, DaggerfallUnityItem item)
        {
            w.stash.Add(item);
            int gold = 0;
            while (w.stash.Count > IdleData.StashSize)
            {
                int worst = 0;
                for (int i = 1; i < w.stash.Count; i++)
                    if (SalePrice(w.stash[i]) < SalePrice(w.stash[worst]))
                        worst = i;
                gold += SalePrice(w.stash[worst]);
                Salvage(w, w.stash[worst]);
                ReturnGems(w.s, w.stash[worst]);
                w.stash.RemoveAt(worst);
            }
            return gold;
        }

        // ------------------------------------------------------------------ the player's own gear changes

        // Wears an item from the stash. Whatever it replaces goes back to the stash, and a two-handed weapon and a
        // shield push each other out.
        public static bool EquipFromStash(IdleWorld w, int index)
        {
            if (index < 0 || index >= w.stash.Count)
                return false;
            DaggerfallUnityItem item = w.stash[index];
            int slot = SlotFor(item);
            if (slot < 0)
                return false;
            w.stash.RemoveAt(index);
            if (w.gear[slot] != null)
                w.stash.Insert(index, w.gear[slot]);
            w.gear[slot] = item;

            int other = -1;
            if (slot == IdleData.SlotWeapon && ItemEquipTable.GetItemHands(item) == ItemHands.Both)
                other = IdleData.SlotShield;
            else if (slot == IdleData.SlotShield && w.gear[IdleData.SlotWeapon] != null
                && ItemEquipTable.GetItemHands(w.gear[IdleData.SlotWeapon]) == ItemHands.Both)
                other = IdleData.SlotWeapon;
            if (other >= 0 && w.gear[other] != null)
            {
                w.stash.Add(w.gear[other]);
                w.gear[other] = null;
            }
            RefreshBonus(w);
            return true;
        }

        public static bool Unequip(IdleWorld w, int slot)
        {
            if (slot < 0 || slot >= w.gear.Length || w.gear[slot] == null)
                return false;
            w.stash.Add(w.gear[slot]);
            w.gear[slot] = null;
            RefreshBonus(w);
            return true;
        }

        public static int SellFromStash(IdleWorld w, int index)
        {
            if (index < 0 || index >= w.stash.Count)
                return 0;
            int gold = SalePrice(w.stash[index]);
            Salvage(w, w.stash[index]);
            ReturnGems(w.s, w.stash[index]);
            w.stash.RemoveAt(index);
            w.s.gold += gold;
            w.s.goldEarned += gold;
            return gold;
        }

        // The shop: a fresh set of weapons and armor every time the hero comes home, better as the hero goes deeper.
        public static void Restock(IdleWorld w)
        {
            IdleSave s = w.s;
            int level = Mathf.Max(1, (s.level + s.deepestFloor + 1) / 2);
            w.shop.Clear();
            UnityEngine.Random.State keep = UnityEngine.Random.state;
            UnityEngine.Random.InitState(Next(s));
            try
            {
                for (int i = 0; i < IdleData.ShopSize; i++)
                {
                    DaggerfallUnityItem item = (i % 2 == 0)
                        ? ItemBuilder.CreateRandomWeapon(level)
                        : ItemBuilder.CreateRandomArmor(level, (Genders)s.gender, (Races)s.race);
                    if (item != null && SlotFor(item) >= 0)
                        w.shop.Add(Roll(s, 25) ? Affixed(w, item, 1, false) : item);
                }
            }
            finally
            {
                UnityEngine.Random.state = keep;
            }
        }

        public static int ShopPrice(DaggerfallUnityItem item)
        {
            return Mathf.Max(5, (int)((long)item.value * item.stackCount * WorthOf(item) / 100));
        }

        // Buys a shop item into the stash. Returns false when there is no gold or no room for it.
        public static bool Buy(IdleWorld w, int index)
        {
            if (index < 0 || index >= w.shop.Count || w.stash.Count >= IdleData.StashSize)
                return false;
            int price = ShopPrice(w.shop[index]);
            if (price > w.s.gold)
                return false;
            w.s.gold -= price;
            w.stash.Add(w.shop[index]);
            w.shop.RemoveAt(index);
            return true;
        }

        public static bool BuyPack(IdleWorld w)
        {
            IdleSave s = w.s;
            if (s.packLevel >= IdleData.PackLevels || IdleData.PackCost(s.packLevel) > s.gold)
                return false;
            s.gold -= IdleData.PackCost(s.packLevel);
            s.packLevel++;
            return true;
        }

        // Sets where the hero goes. In town it applies at once, otherwise from the next run.
        public static void SetDestination(IdleWorld w, int type, string name, int travelSeconds)
        {
            IdleSave s = w.s;
            s.nextType = type;
            s.nextName = name;
            s.nextTravel = travelSeconds;
            if (s.phase != IdleData.PhaseTown)
                return;
            s.targets[s.dungeonType] = s.targetFloor;
            s.dungeonType = type;
            s.destName = name;
            s.travelSeconds = Mathf.Max(5, travelSeconds);
            s.targetFloor = Mathf.Max(1, s.targets[type] > 0 ? s.targets[type] : 2);
            s.nextType = -1;
        }

        static void ArriveHome(IdleWorld w)
        {
            IdleSave s = w.s;
            s.expeditions++;
            s.phase = IdleData.PhaseTown;
            s.phaseLeft = 0;
            s.recall = 0;
            s.health = s.maxHealth;
            s.healCarry = 0;
            s.magicka = MaxMagicka(s);
            s.magickaCarry = 0;

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
                    if (old != null)
                        ReturnGems(s, old);         // the jeweller sets them again in the new piece
                    if (w.record)
                        w.Emit(IdleData.EvHome, "Now using " + Name(item));
                    item = old;
                }
                if (item == null)
                    continue;
                if (SlotFor(item) >= 0 && Works(item))
                    sold += Store(w, item);
                else
                {
                    sold += SalePrice(item);
                    Salvage(w, item);
                    ReturnGems(s, item);
                }
            }
            w.haul.Clear();

            int income = s.runGold + sold;
            s.gold += income;
            s.goldEarned += income;

            // Potions are bought before repairs: they keep the hero alive.
            int bought = 0;
            int price = IdleData.PotionPrice(s.level);
            while (s.potions < s.potionsToCarry && s.gold >= price)
            {
                s.gold -= price;
                s.potions++;
                bought++;
            }

            while (s.soulGemsEmpty < s.soulGemsToCarry && s.gold >= IdleData.SoulGemPrice * 3)
            {
                s.gold -= IdleData.SoulGemPrice;
                s.enchantGold += IdleData.SoulGemPrice;
                s.soulGemsEmpty++;
            }

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

            AutoForge(w);
            int smith = AutoSmith(w);
            smith += AutoJewel(w);
            smith += AutoEnchant(w);
            smith += AutoRank(w);
            AutoAlchemy(w);

            Restock(w);
            RefreshBonus(w);
            if (w.record)
                w.Emit(IdleData.EvHome, "Back in town: " + s.runKills + " kills, " + s.runGold + " gold found, loot sold for " + sold
                    + ", repairs " + repairs + ((smith > 0) ? ", smith " + smith : "") + ((bought > 0) ? ", " + bought + " potion(s) bought" : "") + ((equipped > 0) ? ", " + equipped + " upgrade(s)" : ""));
            s.carried = 0;
        }

        // ------------------------------------------------------------------ test harness

        public static string Summary(IdleWorld w)
        {
            IdleSave s = w.s;
            string gear = "";
            for (int i = 0; i < w.gear.Length; i++)
                if (w.gear[i] != null)
                    gear += (gear.Length > 0 ? ", " : "") + Name(w.gear[i]);
            return "level " + s.level + ", health " + s.maxHealth + ", gold " + s.gold + ", runs " + s.expeditions + " (cleared " + s.clears
                + ", retreated " + s.retreats + ", died " + s.deaths + "), kills " + s.kills + ", deepest floor " + s.deepestFloor
                + ", fled " + s.fled + ", champions " + s.championsKilled + ", bosses " + s.bossesKilled + ", potions " + s.potionsDrunk + ", spells " + s.spellsCast + "/" + s.enemySpellsCast + ", enchanted items " + s.affixesFound + ", levels bought " + s.levelsBought + ", reforges " + s.reforges + ", smith gold " + s.smithGold + ", veins " + s.veinsMined + ", ore " + s.oreMined + ", smelted " + s.ingotsSmelted + ", salvaged " + s.ingotsSalvaged + ", picks broken " + s.picksBroken + ", gems " + s.gemsFound + ", sockets added " + s.socketsAdded + ", gem upgrades " + s.gemUpgrades + ", jeweller gold " + s.jewelGold + ", souls " + s.soulsTrapped + ", enchanted " + s.enchantsAdded + ", enchanter gold " + s.enchantGold + ", " + IdleData.ClassNames[s.heroClass] + ", spell ranks gold " + s.spellRankGold + ", poisons " + s.poisonsKnown + " (" + s.poisonHits + " hits, alchemy gold " + s.alchemyGold + ")" + ", " + PickName(s) + ", hearts " + s.hearts + ", left behind " + s.leftBehind + ", time " + Hours(s) + " h, gold earned per hour "
                + (long)(s.goldEarned / Mathf.Max(0.05f, s.clock / 36000f)) + " | " + gear;
        }

        static string Hours(IdleSave s)
        {
            return (s.clock / 36000f).ToString("0.0");
        }

        // Runs a fresh hero through a number of expeditions without touching the real save.
        public static IdleWorld Simulate(int seed, int expeditions, bool deeper)
        {
            return Simulate(seed, expeditions, deeper, -1);
        }

        public static IdleWorld Simulate(int seed, int expeditions, bool deeper, int dungeonType)
        {
            return Simulate(seed, expeditions, deeper, dungeonType, IdleData.ClassWarrior);
        }

        public static IdleWorld Simulate(int seed, int expeditions, bool deeper, int dungeonType, int heroClass)
        {
            IdleWorld w = NewHero(seed, "Test", heroClass);
            if (dungeonType >= 0)
                w.s.dungeonType = dungeonType;
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
