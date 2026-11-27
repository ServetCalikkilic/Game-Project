using System;

namespace Rts.Core
{
    /// <summary>Effective unit stats after the owner's technologies.</summary>
    public struct UnitStats
    {
        public float Damage, Range;
        public int ArmorMelee, ArmorPierce;
    }

    public static class CombatRules
    {
        static bool IsMeleeInfantryOrCavalry(UnitType u) => u == UnitType.Spearman || u == UnitType.Guardian || u == UnitType.Raider;

        /// <summary>Technology effects from rag/knowledge/technologies.json applied to one unit type.</summary>
        public static UnitStats Effective(UnitType u, PlayerState owner)
        {
            var d = GameData.Units[u];
            var s = new UnitStats { Damage = d.Damage, Range = d.Range, ArmorMelee = d.ArmorMelee, ArmorPierce = d.ArmorPierce };
            if (owner == null) return s;
            if (u == UnitType.Archer && owner.Has(TechId.LongbowTraining)) { s.Range += 1; s.Damage += 2; }
            if (IsMeleeInfantryOrCavalry(u) && owner.Has(TechId.ForgedBlades)) s.Damage *= 1.2f;
            if ((IsMeleeInfantryOrCavalry(u) || u == UnitType.Ram) && owner.Has(TechId.ReinforcedPlating)) s.ArmorPierce += 1;
            return s;
        }

        /// <summary>Damage of one hit: (base + bonus vs target, x1.2 with Forged Blades) minus the target's armor for that damage type, min 1.
        /// Siege ignores armor against buildings (Rams are the intended building killers).</summary>
        public static float HitDamage(Entity attacker, PlayerState attackerOwner, Entity target, PlayerState targetOwner)
        {
            var def = GameData.Units[attacker.Unit];
            var stats = Effective(attacker.Unit, attackerOwner);
            float raw = stats.Damage;
            int armor;
            if (target.Kind == EntityKind.Building)
            {
                raw += def.BonusVsBuilding;
                armor = def.DamageType == DamageType.Siege ? 0
                      : def.DamageType == DamageType.Pierce ? GameData.BuildingArmorPierce : GameData.BuildingArmorMelee;
            }
            else
            {
                if (def.BonusVsUnit.TryGetValue(target.Unit, out int bonus)) raw += bonus;
                var t = Effective(target.Unit, targetOwner);
                armor = def.DamageType == DamageType.Pierce ? t.ArmorPierce : t.ArmorMelee;
            }
            return Math.Max(1f, raw - armor);
        }
    }
}
