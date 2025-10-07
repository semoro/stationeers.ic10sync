using System;
using System.Collections.Generic;
using Assets.Scripts;
using Assets.Scripts.Objects.Electrical;
using Assets.Scripts.Objects.Motherboards;
using Assets.Scripts.Util;
using HarmonyLib;
using StationeersMods.Interface;

namespace IC10Sync
{
    [StationeersMod("IC10Sync", "IC10Sync", "0.2.5499.24517.3")]
    public class IC10Sync : ModBehaviour
    {
        // Config entry for chip data path
        private static string _chipDataPathResolved;

        private static bool _allowDirectChipEditingInSurvival = false;

        public override void OnLoaded(ContentHandler contentHandler)
        {
            base.OnLoaded(contentHandler);

            // Config for file path
            var chipDataPath = Config.Bind("Output", "ChipDataPath", @"%USERPROFILE%\Documents\My games\Stationeers\chipdata", "Path to write chip data").Value;

            // Config for allowing direct chip editing in survival mode
            _allowDirectChipEditingInSurvival = Config.Bind("Settings", "AllowDirectChipEditingInSurvival", true, "Allow direct editing of IC10 chips in survival mode (Otherwise, allow to only edit source code in computer/laptop), Change requires restart").Value;

            var harmony = new Harmony("IC10Sync");
            harmony.PatchAll();
            UnityEngine.Debug.Log("IC10Sync Loaded!");
            _chipDataPathResolved = System.IO.Path.GetFullPath(System.Environment.ExpandEnvironmentVariables(chipDataPath));
            UnityEngine.Debug.Log($"Path `{chipDataPath}` Resolved to `{_chipDataPathResolved}`");


            // Start coroutine to write chip data every 10 seconds
            StartCoroutine(WriteChipDataRoutine());
        }

        private System.Collections.IEnumerator WriteChipDataRoutine()
        {
            while (true)
            {
                var shorterDelay = WriteAllChipData();
                if (shorterDelay)
                {
                    yield return new UnityEngine.WaitForSeconds(1f);
                }
                else
                {
                    yield return new UnityEngine.WaitForSeconds(10f);
                }
            }
        }

        private static Dictionary<string, long> codeHashes = new Dictionary<string, long>();

        /**
         * Returns true if any chips was loaded => means continue with a shorter delay 
         */
        private bool WriteAllChipData()
        {
            try
            {

                string directory = _chipDataPathResolved;

                if (!System.IO.Directory.Exists(directory))
                {
                    System.IO.Directory.CreateDirectory(directory);
                }

                // In survival mode, only update chips if allowed by config
                if (WorldManager.Instance.GameMode == GameMode.Creative || _allowDirectChipEditingInSurvival)
                {
                    // Find all ProgrammableChip instances in the scene
                    var chips = FindObjectsOfType<ProgrammableChip>();

                    foreach (var chip in chips)
                    {
                        var filePrefix = $"{chip.DisplayName ?? "UNNAMED"} [{chip.ReferenceId}]";
                        if (UpdateChipDataIfNecessary(filePrefix, chip, directory))
                        {
                            return true; // If any chip was updated, return true to use shorter delay
                        }
                    }
                }

                // Always update chips in computers and laptops
                var motherboards = FindObjectsOfType<ProgrammableChipMotherboard>();
                foreach (var motherboard in motherboards)
                {
                    var computer = motherboard.ParentComputer;
                    if (computer == null)
                    {
                        continue;
                    }
                    var filePrefix = $"{computer.DisplayName ?? "UNNAMED"} {motherboard.DisplayName ?? "UNNAMED"} [{motherboard.ReferenceId}]";
                    if (UpdateChipDataIfNecessary(filePrefix, motherboard, directory))
                    {
                        return true; // If any chip was updated, return true to use shorter delay
                    }
                }
               
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogError("Failed to write chip data: " + ex.Message);
            }
            return false;
        }

        public bool UpdateChipDataIfNecessary(string filePrefix, ISourceCode sourceCode, string directory)
        {
            var key = filePrefix;
            string fileName = $"{key}.ic10";
            string filePath = System.IO.Path.Combine(directory, fileName);

            var sourceCodeText = sourceCode.GetSourceCode().ToString();
            var hashBefore = sourceCodeText.GetHashCode();


            if (!System.IO.File.Exists(filePath) || !codeHashes.ContainsKey(key) || codeHashes[key] != hashBefore) // file doesn't exist or code has changed
            {
                UnityEngine.Debug.LogWarning($"{key} source code exported. Before: {codeHashes.GetValueOrDefault(key)}, After: {hashBefore}");
                ConsoleWindow.Print($"{key} source code exported. Before: {codeHashes.GetValueOrDefault(key)}, After: {hashBefore}", ConsoleColor.Blue);
                System.IO.File.WriteAllText(filePath, sourceCodeText);
            }
            codeHashes[key] = hashBefore; // store current hash

            // Check if the file exists and read its content
            var onDiskSourceCode = System.IO.File.Exists(filePath) ? System.IO.File.ReadAllText(filePath) : string.Empty;
            var onDiskHash = onDiskSourceCode.GetHashCode();

            if (onDiskHash != hashBefore)
            {
                UnityEngine.Debug.LogWarning($"{key} source code loaded. Before: {hashBefore}, After: {onDiskHash}");
                ConsoleWindow.Print($"{key} source code loaded. Before: {hashBefore}, After: {onDiskHash}", ConsoleColor.Green);
                // If the hash doesn't match, write the new source code
                sourceCode.SetSourceCode(onDiskSourceCode);
                sourceCode.SendUpdate();
                codeHashes[key] = onDiskHash; // Update the hash in the dictionary
                return true; // Update only one chip per iteration to avoid networking rejection
            }

            return false; // No source code was updated
        }
    }

}