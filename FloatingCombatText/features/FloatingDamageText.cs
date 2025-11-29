using System;
using System.Collections.Generic;
using System.Linq;
using FloatingCombatText.Utils;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;


#nullable disable

namespace FloatingCombatText;

public class FloatingDamageText : IFeature
{
    private static ICoreClientAPI _capi;
    public static Dictionary<Entity, List<CombatTextEntry>> CombatTexts = new();
    private static readonly Dictionary<long, float> EntityHealthCache = new();
    private long _healthCheckTickId;

    private const int HealthCheckIntervalMs = 100;
    public const float LifetimeSeconds = 2f;

    public FloatingDamageText(ICoreClientAPI api)
    {
        _capi = api;
    }

    public EnumAppSide Side => EnumAppSide.Client;

    public bool Initialize()
    {
        _capi.Event.RegisterRenderer(new DamageTextRenderer(_capi), EnumRenderStage.Ortho);
        _healthCheckTickId = _capi.Event.RegisterGameTickListener(OnHealthCheckTick, HealthCheckIntervalMs);
        return true;
    }

    public void Teardown()
    {
        _capi.Event.UnregisterGameTickListener(_healthCheckTickId);
        CombatTexts.Clear();
        EntityHealthCache.Clear();
    }

    private void OnHealthCheckTick(float dt)
    {
        EntityPlayer player = _capi.World.Player?.Entity;
        if (player == null) return;

        // Check entities near the player
        Entity[] entities = _capi.World.GetEntitiesAround(
            player.Pos.XYZ,
            ModConfig.Instance.CombatTextRange, ModConfig.Instance.CombatTextRange,
            e => e is EntityAgent && e.Alive
        );

        // CleanUp
        foreach (KeyValuePair<Entity, List<CombatTextEntry>> combatText in CombatTexts)
        {
            if (entities.Any(e => e == combatText.Key))
            {
                List<CombatTextEntry> toDispose = combatText.Value.Where(entry => entry.GetAge() > LifetimeSeconds).ToList();

                foreach (CombatTextEntry entry in toDispose)
                {
                    entry.Dispose();
                    CombatTexts[combatText.Key].Remove(entry);
                }
            }
            else
            {
                EntityHealthCache.Remove(combatText.Key.EntityId);
                foreach (CombatTextEntry entry in combatText.Value.ToList())
                {
                    entry.Dispose();
                }
                CombatTexts[combatText.Key].Clear();
                CombatTexts.Remove(combatText.Key);
            }
        }


        // Check for health changes
        foreach (Entity entity in entities)
        {
            ITreeAttribute healthTree = entity.WatchedAttributes.GetTreeAttribute("health");
            if (healthTree == null) continue;

            float currentHealth = healthTree.GetFloat("currenthealth", 0);
            long entityId = entity.EntityId;

            if (EntityHealthCache.TryGetValue(entityId, out float previousHealth))
            {
                float deltaHealth = previousHealth - currentHealth;
                int color = DamageTextRenderer.DefaultDamageColor;
                switch (deltaHealth)
                {
                    case >= 0.1f:
                        break; // Health decreased = damage taken
                    case <= -0.1f:
                        color = DamageTextRenderer.DefaultHealingColor;
                        break; // Health increase = health receive
                    default:
                        continue;
                }

                float entityHeight = entity.SelectionBox?.Y2 ?? entity.CollisionBox?.Y2 ?? 1.0f;
                Vec3d pos = entity.Pos.XYZ.Add(0, entityHeight, 0);

                CombatTextEntry entry = new()
                {
                    Position = pos.Clone(),
                    AbsDeltaHealth = Math.Abs(deltaHealth),
                    SpawnTime = _capi.ElapsedMilliseconds,
                    Color = color
                };

                if (CombatTexts.TryGetValue(entity, out List<CombatTextEntry> value))
                {
                    value.Add(entry);
                }
                else
                {
                    CombatTexts.Add(entity, [entry]);
                }
            }

            EntityHealthCache[entityId] = currentHealth;
        }
    }

    public static long GetElapsedTime()
    {
        return _capi.ElapsedMilliseconds;
    }

    public class CombatTextEntry
    {
        public Vec3d Position { get; init; }
        public float AbsDeltaHealth { get; init; }
        public long SpawnTime { get; set; }
        public int Color { get; init; }
        
        public LoadedTexture Texture { get; set; }

        public float GetAge() 
        {
           return (GetElapsedTime() - SpawnTime) / 1000f;
        }

        public void Dispose()
        {
            Texture?.Dispose();
        }
    }
}