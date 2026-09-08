#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

namespace Nox.Editor {
	public static class MaterialConverter {

		private const string MenuPath = "Assets/Nox/Convert to URP";

		// ── Context menu entry ──────────────────────────────────────────────

		[MenuItem(MenuPath, false, 2000)]
		private static void ConvertSelectedToURP() {
			var materials = GetSelectedMaterials();
			if (materials.Length == 0) return;

			// Group all conversions into a single Undo step
			Undo.SetCurrentGroupName("Convert to URP");
			int group = Undo.GetCurrentGroup();

			int count = 0;
			foreach (var mat in materials) {
				if (ConvertToURP(mat))
					count++;
			}

			Undo.CollapseUndoOperations(group);

			if (count > 0)
				Debug.Log($"[Nox.Editor] Converted {count} material(s) to URP.");
		}

		[MenuItem(MenuPath, true)]
		private static bool ConvertSelectedToURPValidate() {
			foreach (var obj in Selection.objects) {
				if (obj is Material m && IsBuiltInStandard(m.shader))
					return true;
			}
			return false;
		}

		// ── Helpers ─────────────────────────────────────────────────────────

		private static Material[] GetSelectedMaterials() {
			var objects = Selection.objects;
			var list    = new System.Collections.Generic.List<Material>();
			foreach (var obj in objects) {
				if (obj is Material m)
					list.Add(m);
			}
			return list.ToArray();
		}

		private static bool IsBuiltInStandard(Shader shader) {
			if (shader == null) return false;
			string name = shader.name;
			return name == "Standard"
				|| name == "Standard (Specular setup)"
				|| name == "Standard (Roughness setup)";
		}

		/// <summary>
		/// Detects the Standard shader surface mode via the "_Mode" hidden property.
		/// 0 = Opaque, 1 = Cutout, 2 = Fade, 3 = Transparent.
		/// </summary>
		private static int GetStandardMode(Material m) {
			if (m.HasProperty("_Mode"))
				return Mathf.RoundToInt(m.GetFloat("_Mode"));
			// Fallback heuristic from render queue / blend state
			if (m.renderQueue == (int)UnityEngine.Rendering.RenderQueue.AlphaTest
				|| m.renderQueue == (int)UnityEngine.Rendering.RenderQueue.Transparent)
				return (m.renderQueue == (int)UnityEngine.Rendering.RenderQueue.AlphaTest) ? 1 : 3;
			return 0;
		}

		/// <summary>
		/// Returns true when the Standard material uses the Metallic workflow (i.e. the
		/// "Standard" default shader) rather than the Specular workflow. Only relevant
		/// for shader name "Standard (Specular setup)".
		/// </summary>
		private static bool IsMetallicWorkflow(Material m) {
			if (m.shader == null) return true;
			return m.shader.name == "Standard" || m.shader.name == "Standard (Roughness setup)";
		}

		// ── Conversion ──────────────────────────────────────────────────────

