using System;
using System.IO;
using MCPForUnity.Editor.Services;
using MCPForUnity.Editor.Services.Transport.Transports;
using UnityEditor;
using UnityEngine;

namespace CleanGirls.Editor
{
    /// <summary>Project-scoped stdio bridge, including after domain reloads.</summary>
    [InitializeOnLoad]
    public static class ProjectMcpConnection
    {
        static ProjectMcpConnection()
        {
            ConfigureEnvironment();
            // Keep the transport override session-local; do not change other projects' preferences.
            EditorConfigurationCache.Instance.PinStdioForSession();
            SessionState.SetBool("MCPForUnity.SetupWindowCheckedThisEditorSession", true);
            if (!Application.isBatchMode || Environment.GetEnvironmentVariable("UNITY_MCP_ALLOW_BATCH") == "1")
                EditorApplication.delayCall += Start;
        }

        private static void ConfigureEnvironment()
        {
            string root = Directory.GetParent(Application.dataPath).Parent.FullName;
            string status = Path.Combine(root, "build", "unity-mcp-status");
            Directory.CreateDirectory(status);
            Environment.SetEnvironmentVariable("UNITY_MCP_STATUS_DIR", status);
            Environment.SetEnvironmentVariable("UNITY_MCP_DISABLE_TELEMETRY", "1");
        }

        /// <summary>Starts the project MCP bridge; may be used with -executeMethod.</summary>
        public static void Start()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += Start;
                return;
            }
            ConfigureEnvironment();
            EditorConfigurationCache.Instance.PinStdioForSession();
            if (!StdioBridgeHost.IsRunning) StdioBridgeHost.StartAutoConnect();
            Debug.Log("CLEAN_GIRLS_MCP_READY port=" + StdioBridgeHost.GetCurrentPort());
        }
    }
}
