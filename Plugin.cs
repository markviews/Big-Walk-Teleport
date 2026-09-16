using System;
using System.Collections;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using Mirror;
using TMPro;
using UnityEngine;

namespace BigTeleport
{
    [BepInPlugin("markviews.bigTeleport", "Big Teleport", "1.0.1")]
    public class Plugin : BasePlugin
    {

        public static readonly System.Collections.Generic.Dictionary<string, string> NameOverrides = new(StringComparer.OrdinalIgnoreCase)
        {
            { "HighButton", "TallButtonChallenge" },
            { "CannonBall", "CannonBallCarry" },
            { "Basketball", "BasketballChallenge" },
            { "Coordinates", "CoordinatesSimPress" },
            { "MediumSimPress", "MediumSimPressChallenge" },
            { "EasySimPress", "SmallSimPressChallenge" },
            { "Concert", "ConductorConcert" },
            { "CenturonSong", "CenturionSong" },
            { "TelescopeToBox", "FixedTelescopeToGourd" },

            { "WindowLabyrinth", "Labyrinth" },
            { "IndoorSemaphore", "SemaphoreRooms" },

            { "InvisibleInk", "InvisibleInkChallenge" },
            { "RingRoom", "RingRoomChallenge" },

             // I'm unsure about these last 2
            { "TileSoup", "SilentGauntlet 4Player" },
            { "Maypole", "BlackTower" },
        };

        public static readonly System.Collections.Generic.Dictionary<string, Vector3> locations = new();

        internal static ManualLogSource Log;

        public override void Load()
        {
            Log = base.Log;
            new Harmony("markviews.bigTeleport").PatchAll();
            ClassInjector.RegisterTypeInIl2Cpp<ButtonScanner>();

            var scannerObj = new GameObject("BigWalkHelloWorld_ButtonScanner");
            UnityEngine.Object.DontDestroyOnLoad(scannerObj);
            scannerObj.AddComponent<ButtonScanner>();
        }
    }

    public class ButtonScanner : MonoBehaviour
    {
        public ButtonScanner(IntPtr ptr) : base(ptr) { }

        private static GameObject prefab;
        private static TextMeshProUGUI chatBox;
        private static TextMeshProUGUI chatOutput;
        private TextMeshProUGUI myOutput;
        private Vector3 mapPosition;
        private PlayerCharacter localPlayer;

        private static readonly string[] SearchRoots = new[]
        {
            "LandmarksPlayerCount2/Contents",
            "LandmarksPlayerCount3/Contents",
            "LandmarksPlayerCount4/Contents",
            "LandmarksPlayerCountAny"
        };

        private void Start()
        {
            WorldManager.add_OnWorldManagerStart(new Action(() => {
                InvokeRepeating(nameof(ScanForButton), 0f, 1f);
            }));
        }

        private void Update()
        {
            if (Input.GetMouseButtonDown(0))
            {
                if (localPlayer == null) return;

                var caster = localPlayer.caster;
                if (caster == null) return;

                var target = caster.castableTarget;
                if (target == null) return;

                if (Plugin.locations.TryGetValue(target.gameObject.name, out var pos))
                {
                    StartCoroutine(Teleport(localPlayer, pos).WrapToIl2Cpp());
                }
            }

            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                if (chatBox == null || localPlayer == null || myOutput == null) return;

                string trimmed = chatBox.text.Replace("\u200B", "").Trim().ToLower();
                if (trimmed.Equals("map") || trimmed.Equals("/map"))
                {
                    StartCoroutine(ClearChatbox().WrapToIl2Cpp());

                    float dist = GetNearestPuzzle();
                    if (dist < 20f)
                    {
                        float needsToMove = Mathf.Max(20 - dist, 0.01f);
                        ShowMessage($"<color=red>Move {needsToMove:F1}m away from puzzle</color>");
                    }
                    else
                    {
                        StartCoroutine(Teleport(localPlayer, mapPosition).WrapToIl2Cpp());
                    }


                }
            }

        }

        private float GetNearestPuzzle()
        {
            string nearestName = null;
            float nearestDist = float.MaxValue;
            Vector3 playerPos = localPlayer.transform.position;

            foreach (var kvp in Plugin.locations)
            {
                float dist = Vector3.Distance(playerPos, kvp.Value);
                if (dist < nearestDist)
                {
                    nearestDist = dist;
                    nearestName = kvp.Key;
                }
            }

            if (nearestName == null)
            {
                Plugin.Log.LogWarning("GetNearestPuzzle: locations dictionary is empty.");
                return -1f;
            }

            //Plugin.Log.LogWarning($"GetNearestPuzzle: {nearestDist} {nearestName}");

            return nearestDist;
        }