		private static bool ConvertToURP(Material src) {
			if (src == null) return false;

			Shader urpShader = Shader.Find("Universal Render Pipeline/Lit");
			if (urpShader == null) {
				Debug.LogWarning("[Nox.Editor] Universal Render Pipeline/Lit shader not found. Is URP installed?");
				return false;
			}

			// Preserve path & name
			string assetPath = AssetDatabase.GetAssetPath(src);
			string matName   = src.name;

			// Record state for Undo (Ctrl+Z)
			Undo.RegisterCompleteObjectUndo(src, "Convert to URP");

			// Backup original colour before creating the new material
			Color origColor = Color.white;
			if (src.HasProperty("_Color"))
				origColor = src.GetColor("_Color");

			// ── Remap properties whose names differ between shaders ──────────
			//
			// IMPORTANT: In the URP Lit shader the active [MainTexture]/[MainColor]
			// are _BaseMap / _BaseColor. _MainTex / _Color only exist as hidden
			// ObsoleteProperties. We therefore copy every needed value explicitly
			// from the source (Standard) material to the correct URP property, and
			// we always carry over the texture tiling/offset (SetTexture alone would
			// reset the scale to 1,1 and lose e.g. an 8x8 albedo tiling).

			var dst = new Material(urpShader);

			// Albedo color (Standard _Color → URP _BaseColor, the [MainColor])
			SetColor(dst, "_BaseColor", origColor);

			// Albedo texture (Standard _MainTex → URP _BaseMap, the [MainTexture])
			CopyTexture(src, dst, "_MainTex", "_BaseMap");

			// Specular workflow detection (Standard Specular setup → URP)
			bool isSpecularSetup = src.HasProperty("_SpecColor")
				&& (src.GetColor("_SpecColor") != Color.white || IsMetallicWorkflow(src) == false);

			// Workflow: Metallic = 0, Specular = 1
			if (dst.HasProperty("_WorkflowMode"))
				dst.SetFloat("_WorkflowMode", isSpecularSetup ? 1f : 0f);

			if (isSpecularSetup) {
				dst.EnableKeyword("_SPECULAR_SETUP");
				// Specular map (Standard _MetallicGlossMap doubles as spec gloss map in Specular mode)
				CopyTexture(src, dst, "_SpecGlossMap", "_SpecGlossMap");
				if (dst.HasProperty("_SpecGlossMap")) {
					if (src.GetTexture("_SpecGlossMap") == null)
						CopyTexture(src, dst, "_MetallicGlossMap", "_SpecGlossMap");
				}
				// Indicator used by Lit shader for spec setup
				dst.EnableKeyword("_METALLICSPECGLOSSMAP");
			} else {
				// Metallic map (Standard _MetallicGlossMap → URP _MetallicGlossMap)
				CopyTexture(src, dst, "_MetallicGlossMap", "_MetallicGlossMap");
				if (dst.HasProperty("_MetallicGlossMap") && src.GetTexture("_MetallicGlossMap") != null)
					dst.EnableKeyword("_METALLICSPECGLOSSMAP");
			}

			// Specular color (Standard _SpecColor → URP _SpecColor)
			if (src.HasProperty("_SpecColor"))
				CopyColor(src, dst, "_SpecColor", "_SpecColor");

			// Smoothness (Standard _Glossiness → URP _Smoothness)
			if (src.HasProperty("_Glossiness")) {
				float smoothness = src.GetFloat("_Glossiness");
				if (dst.HasProperty("_Smoothness"))
					dst.SetFloat("_Smoothness", smoothness);
			}

			// Metallic intensity (Standard _Metallic → URP _Metallic, same name)
			CopyFloat(src, dst, "_Metallic", "_Metallic");

			// Specular highlights toggle (Standard _SpecularHighlights → URP)
			CopyFloat(src, dst, "_SpecularHighlights", "_SpecularHighlights");
			if (dst.HasProperty("_SpecularHighlights") && src.HasProperty("_SpecularHighlights")
				&& src.GetFloat("_SpecularHighlights") < 0.5f)
				dst.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");

			// Smoothness texture channel (Standard → URP, same property name)
			CopyFloat(src, dst, "_SmoothnessTextureChannel", "_SmoothnessTextureChannel");
			if (dst.HasProperty("_SmoothnessTextureChannel") && src.HasProperty("_SmoothnessTextureChannel")
				&& src.GetFloat("_SmoothnessTextureChannel") > 0.5f)
				dst.EnableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");

			// Normal map scale
			CopyFloat(src, dst, "_BumpScale", "_BumpScale");

			// Normal map (Standard _BumpMap → URP _BumpMap)
			CopyTexture(src, dst, "_BumpMap", "_BumpMap");
			if (dst.HasProperty("_BumpMap") && src.GetTexture("_BumpMap") != null)
				dst.EnableKeyword("_NORMALMAP");

			// Parallax
			CopyFloat(src, dst, "_Parallax", "_Parallax");
			CopyTexture(src, dst, "_ParallaxMap", "_ParallaxMap");
			if (dst.HasProperty("_ParallaxMap") && src.GetTexture("_ParallaxMap") != null)
				dst.EnableKeyword("_PARALLAXMAP");

			// Occlusion
			CopyFloat(src, dst, "_OcclusionStrength", "_OcclusionStrength");
			CopyTexture(src, dst, "_OcclusionMap", "_OcclusionMap");
			if (dst.HasProperty("_OcclusionMap") && src.GetTexture("_OcclusionMap") != null)
				dst.EnableKeyword("_OCCLUSIONMAP");

			// ── Emission (HDR) ─────────────────────────────────────────────

			if (src.HasProperty("_EmissionColor")) {
				Color emission = src.GetColor("_EmissionColor");
				if (dst.HasProperty("_EmissionColor")) {
					if (emission.maxColorComponent > 0f)
						dst.EnableKeyword("_EMISSION");
					dst.SetColor("_EmissionColor", emission);
					dst.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
				}
			}
			CopyTexture(src, dst, "_EmissionMap", "_EmissionMap");

			// ── Surface mode (Standard _Mode → URP _Surface) ───────────────

			int mode = GetStandardMode(src);   // 0 Opaque, 1 Cutout, 2 Fade, 3 Transparent
			if (mode > 0 && dst.HasProperty("_Surface")) {
				// URP surface: 0 = Opaque, 1 = Transparent (handles Fade/Transparent)
				dst.SetFloat("_Surface", 1f);
				dst.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
				dst.SetOverrideTag("RenderType", "Transparent");
				dst.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

				// Blend state for transparency
				if (dst.HasProperty("_SrcBlend")) dst.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
				if (dst.HasProperty("_DstBlend")) dst.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
				if (dst.HasProperty("_ZWrite")) dst.SetFloat("_ZWrite", 0f);

				// Transparent render
				dst.SetShaderPassEnabled("ShadowCaster", false);
			} else {
				dst.SetShaderPassEnabled("ShadowCaster", true);
				dst.SetShaderPassEnabled("Meta", true);
			}

			// ── Alpha clipping ──────────────────────────────────────────────

			if (mode == 1 && src.HasProperty("_Cutoff")) {   // Cutout only
				float cutoff = src.GetFloat("_Cutoff");
				if (dst.HasProperty("_Cutoff"))
					dst.SetFloat("_Cutoff", cutoff);

				dst.EnableKeyword("_ALPHATEST_ON");
				dst.SetOverrideTag("RenderType", "TransparentCutout");
				dst.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
			}

			// ── Remaining URP configuration ────────────────────────────────

			// Render face: Front (2)
			if (dst.HasProperty("_Cull"))
				dst.SetFloat("_Cull", 2f);

			// Mirror Standard _GlossyReflections toggle → URP _EnvironmentReflections
			CopyFloat(src, dst, "_GlossyReflections", "_EnvironmentReflections");
			if (dst.HasProperty("_EnvironmentReflections") && dst.GetFloat("_EnvironmentReflections") < 0.5f)
				dst.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");

			dst.SetShaderPassEnabled("Meta", true);

			// ── Persist to disk ─────────────────────────────────────────────

			dst.name = matName;

			if (!string.IsNullOrEmpty(assetPath)) {
				EditorUtility.CopySerialized(dst, src);
				Object.DestroyImmediate(dst);
				EditorUtility.SetDirty(src);
				AssetDatabase.SaveAssets();
			}

			return true;
		}

