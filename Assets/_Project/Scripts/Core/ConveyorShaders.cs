using UnityEngine;

namespace IndustrialConveyor.Simulation.Core
{
    /// <summary>Resolves the lit shader in a way that survives player build stripping.</summary>
    public static class ConveyorShaders
    {
        /// <summary>Gets the URP Lit shader, falling back to Standard; null if neither is available.</summary>
        public static Shader Lit
        {
            get
            {
                // Shader.Find alone returns null in builds; the Resources material pins the shader into the player.
                var pinned = Resources.Load<Material>("ConveyorLit");
                if (pinned != null && pinned.shader != null)
                    return pinned.shader;
                return Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            }
        }
    }
}