        private IEnumerator ClearChatbox()
        {
            yield return new WaitForSeconds(0.5f);
            chatBox.text = "";
        }

        private Coroutine delayCoroutine;

        private void ShowMessage(string message)
        {
            myOutput.text = message;

            if (delayCoroutine != null)
            {
                StopCoroutine(delayCoroutine);
            }
            delayCoroutine = StartCoroutine(DelayClearMessage().WrapToIl2Cpp());
        }

        private IEnumerator DelayClearMessage()
        {
            yield return new WaitForSeconds(3f);
            myOutput.text = "";
            delayCoroutine = null;
        }

        private void CreateMyOutput()
        {
            var go = UnityEngine.Object.Instantiate(chatOutput.gameObject, chatOutput.transform.parent.parent.parent);
            go.name = "MyModOutput";

            var rect = go.GetComponent<RectTransform>();
            rect.anchoredPosition = new Vector2(0f, -80f);
            go.SetActive(true);

            myOutput = go.GetComponent<TextMeshProUGUI>();
            myOutput.richText = true;
        }

        // keep trying to teleport once per frame.. sometimes it takes a few tries
        private IEnumerator Teleport(PlayerCharacter player, Vector3 pos)
        {
            int attempt = 0;

            while (true)
            {
                attempt++;
                if (attempt >= 10)
                {
                    Plugin.Log.LogInfo($"Teleport attempt {pos} FAILED");
                    ShowMessage("<color=red>Teleport Failed after 10 attempts</color>");
                    break;
                }

                float dist = Vector3.Distance(player.transform.position, pos);
                if (dist <= 1)
                {
                    break;
                }

                player.grease.Teleport(pos, Quaternion.identity, true);
                //player.transform.position = pos;

                yield return null;
            }
            ShowMessage("Teleport Successful");
        }

        private void ScanForButton()
        {
            //Plugin.Log.LogInfo($"ScanForButton");

            if (NetworkClient.localPlayer == null) return;

            GameObject originalButton = GameObject.Find("LandmarksNonChallenge/SpawnCourtyardandHubPlatform/Positioner/TeachingArea/Positioner/ObjectsTeaching/Switch/PushButton/BasicPushButton/PeckSwitchTrigger");
            if (originalButton == null) return;

            GameObject chatBoxObj = GameObject.Find("WorldUI/WorldUICanvas/GameOverlay/TextChatInput/LocalInput/Text Area/Text");
            if (chatBoxObj == null) return;

            GameObject chatOutputObj = GameObject.Find("WorldUI/WorldUICanvas/GameOverlay/TextChatInput/LocalOutput");
            if (chatOutputObj == null) return;

            GameObject mapObj = GameObject.Find("LandmarksNonChallenge/MapRoom/Positioner/TeleportPointMapRoom");
            if (mapObj == null) return;

            GameObject mapRoomRoof = GameObject.Find("LandmarksNonChallenge/MapRoom/Positioner/MapRoom/RoofParent");
            if (mapRoomRoof == null) return;

            Plugin.Log.LogInfo($"ScanForButton SUCCESS");

            mapRoomRoof.SetActive(false);
            mapPosition = mapObj.transform.position;
            chatBox = chatBoxObj.GetComponent<TextMeshProUGUI>();
            chatOutput = chatBoxObj.transform.GetComponent<TextMeshProUGUI>();
            localPlayer = NetworkClient.localPlayer.gameObject.GetComponent<PlayerCharacter>();

            CreateMyOutput();

            // disable original button during copy to prevent console warnings about duplicate IDs before we remove PeckSwitch script
            originalButton.SetActive(false);
            prefab = GameObject.Instantiate(originalButton);

            prefab.transform.Find("CrosshairPosition").transform.localPosition = Vector3.zero;
            UnityEngine.Object.Destroy(prefab.transform.Find("UpSwitch").gameObject);

            var peckSwitch = prefab.GetComponent<PeckSwitch>();
            if (peckSwitch != null) UnityEngine.Object.Destroy(peckSwitch);

            var matProp = prefab.GetComponent<PeckEffectMaterialProperty>();
            if (matProp != null) UnityEngine.Object.Destroy(matProp);

            var audio = prefab.GetComponent<PeckEffectAudio>();
            if (audio != null) UnityEngine.Object.Destroy(audio);

            audio = prefab.GetComponent<PeckEffectAudio>();
            if (audio != null) UnityEngine.Object.Destroy(audio);

            var toggle = prefab.GetComponent<PeckEffectToggle>();
            if (toggle != null) UnityEngine.Object.Destroy(toggle);

            originalButton.SetActive(true);

            CancelInvoke(nameof(ScanForButton));
            SpawnButtonsOnChildren();
        }

