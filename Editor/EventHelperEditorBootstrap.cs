using Lin.Runtime.Helper;
using UnityEditor;

namespace Lin.Editor
{
    internal static class EventHelperEditorBootstrap
    {
        [InitializeOnLoadMethod]
        private static void Initialize() => EventHelper.CaptureMainThreadContext();
    }
}
