#if UNITY_EDITOR

using System.Collections.Generic;
using Nox.ModLoader;
using Nox.ModLoader.Mods;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace Nox.Editor
{
    public class ExcludeAssetsFromBuild : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {       
        }
    }
}
#endif