		// ── Property copy helpers ───────────────────────────────────────────
		//
		// Each copy helper reads from the source (Standard) material and writes
		// to the destination (URP) material, even when both use the same property
		// name. This avoids relying on the URP shader's hidden ObsoleteProperties
		// (e.g. _MainTex / _Color) that are NOT the active [MainTexture]/[MainColor].

		private static void SetColor(Material m, string prop, Color value) {
			if (m.HasProperty(prop))
				m.SetColor(prop, value);
		}

		private static void CopyTexture(Material src, Material dst, string srcProp, string dstProp) {
			if (src.HasProperty(srcProp) && dst.HasProperty(dstProp)) {
				dst.SetTexture(dstProp, src.GetTexture(srcProp));
				// Carry over tiling & offset (SetTexture would reset scale to 1,1)
				dst.SetTextureScale(dstProp, src.GetTextureScale(srcProp));
				dst.SetTextureOffset(dstProp, src.GetTextureOffset(srcProp));
			}
		}

		private static void CopyColor(Material src, Material dst, string srcProp, string dstProp) {
			if (src.HasProperty(srcProp) && dst.HasProperty(dstProp))
				dst.SetColor(dstProp, src.GetColor(srcProp));
		}

		private static void CopyFloat(Material src, Material dst, string srcProp, string dstProp) {
			if (src.HasProperty(srcProp) && dst.HasProperty(dstProp))
				dst.SetFloat(dstProp, src.GetFloat(srcProp));
		}
	}
}
#endif
