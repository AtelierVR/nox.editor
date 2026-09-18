
using UnityEngine;
using UnityEditor;
using System.Text;

namespace Nox.Editor {

public static class TransformContextMenu
{
    [MenuItem("CONTEXT/Transform/Log Infos (Path, Local, Global)")]
    private static void LogTransformInfos(MenuCommand command)
    {
        Transform t = (Transform)command.context;

        StringBuilder sb = new StringBuilder();
        sb.AppendLine($"<b>=== Transform Info : {t.name} ===</b>");
        sb.AppendLine($"Path : {GetHierarchyPath(t)}");

        sb.AppendLine("--- Local ---");
        sb.AppendLine($"Position : {t.localPosition:F4}");
        sb.AppendLine($"Rotation (Euler) : {t.localEulerAngles:F4}");
        sb.AppendLine($"Rotation (Quaternion) : {t.localRotation}");
        sb.AppendLine($"Scale : {t.localScale:F4}");

        sb.AppendLine("--- Global ---");
        sb.AppendLine($"Position : {t.position:F4}");
        sb.AppendLine($"Rotation (Euler) : {t.eulerAngles:F4}");
        sb.AppendLine($"Rotation (Quaternion) : {t.rotation}");
        sb.AppendLine($"Scale (Lossy) : {t.lossyScale:F4}");

        sb.AppendLine("--- Divers ---");
        sb.AppendLine($"Forward : {t.forward:F4}");
        sb.AppendLine($"Right : {t.right:F4}");
        sb.AppendLine($"Up : {t.up:F4}");
        sb.AppendLine($"Child Count : {t.childCount}");
        sb.AppendLine($"Sibling Index : {t.GetSiblingIndex()}");

        Debug.Log(sb.ToString(), t);
    }

    [MenuItem("CONTEXT/Transform/Copy Path To Clipboard")]
    private static void CopyPathToClipboard(MenuCommand command)
    {
        Transform t = (Transform)command.context;
        string path = GetHierarchyPath(t);
        EditorGUIUtility.systemCopyBuffer = path;
        Debug.Log($"Path copié : {path}");
    }

    private static string GetHierarchyPath(Transform t)
    {
        if (t.parent == null)
            return "/" + t.name;

        return GetHierarchyPath(t.parent) + "/" + t.name;
    }
}
}