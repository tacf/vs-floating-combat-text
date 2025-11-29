using System.Collections.Generic;
using FloatingCombatText.Utils;
using Vintagestory.API.Client;
using Vintagestory.API.Common;


namespace FloatingCombatText;

#nullable disable
public class FloatingCombatTextModSystem : ModSystem
{
    private static string _configFile;

    public override void Start(ICoreAPI api)
    {
        api.World.Logger.Event($" started (Version: {Mod.Info.Version})"); 
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        Logger.Init(this, api.Logger);
        _configFile = $"{Mod.Info.Name}.json".Replace(" ", "");

        ModConfig.Instance = api.LoadModConfig<ModConfig>(_configFile) ?? new ModConfig();
        api.StoreModConfig(ModConfig.Instance, _configFile);
        List<IFeature> features =
        [
            new FloatingDamageText(api),
        ];
        LoadFeatures(features);
    }
    
    private void LoadFeatures(List<IFeature> features)
    {
        foreach (IFeature feature in features)
        {
            if (!feature.Initialize())
                Logger.Log($"Feature {feature.GetType().Name} not loaded");
            else
                Logger.Log($"Loaded feature {feature.GetType().Name}");
        }
        Logger.Log(" Finished server initialization");
    }
}