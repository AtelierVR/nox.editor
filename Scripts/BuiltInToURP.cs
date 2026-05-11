using UnityEditor;
using UnityEngine;

namespace Nox.Editor
{
    public static class BuiltInToUrp
    {
        [MenuItem("Nox/Tools/Convert Materials Built-In to URP")]
        public static void ConvertBuiltInToUrp()
        {
            Undo.SetCurrentGroupName("Convert Materials Built-In to URP");
            var group = Undo.GetCurrentGroup();
            
            var guids = AssetDatabase.FindAssets("t:Material", null);
            var count = 0;
            
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);

                if (!material || !material.shader.name.StartsWith("Standard")) continue;
                
                Undo.RecordObject(material, "Convert Built-In to URP");
                
                // Sauvegarder les valeurs avant changement de shader
                var renderQueue = material.renderQueue;
                var mode = material.GetFloat("_Mode");
                var srcBlend = material.GetInt("_SrcBlend");
                var dstBlend = material.GetInt("_DstBlend");
                var zWrite = material.GetInt("_ZWrite");
                
                // Changer le shader
                material.shader = Shader.Find("Universal Render Pipeline/Lit");
                
                // Configurer le mode de surface (Opaque/Transparent)
                if (mode == 0) // Opaque
                {
                    material.SetFloat("_Surface", 0);
                    material.SetFloat("_Blend", 0);
                    material.SetFloat("_AlphaClip", 0);
                    material.SetInt("_SrcBlend", 1);
                    material.SetInt("_DstBlend", 0);
                    material.SetInt("_ZWrite", 1);
                    material.renderQueue = 2000;
                }
                else if (mode == 1) // Cutout
                {
                    material.SetFloat("_Surface", 0);
                    material.SetFloat("_Blend", 0);
                    material.SetFloat("_AlphaClip", 1);
                    material.SetInt("_SrcBlend", 1);
                    material.SetInt("_DstBlend", 0);
                    material.SetInt("_ZWrite", 1);
                    material.renderQueue = 2450;
                }
                else if (mode == 2 || mode == 3) // Fade ou Transparent
                {
                    material.SetFloat("_Surface", 1);
                    material.SetFloat("_Blend", 0); // Alpha blending
                    material.SetFloat("_AlphaClip", 0);
                    material.SetInt("_SrcBlend", srcBlend);
                    material.SetInt("_DstBlend", dstBlend);
                    material.SetInt("_ZWrite", zWrite);
                    material.renderQueue = renderQueue;
                }
                
                material.SetInt("_SrcBlendAlpha", 1);
                material.SetInt("_DstBlendAlpha", 10);
                material.SetFloat("_Cull", 2);
                material.SetFloat("_AlphaToMask", 0);
                
                // Copier les couleurs et textures
                if (material.HasProperty("_Color"))
                    material.SetColor("_BaseColor", material.GetColor("_Color"));
                    
                if (material.HasProperty("_MainTex"))
                {
                    var mainTex = material.GetTexture("_MainTex");
                    material.SetTexture("_BaseMap", mainTex);
                    if (mainTex != null)
                    {
                        material.SetTextureScale("_BaseMap", material.GetTextureScale("_MainTex"));
                        material.SetTextureOffset("_BaseMap", material.GetTextureOffset("_MainTex"));
                    }
                }
                
                if (material.HasProperty("_Cutoff"))
                    material.SetFloat("_Cutoff", material.GetFloat("_Cutoff"));
                    
                if (material.HasProperty("_Glossiness"))
                    material.SetFloat("_Smoothness", material.GetFloat("_Glossiness"));
                    
                if (material.HasProperty("_Metallic"))
                    material.SetFloat("_Metallic", material.GetFloat("_Metallic"));
                    
                if (material.HasProperty("_MetallicGlossMap"))
                {
                    var metallicMap = material.GetTexture("_MetallicGlossMap");
                    material.SetTexture("_MetallicGlossMap", metallicMap);
                    if (metallicMap != null)
                    {
                        material.SetFloat("_SmoothnessTextureChannel", 0);
                        material.EnableKeyword("_METALLICSPECGLOSSMAP");
                    }
                }
                
                if (material.HasProperty("_BumpMap"))
                {
                    var bumpMap = material.GetTexture("_BumpMap");
                    material.SetTexture("_BumpMap", bumpMap);
                    if (bumpMap != null)
                    {
                        material.EnableKeyword("_NORMALMAP");
                        if (material.HasProperty("_BumpScale"))
                            material.SetFloat("_BumpScale", material.GetFloat("_BumpScale"));
                    }
                }
                
                if (material.HasProperty("_EmissionColor"))
                {
                    var emissionColor = material.GetColor("_EmissionColor");
                    material.SetColor("_EmissionColor", emissionColor);
                    
                    // Activer l'émission si la couleur n'est pas noire
                    if (emissionColor.r > 0 || emissionColor.g > 0 || emissionColor.b > 0)
                    {
                        material.EnableKeyword("_EMISSION");
                        material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                    }
                }
                
                if (material.HasProperty("_EmissionMap"))
                {
                    var emissionMap = material.GetTexture("_EmissionMap");
                    material.SetTexture("_EmissionMap", emissionMap);
                    if (emissionMap != null)
                    {
                        material.EnableKeyword("_EMISSION");
                        material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                    }
                }
                
                if (material.HasProperty("_OcclusionMap"))
                {
                    var occlusionMap = material.GetTexture("_OcclusionMap");
                    material.SetTexture("_OcclusionMap", occlusionMap);
                    if (occlusionMap != null)
                    {
                        material.EnableKeyword("_OCCLUSIONMAP");
                        if (material.HasProperty("_OcclusionStrength"))
                            material.SetFloat("_OcclusionStrength", material.GetFloat("_OcclusionStrength"));
                    }
                }
                
                // Propriétés supplémentaires
                material.SetFloat("_ReceiveShadows", 1);
                material.SetFloat("_SpecularHighlights", 1);
                material.SetFloat("_EnvironmentReflections", 1);
                material.SetFloat("_QueueOffset", 0);
                
                EditorUtility.SetDirty(material);
                count++;
            }
            
            if (count > 0)
                AssetDatabase.SaveAssets();
            Undo.CollapseUndoOperations(group);
            UnityEngine.Debug.Log($"Converted {count} materials from Built-In to URP.");
        }
    }
}