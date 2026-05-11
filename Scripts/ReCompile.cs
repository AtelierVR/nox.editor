#if UNITY_EDITOR
using Nox.CCK.Utils;
using UnityEditor;
using UnityEditor.Compilation;

namespace Nox.Editor {
	public class ReCompile {
		[MenuItem("Nox/Tools/Recompile Unity")]
		public static void Recompile() {
			Logger.Log("Requesting script recompilation...");
			CompilationPipeline.RequestScriptCompilation();
		}
	}
}
#endif