using UnityEngine;

namespace Mimas.Client.Presentation
{
    /// <summary>
    /// The material the placeholder primitives should wear.
    ///
    /// <para><c>GameObject.CreatePrimitive</c> leaves Unity's built-in <c>Default-Material</c> on whatever
    /// it makes, and that material's shader belongs to the Built-in render pipeline. Under URP it has no
    /// valid pass, so every unit and prop draws in the magenta error colour — which is exactly how the
    /// first Web build came out. Assigning a URP material instead removes the dependency on whatever
    /// <c>CreatePrimitive</c> happens to default to.</para>
    ///
    /// <para>Built once and shared: the tints are per-renderer <c>MaterialPropertyBlock</c>s, so one
    /// material serves every placeholder without breaking batching.</para>
    /// </summary>
    public static class PlaceholderMaterial
    {
        private const string ShaderName = "Universal Render Pipeline/Lit";

        private static Material _shared;
        private static bool _complained;

        /// <summary>The shared placeholder material, or null if URP's Lit shader is missing.</summary>
        public static Material Shared
        {
            get
            {
                if (_shared != null) return _shared;

                Shader shader = Shader.Find(ShaderName);
                if (shader == null)
                {
                    // Shader.Find only reaches shaders the build actually included. WebBuild adds this
                    // one to Always Included Shaders for exactly that reason.
                    if (!_complained)
                    {
                        _complained = true;
                        Debug.LogError("[PlaceholderMaterial] '" + ShaderName
                            + "' not found; placeholders will draw magenta.");
                    }
                    return null;
                }

                _shared = new Material(shader) { name = "Placeholder (runtime)" };
                return _shared;
            }
        }

        /// <summary>Puts the placeholder material on a renderer that has nothing better.</summary>
        public static void Apply(GameObject go)
        {
            if (go == null) return;

            Renderer renderer = go.GetComponent<Renderer>();
            if (renderer == null) return;

            Material material = Shared;
            if (material != null) renderer.sharedMaterial = material;
        }
    }
}