        private void SpawnButtonsOnChildren()
        {
            //Plugin.Log.LogInfo($"****************************** SpawnButtonsOnChildren ******************************");

            var root = GameObject.Find("LandmarksNonChallenge/MapRoom/Positioner/Map-Relief/GourdMap");
            if (root == null)
            {
                Plugin.Log.LogWarning("GourdMap not found — check the path or make sure it's active in the hierarchy.");
                return;
            }

            // Resolve each search root once, up front, rather than re-finding it per gourd.
            var resolvedRoots = new Transform[SearchRoots.Length];
            for (int i = 0; i < SearchRoots.Length; i++)
            {
                var go = GameObject.Find(SearchRoots[i]);
                resolvedRoots[i] = go != null ? go.transform : null;

                if (go == null)
                    Plugin.Log.LogWarning($"Search root not found: {SearchRoots[i]}");
            }

            var parent = root.transform;
            int childCount = parent.childCount;
            //Plugin.Log.LogInfo($"Found GourdMap with {childCount} children — resolving targets and spawning buttons.");

            for (int i = 0; i < childCount; i++)
            {
                var child = parent.GetChild(i);
                string childName = child.name;
                Vector3 spawnPos = child.position;

                string targetName = childName.StartsWith("gourd", StringComparison.OrdinalIgnoreCase) ? childName.Substring(5) : childName;

                if (Plugin.NameOverrides.TryGetValue(targetName, out var overrideName))
                {
                    //Plugin.Log.LogInfo($"[{childName}] -> override '{targetName}' -> '{overrideName}'");
                    targetName = overrideName;
                }

                Transform match = null;
                string matchedRoot = null;

                for (int r = 0; r < resolvedRoots.Length; r++)
                {
                    if (resolvedRoots[r] == null) continue;

                    match = FindDescendant(resolvedRoots[r], targetName);
                    if (match != null)
                    {
                        matchedRoot = SearchRoots[r];
                        break;
                    }
                }

                Transform teleportTarget = null;

                if (match != null)
                {
                    teleportTarget = FindTeleportPoint(match);
                    if (teleportTarget != null)
                    {
                        //Plugin.Log.LogInfo($"[{childName}] -> found teleport point: '{teleportTarget.name} {teleportTarget.position}'");
                    }
                    else
                        Plugin.Log.LogWarning($"[{childName}] -> matched '{targetName}' but no teleport point found among its children.");
                }
                else
                {
                    Plugin.Log.LogWarning($"[{childName}] -> no match found for '{targetName}' in any search root.");
                }

                Transform capturedTeleportTarget = teleportTarget; // fresh local per-iteration, safe in the closure below

                if (capturedTeleportTarget != null)
                {
                    Plugin.locations[targetName] = capturedTeleportTarget.position;
                }

                var go = UnityEngine.Object.Instantiate(prefab, spawnPos, Quaternion.identity);
                go.name = targetName;
                go.transform.SetParent(child);
                go.transform.localPosition = Vector3.zero;
                go.SetActive(true);

                //Plugin.Log.LogInfo($"Spawned button at: {spawnPos} for {childName}");
            }

            //Plugin.Log.LogInfo($"SpawnButtonsOnChildren SUCCESS");
        }

        private static Transform FindDescendant(Transform root, string name)
        {
            if (root == null) return null;

            var all = root.GetComponentsInChildren<Transform>(true);
            foreach (var t in all)
            {
                if (t != root && string.Equals(t.name, name, StringComparison.OrdinalIgnoreCase))
                    return t;
            }

            return null;
        }

        private static Transform FindTeleportPoint(Transform root)
        {
            if (root == null) return null;

            var all = root.GetComponentsInChildren<Transform>(true);
            foreach (var t in all)
            {
                if (t != root && t.name.IndexOf("teleport", StringComparison.OrdinalIgnoreCase) >= 0)
                    return t;
            }

            return null;
        }

    }
}