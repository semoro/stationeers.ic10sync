using System;
using System.Collections.Generic;
using Assets.Scripts;
using Assets.Scripts.Objects.Electrical;
using Assets.Scripts.Objects.Motherboards;
using Assets.Scripts.Serialization;
using Assets.Scripts.Util;
using HarmonyLib;
using StationeersMods.Interface;

namespace IC10Sync
{
    [StationeersMod("IC10Sync", "IC10Sync", "0.2.6227.27061.1")]
    public class IC10Sync : ModBehaviour
    {
        // Config entry for chip data path
        private static string _chipDataPathResolved;

        private static bool _allowDirectChipEditingInSurvival = false;

        public override void OnLoaded(ContentHandler contentHandler)
        {
            base.OnLoaded(contentHandler);

            // Config for file path. Ask Windows for the Documents folder, since it may be redirected (e.g. to OneDrive)
            var documents = System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments);
            var defaultChipDataPath = System.IO.Path.Combine(documents, "My Games", "Stationeers", "chipdata");
            var chipDataPath = Config.Bind("Output", "ChipDataPath", defaultChipDataPath, "Path to write chip data").Value;

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

        private static string _saveSaveName;
        private static string _saveChipDirectory;

        /**
         * Returns the chipdata subfolder for the save, or null at the main menu
         */
        private static string GetSaveDirectory()
        {
            var saveName = XmlSaveLoad.Instance?.CurrentStationName;
            if (saveName == _saveSaveName)
            {
                return _saveChipDirectory;
            }
            _saveSaveName = saveName;

            if (string.IsNullOrEmpty(saveName))
            {
                _saveChipDirectory = null;
            }
            else
            {
                var folderName = saveName;
                foreach (var character in System.IO.Path.GetInvalidFileNameChars())
                {
                    folderName = folderName.Replace(character, '_');
                }
                _saveChipDirectory = System.IO.Path.Combine(_chipDataPathResolved, folderName);
                var message = $"IC10Sync game save `{saveName}` -> `{_saveChipDirectory}`";
                UnityEngine.Debug.Log(message);
                ConsoleWindow.Print(message, ConsoleColor.Yellow);
            }

            // A different save behaves like a fresh start: the game's code is exported, not overwritten by stale files
            codeHashes.Clear();

            return _saveChipDirectory;
        }

        /**
         * Returns true if any chips was loaded => means continue with a shorter delay 
         */
        private bool WriteAllChipData()
        {
            try
            {

                string directory = GetSaveDirectory();
                if (directory == null)
                {
                    return false; // no game save is loaded
                }

                if (!System.IO.Directory.Exists(directory))
                {
                    System.IO.Directory.CreateDirectory(directory);
                }

                // In survival mode, only update chips if allowed by config
                var isCreative = WorldManager.Instance != null && WorldManager.Instance.GameMode == GameMode.Creative;
                if (isCreative || _allowDirectChipEditingInSurvival)
                {
                    // Find all ProgrammableChip instances in the scene
                    var chips = FindObjectsOfType<ProgrammableChip>();

                    foreach (var chip in chips)
                    {
                        if (chip == null)
                        {
                            continue;
                        }
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
                    if (motherboard == null)
                    {
                        continue;
                    }
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