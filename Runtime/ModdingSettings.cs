#if UNITY_EDITOR
using Nox.CCK.Utils;
using UnityEditor;
using UnityEditor.Build;

namespace Nox.Editor {
	public class ModdingSettings {
		public static ScriptingImplementation Implementation {
			get
				=> Config.LoadEditor()
						.Get("scripting_backend", "mono")
						.ToLowerInvariant()
					switch {
						"il2cpp" => ScriptingImplementation.IL2CPP,
						_        => ScriptingImplementation.Mono2x
					};
			set {
				var val = value switch {
					ScriptingImplementation.IL2CPP => "il2cpp",
					_                              => "mono"
				};
				var config = Config.LoadEditor();
				config.Set("scripting_backend", val);
				config.Save();
			}
		}

		[MenuItem("Nox/Other/Set Scripting Backend to Mono")]
		public static void SetScriptingBackendToMono() {
			Implementation = ScriptingImplementation.Mono2x;
			Init();
			Logger.Log("Scripting backend set to Mono.");
		}
		
		[MenuItem("Nox/Other/Set Scripting Backend to IL2CPP")]
		public static void SetScriptingBackendToIL2CPP() {
			Implementation = ScriptingImplementation.IL2CPP;
			Init();
			Logger.Log("Scripting backend set to IL2CPP.");
		}
		
		[InitializeOnLoadMethod]
		public static void Init() {
			ScriptingDefinitions.Add("NOX_SDK", NamedBuildTarget.Standalone);

			if (PlayerSettings.stripEngineCode)
				PlayerSettings.stripEngineCode = false;

			if (PlayerSettings.GetScriptingBackend(NamedBuildTarget.Standalone) != Implementation)
				PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, Implementation);

			if (PlayerSettings.GetApiCompatibilityLevel(NamedBuildTarget.Standalone) != ApiCompatibilityLevel.NET_Standard_2_0)
				PlayerSettings.SetApiCompatibilityLevel(NamedBuildTarget.Standalone, ApiCompatibilityLevel.NET_Standard_2_0);

			if (PlayerSettings.GetEditorAssembliesCompatibilityLevel() != EditorAssembliesCompatibilityLevel.NET_Standard)
				PlayerSettings.SetEditorAssembliesCompatibilityLevel(EditorAssembliesCompatibilityLevel.NET_Standard);

			if (PlayerSettings.insecureHttpOption != InsecureHttpOption.AlwaysAllowed)
				PlayerSettings.insecureHttpOption = InsecureHttpOption.AlwaysAllowed;

			ModLinkerHelper.EnsureLinkerClassExists();
		}
	}
}
#endif