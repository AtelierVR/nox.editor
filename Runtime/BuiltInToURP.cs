#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Nox.Editor
{
    public static class BuiltInToUrp
    {
        // ── Context Menu (Material Inspector) ──────────────────────────────────

        [MenuItem("CONTEXT/Material/Convert/To URP", false, 500)]
        public static void ConvertMaterialContextMenu(MenuCommand command)
        {
            if (command.context is not Material material) return;

            Undo.RecordObject(material, "Convert to URP");
            if (ConvertMaterial(material))
            {
                EditorUtility.SetDirty(material);
                AssetDatabase.SaveAssets();
                Debug.Log($"Converted material '{material.name}' to URP.");
            }
        }

        [MenuItem("CONTEXT/Material/Convert/To URP", true)]
        public static bool ValidateConvertMaterialContextMenu(MenuCommand command)
        {
            return command.context is Material material && IsConvertibleToUrp(material);
        }

        // ── Assets Context Menu (Project Window) ───────────────────────────────

        [MenuItem("Assets/Convert/To URP", false, 2000)]
        public static void ConvertSelectedMaterials()
        {
            var materials = Selection.GetFiltered<Material>(SelectionMode.DeepAssets);
            if (materials == null || materials.Length == 0) return;

            Undo.SetCurrentGroupName("Convert Materials to URP");
            var group = Undo.GetCurrentGroup();

            var count = 0;
            foreach (var mat in materials)
            {
                if (mat == null || !IsConvertibleToUrp(mat)) continue;

                Undo.RecordObject(mat, "Convert to URP");
                if (ConvertMaterial(mat))
                {
                    EditorUtility.SetDirty(mat);
                    count++;
                }
            }

            if (count > 0)
                AssetDatabase.SaveAssets();

            Undo.CollapseUndoOperations(group);
            Debug.Log($"Converted {count} material(s) to URP.");
        }

        [MenuItem("Assets/Convert/To URP", true)]
        public static bool ValidateConvertSelectedMaterials()
        {
            var materials = Selection.GetFiltered<Material>(SelectionMode.DeepAssets);
            if (materials == null || materials.Length == 0) return false;

            foreach (var mat in materials)
            {
                if (mat != null && IsConvertibleToUrp(mat))
                    return true;
            }

            return false;
        }

        // ── Tools Menu (All Materials in Project) ──────────────────────────────

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

                if (material == null || !IsConvertibleToUrp(material)) continue;

                Undo.RecordObject(material, "Convert Built-In to URP");
                if (ConvertMaterial(material))
                {
                    EditorUtility.SetDirty(material);
                    count++;
                }
            }

            if (count > 0)
                AssetDatabase.SaveAssets();

            Undo.CollapseUndoOperations(group);
            Debug.Log($"Converted {count} materials from Built-In to URP.");
        }

        // ── Validation & Conversion Logic ──────────────────────────────────────

        public static bool IsConvertibleToUrp(Material material)
        {
            if (material == null) return false;
            if (material.shader == null) return true;

            var shaderName = material.shader.name;
            if (string.IsNullOrEmpty(shaderName)) return true;
            if (shaderName == "Hidden/InternalErrorShader") return true;

            // Check if already URP
            if (shaderName.StartsWith("Universal Render Pipeline/") ||
                shaderName.StartsWith("URP/") ||
                shaderName.StartsWith("Shader Graphs/"))
            {
                return false;
            }

            return true;
        }

        public static bool ConvertMaterial(Material material)
        {
            if (material == null) return false;

            var originalShader = material.shader;
            var originalShaderName = originalShader != null ? originalShader.name : string.Empty;

            // Determine Target Shader
            Shader targetShader;
            if (originalShaderName.StartsWith("Unlit/") || originalShaderName.StartsWith("Legacy Shaders/Unlit") || originalShaderName == "Unlit")
            {
                targetShader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Universal Render Pipeline/Lit");
            }
            else if (originalShaderName.StartsWith("Particles/"))
            {
                if (originalShaderName.Contains("Lit") || originalShaderName.Contains("Surface"))
                    targetShader = Shader.Find("Universal Render Pipeline/Particles/Lit") ?? Shader.Find("Universal Render Pipeline/Lit");
                else
                    targetShader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Universal Render Pipeline/Lit");
            }
            else if (originalShaderName.StartsWith("Sprites/"))
            {
                targetShader = Shader.Find("Universal Render Pipeline/2D/Sprite-Lit-Default") ??
                               Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default") ??
                               Shader.Find("Universal Render Pipeline/Lit");
            }
            else
            {
                targetShader = Shader.Find("Universal Render Pipeline/Lit");
            }

            if (targetShader == null)
            {
                Debug.LogError($"[BuiltInToUrp] Failed to find target URP shader for material '{material.name}'.");
                return false;
            }

            var isTargetLit = targetShader.name == "Universal Render Pipeline/Lit";
            var isTargetUnlit = targetShader.name == "Universal Render Pipeline/Unlit";

            // 1. Cache values before changing shader
            var oldRenderQueue = material.renderQueue;
            var mode = material.HasProperty("_Mode")
                ? material.GetFloat("_Mode")
                : (material.HasProperty("_BlendMode") ? material.GetFloat("_BlendMode") : -1f);
            var srcBlend = material.HasProperty("_SrcBlend") ? material.GetInt("_SrcBlend") : 1;
            var dstBlend = material.HasProperty("_DstBlend") ? material.GetInt("_DstBlend") : 0;
            var zWrite = material.HasProperty("_ZWrite") ? material.GetInt("_ZWrite") : 1;
            var cull = material.HasProperty("_Cull") ? material.GetFloat("_Cull") : 2f;
            var cutoff = material.HasProperty("_Cutoff")
                ? material.GetFloat("_Cutoff")
                : (material.HasProperty("_AlphaCutoff") ? material.GetFloat("_AlphaCutoff") : 0.5f);

            // Color
            var baseColor = Color.white;
            var hasBaseColor = false;
            if (material.HasProperty("_BaseColor")) { baseColor = material.GetColor("_BaseColor"); hasBaseColor = true; }
            else if (material.HasProperty("_Color")) { baseColor = material.GetColor("_Color"); hasBaseColor = true; }
            else if (material.HasProperty("_TintColor")) { baseColor = material.GetColor("_TintColor"); hasBaseColor = true; }
            else if (material.HasProperty("_MainColor")) { baseColor = material.GetColor("_MainColor"); hasBaseColor = true; }

            // Base Map / Texture
            Texture mainTex = null;
            var mainTexScale = Vector2.one;
            var mainTexOffset = Vector2.zero;
            var hasMainTex = false;
            if (material.HasProperty("_MainTex") && material.GetTexture("_MainTex") != null)
            {
                mainTex = material.GetTexture("_MainTex");
                mainTexScale = material.GetTextureScale("_MainTex");
                mainTexOffset = material.GetTextureOffset("_MainTex");
                hasMainTex = true;
            }
            else if (material.HasProperty("_BaseMap") && material.GetTexture("_BaseMap") != null)
            {
                mainTex = material.GetTexture("_BaseMap");
                mainTexScale = material.GetTextureScale("_BaseMap");
                mainTexOffset = material.GetTextureOffset("_BaseMap");
                hasMainTex = true;
            }
            else if (material.HasProperty("_BaseColorMap") && material.GetTexture("_BaseColorMap") != null)
            {
                mainTex = material.GetTexture("_BaseColorMap");
                mainTexScale = material.GetTextureScale("_BaseColorMap");
                mainTexOffset = material.GetTextureOffset("_BaseColorMap");
                hasMainTex = true;
            }

            // Smoothness / Shininess / Glossiness
            var smoothness = 0.5f;
            if (material.HasProperty("_Glossiness"))
            {
                var useSmoothness = material.HasProperty("_UseSmoothness") && material.GetFloat("_UseSmoothness") > 0.5f;
                var gloss = material.GetFloat("_Glossiness");
                smoothness = useSmoothness ? gloss : (1.0f - gloss);
            }
            else if (material.HasProperty("_Glossiness")) smoothness = material.GetFloat("_Glossiness");
            else if (material.HasProperty("_Smoothness")) smoothness = material.GetFloat("_Smoothness");
            else if (material.HasProperty("_GlossMapScale")) smoothness = material.GetFloat("_GlossMapScale");
            else if (material.HasProperty("_Shininess")) smoothness = material.GetFloat("_Shininess");

            var smoothnessChannel = material.HasProperty("_SmoothnessTextureChannel") ? material.GetFloat("_SmoothnessTextureChannel") : 0f;

            // Metallic / Specular Workflow
            var isSpecularWorkflow = originalShaderName.Contains("Specular") ||
                                     (material.HasProperty("_SpecGlossMap") && material.GetTexture("_SpecGlossMap") != null);

            var metallic = material.HasProperty("_Metallic") ? material.GetFloat("_Metallic") : 0f;
            var metallicGlossMap = material.HasProperty("_MetallicGlossMap") ? material.GetTexture("_MetallicGlossMap") : null;

            var specColor = material.HasProperty("_SpecColor") ? material.GetColor("_SpecColor") : new Color(0.2f, 0.2f, 0.2f, 1f);
            var specGlossMap = material.HasProperty("_SpecGlossMap") ? material.GetTexture("_SpecGlossMap") : null;

            // Normal Map
            Texture bumpMap = null;
            var bumpScaleSt = Vector2.one;
            var bumpOffsetSt = Vector2.zero;
            if (material.HasProperty("_BumpMap") && material.GetTexture("_BumpMap") != null)
            {
                bumpMap = material.GetTexture("_BumpMap");
                bumpScaleSt = material.GetTextureScale("_BumpMap");
                bumpOffsetSt = material.GetTextureOffset("_BumpMap");
            }
            else if (material.HasProperty("_NormalMap") && material.GetTexture("_NormalMap") != null)
            {
                bumpMap = material.GetTexture("_NormalMap");
                bumpScaleSt = material.GetTextureScale("_NormalMap");
                bumpOffsetSt = material.GetTextureOffset("_NormalMap");
            }
            var bumpScale = material.HasProperty("_BumpScale")
                ? material.GetFloat("_BumpScale")
                : (material.HasProperty("_NormalScale") ? material.GetFloat("_NormalScale") : 1f);

            // Parallax Map
            Texture parallaxMap = null;
            if (material.HasProperty("_ParallaxMap") && material.GetTexture("_ParallaxMap") != null)
                parallaxMap = material.GetTexture("_ParallaxMap");
            else if (material.HasProperty("_HeightMap") && material.GetTexture("_HeightMap") != null)
                parallaxMap = material.GetTexture("_HeightMap");
            var parallax = material.HasProperty("_Parallax") ? material.GetFloat("_Parallax") : 0.02f;

            // Occlusion Map
            Texture occlusionMap = null;
            if (material.HasProperty("_OcclusionMap") && material.GetTexture("_OcclusionMap") != null)
                occlusionMap = material.GetTexture("_OcclusionMap");
            else if (material.HasProperty("_AOMap") && material.GetTexture("_AOMap") != null)
                occlusionMap = material.GetTexture("_AOMap");
            else if (material.HasProperty("_PackedMap") && material.GetTexture("_PackedMap") != null)
                occlusionMap = material.GetTexture("_PackedMap");
            var occlusionStrength = material.HasProperty("_OcclusionStrength") ? material.GetFloat("_OcclusionStrength") : 1f;

            // Emission
            var emissionColor = Color.black;
            var hasEmissionColor = false;
            if (material.HasProperty("_EmissionColor")) { emissionColor = material.GetColor("_EmissionColor"); hasEmissionColor = true; }
            else if (material.HasProperty("_EmissiveColor")) { emissionColor = material.GetColor("_EmissiveColor"); hasEmissionColor = true; }

            if (material.HasProperty("_EmissionIntensity"))
            {
                emissionColor *= material.GetFloat("_EmissionIntensity");
            }

            Texture emissionMap = null;
            var emissionScale = Vector2.one;
            var emissionOffset = Vector2.zero;
            if (material.HasProperty("_EmissionMap") && material.GetTexture("_EmissionMap") != null)
            {
                emissionMap = material.GetTexture("_EmissionMap");
                emissionScale = material.GetTextureScale("_EmissionMap");
                emissionOffset = material.GetTextureOffset("_EmissionMap");
            }
            else if (material.HasProperty("_EmissiveMap") && material.GetTexture("_EmissiveMap") != null)
            {
                emissionMap = material.GetTexture("_EmissiveMap");
                emissionScale = material.GetTextureScale("_EmissiveMap");
                emissionOffset = material.GetTextureOffset("_EmissiveMap");
            }

            // Detail Maps
            var detailMask = material.HasProperty("_DetailMask") ? material.GetTexture("_DetailMask") : null;
            var detailAlbedoMap = material.HasProperty("_DetailAlbedoMap") ? material.GetTexture("_DetailAlbedoMap") : null;
            var detailAlbedoScale = (detailAlbedoMap != null && material.HasProperty("_DetailAlbedoMap")) ? material.GetTextureScale("_DetailAlbedoMap") : Vector2.one;
            var detailAlbedoOffset = (detailAlbedoMap != null && material.HasProperty("_DetailAlbedoMap")) ? material.GetTextureOffset("_DetailAlbedoMap") : Vector2.zero;
            var detailNormalMap = material.HasProperty("_DetailNormalMap") ? material.GetTexture("_DetailNormalMap") : null;
            var detailNormalScale = material.HasProperty("_DetailNormalMapScale") ? material.GetFloat("_DetailNormalMapScale") : 1f;
            var uvSec = material.HasProperty("_UVSec") ? material.GetFloat("_UVSec") : 0f;

            // Highlights & Reflections
            var specHighlights = material.HasProperty("_SpecularHighlights") ? material.GetFloat("_SpecularHighlights") : 1f;
            var envReflections = material.HasProperty("_EnvironmentReflections")
                ? material.GetFloat("_EnvironmentReflections")
                : (material.HasProperty("_GlossyReflections") ? material.GetFloat("_GlossyReflections") : 1f);

            // 2. Determine Surface & Blend modes
            var surfaceType = 0; // 0 = Opaque, 1 = Transparent
            var blendMode = 0;   // 0 = Alpha, 1 = Premultiply, 2 = Additive, 3 = Multiply
            var alphaClip = 0;   // 0 = Off, 1 = On
            var finalSrcBlend = 1;
            var finalDstBlend = 0;
            var finalZWrite = 1;
            var targetRenderQueue = 2000;

            if (mode >= 0)
            {
                if (mode == 0) // Opaque
                {
                    surfaceType = 0;
                    blendMode = 0;
                    alphaClip = 0;
                    finalSrcBlend = 1;
                    finalDstBlend = 0;
                    finalZWrite = 1;
                    targetRenderQueue = (oldRenderQueue > 0 && oldRenderQueue != 2000) ? oldRenderQueue : 2000;
                }
                else if (mode == 1) // Cutout
                {
                    surfaceType = 0;
                    blendMode = 0;
                    alphaClip = 1;
                    finalSrcBlend = 1;
                    finalDstBlend = 0;
                    finalZWrite = 1;
                    targetRenderQueue = (oldRenderQueue > 0 && oldRenderQueue != 2000) ? oldRenderQueue : 2450;
                }
                else if (mode == 2) // Fade
                {
                    surfaceType = 1;
                    blendMode = 0;
                    alphaClip = 0;
                    finalSrcBlend = srcBlend != 0 ? srcBlend : (int)BlendMode.SrcAlpha;
                    finalDstBlend = dstBlend != 0 ? dstBlend : (int)BlendMode.OneMinusSrcAlpha;
                    finalZWrite = zWrite;
                    targetRenderQueue = (oldRenderQueue > 0 && oldRenderQueue != 2000) ? oldRenderQueue : 3000;
                }
                else if (mode == 3) // Transparent
                {
                    surfaceType = 1;
                    blendMode = 1;
                    alphaClip = 0;
                    finalSrcBlend = srcBlend != 0 ? srcBlend : (int)BlendMode.One;
                    finalDstBlend = dstBlend != 0 ? dstBlend : (int)BlendMode.OneMinusSrcAlpha;
                    finalZWrite = zWrite;
                    targetRenderQueue = (oldRenderQueue > 0 && oldRenderQueue != 2000) ? oldRenderQueue : 3000;
                }
            }
            else
            {
                var isCutout = originalShaderName.Contains("Cutout") || originalShaderName.Contains("AlphaTest") || material.IsKeywordEnabled("_ALPHATEST_ON");
                var isTransparent = originalShaderName.Contains("Transparent") || originalShaderName.Contains("Fade") || originalShaderName.Contains("Alpha") || oldRenderQueue >= 3000;
                var isAdditive = originalShaderName.Contains("Additive");

                if (isCutout)
                {
                    surfaceType = 0;
                    alphaClip = 1;
                    finalSrcBlend = 1;
                    finalDstBlend = 0;
                    finalZWrite = 1;
                    targetRenderQueue = oldRenderQueue > 0 ? oldRenderQueue : 2450;
                }
                else if (isAdditive)
                {
                    surfaceType = 1;
                    blendMode = 2;
                    alphaClip = 0;
                    finalSrcBlend = (int)BlendMode.SrcAlpha;
                    finalDstBlend = (int)BlendMode.One;
                    finalZWrite = 0;
                    targetRenderQueue = oldRenderQueue > 0 ? oldRenderQueue : 3000;
                }
                else if (isTransparent)
                {
                    surfaceType = 1;
                    blendMode = 0;
                    alphaClip = 0;
                    finalSrcBlend = srcBlend != 0 ? srcBlend : (int)BlendMode.SrcAlpha;
                    finalDstBlend = dstBlend != 0 ? dstBlend : (int)BlendMode.OneMinusSrcAlpha;
                    finalZWrite = 0;
                    targetRenderQueue = oldRenderQueue > 0 ? oldRenderQueue : 3000;
                }
                else
                {
                    surfaceType = 0;
                    alphaClip = 0;
                    finalSrcBlend = 1;
                    finalDstBlend = 0;
                    finalZWrite = 1;
                    targetRenderQueue = (oldRenderQueue > 0 && oldRenderQueue != 2000) ? oldRenderQueue : 2000;
                }
            }

            // 3. Assign new shader
            material.shader = targetShader;

            // 4. Set surface & blending properties
            if (material.HasProperty("_Surface")) material.SetFloat("_Surface", surfaceType);
            if (material.HasProperty("_Blend")) material.SetFloat("_Blend", blendMode);
            if (material.HasProperty("_AlphaClip")) material.SetFloat("_AlphaClip", alphaClip);
            if (material.HasProperty("_SrcBlend")) material.SetInt("_SrcBlend", finalSrcBlend);
            if (material.HasProperty("_DstBlend")) material.SetInt("_DstBlend", finalDstBlend);
            if (material.HasProperty("_SrcBlendAlpha")) material.SetInt("_SrcBlendAlpha", 1);
            if (material.HasProperty("_DstBlendAlpha")) material.SetInt("_DstBlendAlpha", 10);
            if (material.HasProperty("_ZWrite")) material.SetInt("_ZWrite", finalZWrite);
            if (material.HasProperty("_Cull")) material.SetFloat("_Cull", cull);
            if (material.HasProperty("_AlphaToMask")) material.SetFloat("_AlphaToMask", 0f);
            if (material.HasProperty("_QueueOffset")) material.SetFloat("_QueueOffset", 0f);
            material.renderQueue = targetRenderQueue;

            if (alphaClip == 1)
            {
                material.EnableKeyword("_ALPHATEST_ON");
                material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.SetOverrideTag("RenderType", "TransparentCutout");
            }
            else if (surfaceType == 1)
            {
                material.DisableKeyword("_ALPHATEST_ON");
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                if (blendMode == 1) material.EnableKeyword("_ALPHAPREMULTIPLY_ON");
                else material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                material.SetOverrideTag("RenderType", "Transparent");
            }
            else
            {
                material.DisableKeyword("_ALPHATEST_ON");
                material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                material.SetOverrideTag("RenderType", "Opaque");
            }

            // 5. Base Color & Main Tex
            if (hasBaseColor && material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", baseColor);

            if (hasMainTex && material.HasProperty("_BaseMap"))
            {
                material.SetTexture("_BaseMap", mainTex);
                material.SetTextureScale("_BaseMap", mainTexScale);
                material.SetTextureOffset("_BaseMap", mainTexOffset);
            }

            if (material.HasProperty("_Cutoff"))
                material.SetFloat("_Cutoff", cutoff);

            // 6. Lit Specific Properties
            if (isTargetLit)
            {
                if (material.HasProperty("_WorkflowMode"))
                    material.SetFloat("_WorkflowMode", isSpecularWorkflow ? 0f : 1f);

                if (isSpecularWorkflow)
                {
                    if (material.HasProperty("_SpecColor")) material.SetColor("_SpecColor", specColor);
                    if (material.HasProperty("_SpecGlossMap"))
                    {
                        material.SetTexture("_SpecGlossMap", specGlossMap);
                        if (specGlossMap != null) material.EnableKeyword("_METALLICSPECGLOSSMAP");
                    }
                    material.EnableKeyword("_SPECULAR_SETUP");
                }
                else
                {
                    if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", metallic);
                    if (material.HasProperty("_MetallicGlossMap"))
                    {
                        material.SetTexture("_MetallicGlossMap", metallicGlossMap);
                        if (metallicGlossMap != null)
                        {
                            material.EnableKeyword("_METALLICSPECGLOSSMAP");
                            if (material.HasProperty("_SmoothnessTextureChannel"))
                                material.SetFloat("_SmoothnessTextureChannel", smoothnessChannel);
                        }
                    }
                    material.DisableKeyword("_SPECULAR_SETUP");
                }

                if (material.HasProperty("_Smoothness"))
                    material.SetFloat("_Smoothness", smoothness);

                // Normal Map
                if (material.HasProperty("_BumpMap"))
                {
                    material.SetTexture("_BumpMap", bumpMap);
                    if (bumpMap != null)
                    {
                        material.SetTextureScale("_BumpMap", bumpScaleSt);
                        material.SetTextureOffset("_BumpMap", bumpOffsetSt);
                        material.EnableKeyword("_NORMALMAP");
                        if (material.HasProperty("_BumpScale"))
                            material.SetFloat("_BumpScale", bumpScale);
                    }
                    else
                    {
                        material.DisableKeyword("_NORMALMAP");
                    }
                }

                // Parallax Map
                if (material.HasProperty("_ParallaxMap"))
                {
                    material.SetTexture("_ParallaxMap", parallaxMap);
                    if (parallaxMap != null)
                    {
                        material.EnableKeyword("_PARALLAXMAP");
                        if (material.HasProperty("_Parallax"))
                            material.SetFloat("_Parallax", parallax);
                    }
                    else
                    {
                        material.DisableKeyword("_PARALLAXMAP");
                    }
                }

                // Occlusion Map
                if (material.HasProperty("_OcclusionMap"))
                {
                    material.SetTexture("_OcclusionMap", occlusionMap);
                    if (occlusionMap != null)
                    {
                        material.EnableKeyword("_OCCLUSIONMAP");
                        if (material.HasProperty("_OcclusionStrength"))
                            material.SetFloat("_OcclusionStrength", occlusionStrength);
                    }
                    else
                    {
                        material.DisableKeyword("_OCCLUSIONMAP");
                    }
                }

                // Emission
                if (material.HasProperty("_EmissionColor"))
                    material.SetColor("_EmissionColor", emissionColor);

                if (material.HasProperty("_EmissionMap"))
                {
                    material.SetTexture("_EmissionMap", emissionMap);
                    if (emissionMap != null)
                    {
                        material.SetTextureScale("_EmissionMap", emissionScale);
                        material.SetTextureOffset("_EmissionMap", emissionOffset);
                    }
                }

                var isEmissive = (emissionMap != null) ||
                                 (hasEmissionColor && (emissionColor.r > 0.0001f || emissionColor.g > 0.0001f || emissionColor.b > 0.0001f));
                if (isEmissive)
                {
                    material.EnableKeyword("_EMISSION");
                    material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                }
                else
                {
                    material.DisableKeyword("_EMISSION");
                    material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
                }

                // Detail Map
                var hasDetail = (detailAlbedoMap != null || detailNormalMap != null);
                if (hasDetail)
                {
                    if (material.HasProperty("_DetailMask")) material.SetTexture("_DetailMask", detailMask);
                    if (material.HasProperty("_DetailAlbedoMap"))
                    {
                        material.SetTexture("_DetailAlbedoMap", detailAlbedoMap);
                        material.SetTextureScale("_DetailAlbedoMap", detailAlbedoScale);
                        material.SetTextureOffset("_DetailAlbedoMap", detailAlbedoOffset);
                    }
                    if (material.HasProperty("_DetailNormalMap"))
                    {
                        material.SetTexture("_DetailNormalMap", detailNormalMap);
                        if (material.HasProperty("_DetailNormalMapScale"))
                            material.SetFloat("_DetailNormalMapScale", detailNormalScale);
                    }
                    if (material.HasProperty("_UVSec")) material.SetFloat("_UVSec", uvSec);
                    material.EnableKeyword("_DETAIL_MULX2");
                }
                else
                {
                    material.DisableKeyword("_DETAIL_MULX2");
                }

                // Highlights & Reflections
                if (material.HasProperty("_ReceiveShadows")) material.SetFloat("_ReceiveShadows", 1f);
                if (material.HasProperty("_SpecularHighlights"))
                {
                    material.SetFloat("_SpecularHighlights", specHighlights);
                    if (specHighlights < 0.5f) material.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
                    else material.DisableKeyword("_SPECULARHIGHLIGHTS_OFF");
                }
                if (material.HasProperty("_EnvironmentReflections"))
                {
                    material.SetFloat("_EnvironmentReflections", envReflections);
                    if (envReflections < 0.5f) material.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
                    else material.DisableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
                }
            }
            else if (!isTargetUnlit)
            {
                // Particles / Sprites / Other
                if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
                if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", metallic);
                if (material.HasProperty("_BumpMap") && bumpMap != null) material.SetTexture("_BumpMap", bumpMap);
                if (material.HasProperty("_EmissionColor")) material.SetColor("_EmissionColor", emissionColor);
                if (material.HasProperty("_EmissionMap") && emissionMap != null) material.SetTexture("_EmissionMap", emissionMap);
            }

            return true;
        }
    }
}
#endif