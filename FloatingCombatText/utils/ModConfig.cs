namespace FloatingCombatText.Utils;

class ModConfig
{
    public static ModConfig Instance { get; set; } = new ModConfig();

    /// <summary>
    /// Combat Text display range (0-100, default: 30)
    /// </summary>
    public int CombatTextRange { get; set; } = 30;
    
}