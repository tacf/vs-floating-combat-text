using System;
using Vintagestory.API.Common;

namespace FloatingCombatText;

public interface IFeature 
{
    public EnumAppSide Side { get; }
    public bool Initialize();
    
    public void Teardown();
}