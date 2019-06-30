﻿// <copyright file="ResolverVer1_1.cs" company="Google Inc.">
// Copyright (C) 2015 Google Inc. All Rights Reserved.
//
//  Licensed under the Apache License, Version 2.0 (the "License");
//  you may not use this file except in compliance with the License.
//  You may obtain a copy of the License at
//
//  http://www.apache.org/licenses/LICENSE-2.0
//
//  Unless required by applicable law or agreed to in writing, software
//  distributed under the License is distributed on an "AS IS" BASIS,
//  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//  See the License for the specific language governing permissions and
//    limitations under the License.
// </copyright>

namespace GooglePlayServices
{
    using UnityEngine;
    using UnityEditor;
    using System.Collections.Generic;
    using System.Collections.Specialized;
    using Google;
    using Google.JarResolver;
    using System;
    using System.Collections;
    using System.IO;
    using System.Text.RegularExpressions;
    using System.Xml;

    public class GradleResolver
    {
        // Caches data associated with an aar so that it doesn't need to be queried to determine
        // whether it should be expanded / exploded if it hasn't changed.
        private class AarExplodeData
        {
            // Identifier for an ABI independent AAR.
            public const string ABI_UNIVERSAL = "universal";
            // Time the file was modified the last time it was inspected.
            public System.DateTime modificationTime;
            // Whether the AAR file should be expanded / exploded.
            public bool explode = false;
            // Project's bundle ID when this was expanded.
            public string bundleId = "";
            // Path of the target AAR package.
            public string path = "";
            // Comma separated string that lists the set of *available* ABIs in the source archive.
            // This is ABI_UNIVERSAL if the archive does not contain any native libraries.
            public string availableAbis = ABI_UNIVERSAL;
            // Comma separated string that lists the set of ABIs in the archive.
            // This is ABI_UNIVERSAL if the archive does not contain any native libraries.
            public string targetAbis = ABI_UNIVERSAL;
            // Whether gradle is selected as the build system.
            public bool gradleBuildSystem = PlayServicesResolver.GradleBuildEnabled;
            // Whether gradle export is enabled.
            public bool gradleExport = PlayServicesResolver.GradleProjectExportEnabled;
            // Whether the gradle template is enabled.
            public bool gradleTemplate = PlayServicesResolver.GradleTemplateEnabled;
            // AAR version that should be ignored when attempting to overwrite an existing
            // dependency.  This is reset when the dependency is updated to a version different
            // to this.
            // NOTE: This is not considered in AarExplodeDataIsDirty() as we do not want to
            // re-explode an AAR if this changes.
            public string ignoredVersion = "";

            /// <summary>
            /// Convert a comma separated list of ABIs to an AndroidAbis instance.
            /// </summary>
            /// <param name="abiString">String to convert.</param>
            /// <returns>AndroidAbis instance if native components are present,
            /// null otherwise.</returns>
            private static AndroidAbis AndroidAbisFromString(string abiString) {
                return abiString == ABI_UNIVERSAL ? null : new AndroidAbis(abiString);
            }

            /// <summary>
            /// Convert an AndroidAbis instance to a comma separated string.
            /// </summary>
            /// <param name="abis">Instance to convert.</param>
            /// <returns>Comma separated string.</returns>
            private static string AndroidAbisToString(AndroidAbis abis) {
                return abis != null ? abis.ToString() : ABI_UNIVERSAL;
            }

            /// <summary>
            /// Get the available native component ABIs in the archive.
            /// If this is a universal archive it returns null.
            /// </summary>
            public AndroidAbis AvailableAbis {
                get { return AndroidAbisFromString(availableAbis); }
                set { availableAbis = AndroidAbisToString(value); }
            }

            /// <summary>
            /// Get the current native component ABIs in the archive.
            /// If this is a universal archive it returns null.
            /// </summary>
            public AndroidAbis TargetAbis {
                get { return AndroidAbisFromString(targetAbis); }
                set { targetAbis = AndroidAbisToString(value); }
            }

            /// <summary>
            /// Default constructor.
            /// </summary>
            public AarExplodeData() {}

            /// <summary>
            /// Copy an instance of this object.
            /// </summary>
            public AarExplodeData(AarExplodeData dataToCopy) {
                modificationTime = dataToCopy.modificationTime;
                explode = dataToCopy.explode;
                bundleId = dataToCopy.bundleId;
                path = dataToCopy.path;
                targetAbis = dataToCopy.targetAbis;
                gradleBuildSystem = dataToCopy.gradleBuildSystem;
                gradleExport = dataToCopy.gradleExport;
                gradleTemplate = dataToCopy.gradleTemplate;
                ignoredVersion = dataToCopy.ignoredVersion;
            }

            /// <summary>
            /// Compare with this object.
            /// </summary>
            /// <param name="obj">Object to compare with.</param>
            /// <returns>true if both objects have the same contents, false otherwise.</returns>
            public override bool Equals(System.Object obj)  {
                var data = obj as AarExplodeData;
                return data != null &&
                    modificationTime == data.modificationTime &&
                    explode == data.explode &&
                    bundleId == data.bundleId &&
                    path == data.path &&
                    availableAbis == data.availableAbis &&
                    targetAbis == data.targetAbis &&
                    gradleBuildSystem == data.gradleBuildSystem &&
                    gradleExport == data.gradleExport &&
                    gradleTemplate == data.gradleTemplate &&
                    ignoredVersion == data.ignoredVersion;
            }

            /// <summary>
            /// Generate a hash of this object.
            /// </summary>
            /// <returns></returns>
            public override int GetHashCode() {
                return modificationTime.GetHashCode() ^
                    explode.GetHashCode() ^
                    bundleId.GetHashCode() ^
                    path.GetHashCode() ^
                    availableAbis.GetHashCode() ^
                    targetAbis.GetHashCode() ^
                    gradleBuildSystem.GetHashCode() ^
                    gradleExport.GetHashCode() ^
                    gradleTemplate.GetHashCode() ^
                    ignoredVersion.GetHashCode();
            }

            /// <summary>
            /// Copy AAR explode data.
            /// </summary>
            public static Dictionary<string, AarExplodeData> CopyDictionary(
                    Dictionary<string, AarExplodeData> dataToCopy) {
                var copy = new Dictionary<string, AarExplodeData>();
                foreach (var item in dataToCopy) {
                    copy[item.Key] = new AarExplodeData(item.Value);
                }
                return copy;
            }

            /// <summary>
            /// Convert AAR data to a string.
            /// </summary>
            /// <returns>String description of the instance.</returns>
            public override string ToString() {
                return String.Format("modificationTime={0} " +
                                     "explode={1} " +
                                     "bundleId={2} " +
                                     "path={3} " +
                                     "availableAbis={4} " +
                                     "targetAbis={5} " +
                                     "gradleBuildSystem={6} " +
                                     "gradleExport={7} " +
                                     "gradleTemplate={8}",
                                     modificationTime, explode, bundleId, path, availableAbis,
                                     targetAbis, gradleBuildSystem, gradleExport, gradleTemplate);
            }
        }

        // Data that should be stored in the explode cache.
        private Dictionary<string, AarExplodeData> aarExplodeData =
            new Dictionary<string, AarExplodeData>();
        // Data currently stored in the explode cache.
        private Dictionary<string, AarExplodeData> aarExplodeDataSaved =
            new Dictionary<string, AarExplodeData>();
        // File used to to serialize aarExplodeData.  This is required as Unity will reload classes
        // in the editor when C# files are modified.
        private string aarExplodeDataFile = Path.Combine(FileUtils.ProjectTemporaryDirectory,
                                                         "GoogleAarExplodeCache.xml");

        // Directory used to execute Gradle.
        private string gradleBuildDirectory = Path.Combine(FileUtils.ProjectTemporaryDirectory,
                                                           "PlayServicesResolverGradle");

        private const int MajorVersion = 1;
        private const int MinorVersion = 1;
        private const int PointVersion = 0;

        // Characters that are parsed by Gradle / Java in property values.
        // These characters need to be escaped to be correctly interpreted in a property value.
        private static string[] GradlePropertySpecialCharacters = new string[] {
            " ", "\\", "#", "!", "=", ":"
        };

        // Special characters that should not be escaped in URIs for Gradle property values.
        private static HashSet<string> GradleUriExcludeEscapeCharacters = new HashSet<string> {
            ":"
        };

        // Queue of System.Action objects for resolve actions to execute on the main thread.
        private static System.Collections.Queue resolveUpdateQueue = new System.Collections.Queue();
        // Currently active resolution operation.
        private static System.Action resolveActionActive = null;
        // Lock for resolveUpdateQueue and resolveActionActive.
        private static object resolveLock = new object();

        public GradleResolver() {
            RunOnMainThread.Run(LoadAarExplodeCache, false);
        }

        /// <summary>
        /// Compare two dictionaries of AarExplodeData.
        /// </summary>
        private bool CompareExplodeData(Dictionary<string, AarExplodeData> explodeData1,
                                        Dictionary<string, AarExplodeData> explodeData2) {
            if (explodeData1 == explodeData2) return true;
            if (explodeData1 == null || explodeData2 == null) return false;
            if (explodeData1.Count != explodeData2.Count) return false;
            var keys = new HashSet<string>(explodeData1.Keys);
            keys.UnionWith(new HashSet<string>(explodeData2.Keys));
            foreach (var key in keys) {
                AarExplodeData data1;
                AarExplodeData data2;
                if (!(explodeData1.TryGetValue(key, out data1) &&
                      explodeData2.TryGetValue(key, out data2))) {
                    return false;
                }
                if (!data1.Equals(data2)) {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Load data cached in aarExplodeDataFile into aarExplodeData.
        /// </summary>
        private void LoadAarExplodeCache() {
            if (!File.Exists(aarExplodeDataFile)) {
                // Build aarExplodeData from the current set of AARs in the project.
                foreach (string path in PlayServicesResolver.FindLabeledAssets()) {
                    PlayServicesResolver.Log(String.Format("Caching AAR {0} state",
                                                           path), LogLevel.Verbose);
                    ShouldExplode(path);
                }
                return;
            }

            try {
                using (XmlTextReader reader =
                           new XmlTextReader(new StreamReader(aarExplodeDataFile))) {
                    aarExplodeData.Clear();
                    while (reader.Read()) {
                        if (reader.NodeType == XmlNodeType.Element && reader.Name == "aars") {
                            while (reader.Read()) {
                                if (reader.NodeType == XmlNodeType.Element &&
                                    reader.Name == "explodeData") {
                                    string aar = "";
                                    AarExplodeData aarData = new AarExplodeData();
                                    do {
                                        if (!reader.Read()) break;

                                        if (reader.NodeType == XmlNodeType.Element) {
                                            string elementName = reader.Name;
                                            if (reader.Read() &&
                                                reader.NodeType == XmlNodeType.Text) {
                                                if (elementName == "aar") {
                                                    aar = reader.ReadContentAsString();
                                                } else if (elementName == "modificationTime") {
                                                    aarData.modificationTime =
                                                        reader.ReadContentAsDateTime();
                                                } else if (elementName == "explode") {
                                                    aarData.explode = reader.ReadContentAsBoolean();
                                                } else if (elementName == "bundleId") {
                                                    aarData.bundleId = reader.ReadContentAsString();
                                                } else if (elementName == "path") {
                                                    aarData.path = reader.ReadContentAsString();
                                                } else if (elementName == "availableAbis") {
                                                    aarData.availableAbis =
                                                        reader.ReadContentAsString();
                                                } else if (elementName == "targetAbi") {
                                                    aarData.targetAbis =
                                                        reader.ReadContentAsString();
                                                } else if (elementName == "gradleBuildSystem") {
                                                    aarData.gradleBuildSystem =
                                                        reader.ReadContentAsBoolean();
                                                } else if (elementName == "gradleExport") {
                                                    aarData.gradleExport =
                                                        reader.ReadContentAsBoolean();
                                                } else if (elementName == "gradleTemplate") {
                                                    aarData.gradleTemplate =
                                                        reader.ReadContentAsBoolean();
                                                } else if (elementName == "ignoredVersion") {
                                                    aarData.ignoredVersion =
                                                        reader.ReadContentAsString();
                                                }
                                            }
                                        }
                                    } while (!(reader.Name == "explodeData" &&
                                               reader.NodeType == XmlNodeType.EndElement));
                                    if (aar != "" && aarData.path != "") {
                                        aarExplodeData[aar] = aarData;
                                    }
                                }
                            }
                        }
                    }
                    reader.Close();
                }
            } catch (Exception e) {
                PlayServicesResolver.Log(String.Format(
                    "Failed to read AAR cache {0} ({1})\n" +
                    "Auto-resolution will be slower.\n", aarExplodeDataFile, e.ToString()),
                    level: LogLevel.Warning);
            }
            aarExplodeDataSaved = AarExplodeData.CopyDictionary(aarExplodeData);
        }

        /// <summary>
        /// Save data from aarExplodeData into aarExplodeDataFile.
        /// </summary>
        private void SaveAarExplodeCache()
        {
            try {
                if (File.Exists(aarExplodeDataFile))
                {
                    // If the explode data hasn't been modified, don't save.
                    if (CompareExplodeData(aarExplodeData, aarExplodeDataSaved)) return;
                    File.Delete(aarExplodeDataFile);
                }
                using (XmlTextWriter writer =
                       new XmlTextWriter(new StreamWriter(aarExplodeDataFile)) {
                           Formatting = Formatting.Indented
                       }) {
                    writer.WriteStartElement("aars");
                    foreach (KeyValuePair<string, AarExplodeData> kv in aarExplodeData)
                    {
                        writer.WriteStartElement("explodeData");
                        writer.WriteStartElement("aar");
                        writer.WriteValue(kv.Key);
                        writer.WriteEndElement();
                        writer.WriteStartElement("modificationTime");
                        writer.WriteValue(kv.Value.modificationTime);
                        writer.WriteEndElement();
                        writer.WriteStartElement("explode");
                        writer.WriteValue(kv.Value.explode);
                        writer.WriteEndElement();
                        writer.WriteStartElement("bundleId");
                        writer.WriteValue(PlayServicesResolver.GetAndroidApplicationId());
                        writer.WriteEndElement();
                        writer.WriteStartElement("path");
                        writer.WriteValue(kv.Value.path);
                        writer.WriteEndElement();
                        writer.WriteStartElement("availableAbis");
                        writer.WriteValue(kv.Value.availableAbis);
                        writer.WriteEndElement();
                        writer.WriteStartElement("targetAbi");
                        writer.WriteValue(kv.Value.targetAbis);
                        writer.WriteEndElement();
                        writer.WriteStartElement("gradleBuildEnabled");
                        writer.WriteValue(kv.Value.gradleBuildSystem);
                        writer.WriteEndElement();
                        writer.WriteStartElement("gradleExport");
                        writer.WriteValue(kv.Value.gradleExport);
                        writer.WriteEndElement();
                        writer.WriteStartElement("gradleTemplate");
                        writer.WriteValue(kv.Value.gradleTemplate);
                        writer.WriteEndElement();
                        writer.WriteStartElement("ignoredVersion");
                        writer.WriteValue(kv.Value.ignoredVersion);
                        writer.WriteEndElement();
                        writer.WriteEndElement();
                    }
                    writer.WriteEndElement();
                    writer.Flush();
                    writer.Close();
                    aarExplodeDataSaved = AarExplodeData.CopyDictionary(aarExplodeData);
                }
            } catch (Exception e) {
                PlayServicesResolver.Log(String.Format(
                    "Failed to write AAR cache {0} ({1})\n" +
                    "Auto-resolution will be slower after recompilation.\n", aarExplodeDataFile,
                    e.ToString()), level: LogLevel.Warning);
            }
        }

        /// <summary>
        /// Parse output of download_artifacts.gradle into lists of copied and missing artifacts.
        /// </summary>
        /// <param name="output">Standard output of the download_artifacts.gradle.</param>
        /// <param name="destinationDirectory">Directory to artifacts were copied into.</param>
        /// <param name="copiedArtifacts">Returns a list of copied artifact files.</param>
        /// <param name="missingArtifacts">Returns a list of missing artifact
        /// specifications.</param>
        /// <param name="modifiedArtifacts">Returns a list of artifact specifications that were
        /// modified.</param>
        private void ParseDownloadGradleArtifactsGradleOutput(
                string output, string destinationDirectory,
                out List<string> copiedArtifacts, out List<string> missingArtifacts,
                out List<string> modifiedArtifacts) {
            // Parse stdout for copied and missing artifacts.
            copiedArtifacts = new List<string>();
            missingArtifacts = new List<string>();
            modifiedArtifacts = new List<string>();
            string currentHeader = null;
            const string COPIED_ARTIFACTS_HEADER = "Copied artifacts:";
            const string MISSING_ARTIFACTS_HEADER = "Missing artifacts:";
            const string MODIFIED_ARTIFACTS_HEADER = "Modified artifacts:";
            foreach (var line in output.Split(new string[] { "\r\n", "\n" },
                                              StringSplitOptions.None)) {
                if (line.StartsWith(COPIED_ARTIFACTS_HEADER) ||
                    line.StartsWith(MISSING_ARTIFACTS_HEADER) ||
                    line.StartsWith(MODIFIED_ARTIFACTS_HEADER)) {
                    currentHeader = line;
                    continue;
                } else if (String.IsNullOrEmpty(line.Trim())) {
                    currentHeader = null;
                    continue;
                }
                if (!String.IsNullOrEmpty(currentHeader)) {
                    if (currentHeader == COPIED_ARTIFACTS_HEADER) {
                        // Store the POSIX path of the copied package to handle Windows
                        // path variants.
                        copiedArtifacts.Add(
                            FileUtils.PosixPathSeparators(
                                Path.Combine(destinationDirectory, line.Trim())));
                    } else if (currentHeader == MISSING_ARTIFACTS_HEADER) {
                        missingArtifacts.Add(line.Trim());
                    } else if (currentHeader == MODIFIED_ARTIFACTS_HEADER) {
                        modifiedArtifacts.Add(line.Trim());
                    }
                }
            }
        }

        /// <summary>
        /// Log an error with the set of dependencies that were not fetched.
        /// </summary>
        /// <param name="missingArtifacts">List of missing dependencies.</param>
        private void LogMissingDependenciesError(List<string> missingArtifacts) {
            // Log error for missing packages.
            if (missingArtifacts.Count > 0) {
                PlayServicesResolver.Log(
                   String.Format("Resolution failed\n\n" +
                                 "Failed to fetch the following dependencies:\n{0}\n\n",
                                 String.Join("\n", missingArtifacts.ToArray())),
                   level: LogLevel.Error);
            }
        }

        /// <summary>
        /// Escape all special characters in a gradle property value.
        /// </summary>
        /// <param name="value">Value to escape.</param>
        /// <param name="escapeFunc">Function which generates an escaped character.  By default
        /// this adds "\\" to each escaped character.</param>
        /// <param name="charactersToExclude">Characters to exclude from the escaping set.</param>
        /// <returns>Escaped value.</returns>
        private static string EscapeGradlePropertyValue(
                string value, Func<string, string> escapeFunc = null,
                HashSet<string> charactersToExclude = null) {
            if (escapeFunc == null) {
                escapeFunc = (characterToEscape) => { return "\\" + characterToEscape; };
            }
            foreach (var characterToEscape in GradlePropertySpecialCharacters) {
                if (charactersToExclude == null ||
                    !(charactersToExclude.Contains(characterToEscape))) {
                    value = value.Replace(characterToEscape, escapeFunc(characterToEscape));
                }
            }
            return value;
        }

        /// <summary>
        /// Generates a Gradle (Java) properties string from a dictionary of key value pairs.
        /// Details of the format is documented in
        /// http://docs.oracle.com/javase/7/docs/api/java/util/Properties.html#\
        /// store%28java.io.Writer,%20java.lang.String%29
        /// </summary>
        /// <param name="properties">Properties to generate a string from.  Each value must not
        /// contain a newline.</param>
        /// <returns>String with Gradle (Java) properties</returns>
        private string GenerateGradleProperties(Dictionary<string, string> properties) {
            var lines = new List<string>();
            foreach (var kv in properties) {
                var escapedKey = kv.Key.Replace(" ", "\\ ");
                var elementAfterLeadingWhitespace = kv.Value.TrimStart(new [] { ' ' });
                var escapedElement =
                    kv.Value.Substring(elementAfterLeadingWhitespace.Length).Replace(" ", "\\ ") +
                    EscapeGradlePropertyValue(elementAfterLeadingWhitespace);
                lines.Add(String.Format("{0}={1}", escapedKey, escapedElement));
            }
            return String.Join("\n", lines.ToArray());
        }

        /// <summary>
        /// From a list of dependencies generate a list of Maven / Gradle / Ivy package spec
        /// strings.
        /// </summary>
        /// <param name="dependencies">Dependency instances to query for package specs.</param>
        /// <returns>Dictionary of Dependency instances indexed by package spec strings.</returns>
        internal static Dictionary<string, string> DependenciesToPackageSpecs(
                IEnumerable<Dependency> dependencies) {
            var sourcesByPackageSpec = new Dictionary<string, string>();
            foreach (var dependency in dependencies) {
                // Convert the legacy "LATEST" version spec to a Gradle version spec.
                var packageSpec = dependency.Version.ToUpper() == "LATEST" ?
                    dependency.VersionlessKey + ":+" : dependency.Key;
                var source = CommandLine.SplitLines(dependency.CreatedBy)[0];
                string sources;
                if (sourcesByPackageSpec.TryGetValue(packageSpec, out sources)) {
                    sources = sources + ", " + source;
                } else {
                    sources = source;
                }
                sourcesByPackageSpec[packageSpec] = sources;
            }
            return sourcesByPackageSpec;
        }

        /// <summary>
        /// Convert a repo path to a valid URI.
        /// If the specified repo is a local directory and it doesn't exist, search the project
        /// for a match.
        /// </summary>
        /// <param name="repoPath">Repo path to convert.</param>
        /// <param name="sourceLocation>XML or source file this path is referenced from. If this is
        /// null the calling method's source location is used when logging the source of this
        /// repo declaration.</param>
        /// <returns>URI to the repo.</returns>
        internal static string RepoPathToUri(string repoPath, string sourceLocation=null) {
            if (sourceLocation == null) {
                // Get the caller's stack frame.
                sourceLocation = System.Environment.StackTrace.Split(new char[] { '\n' })[1];
            }
            // Filter Android SDK repos as they're supplied in the build script.
            if (repoPath.StartsWith(PlayServicesSupport.SdkVariable)) return null;
            // Since we need a URL, determine whether the repo has a scheme.  If not,
            // assume it's a local file.
            bool validScheme = false;
            foreach (var scheme in new [] { "file:", "http:", "https:" }) {
                validScheme |= repoPath.StartsWith(scheme);
            }
            if (!validScheme) {
                // If the directory isn't found, it is possible the user has moved the repository
                // in the project, so try searching for it.
                string searchDir = "Assets" + Path.DirectorySeparatorChar;
                if (!Directory.Exists(repoPath) &&
                    FileUtils.NormalizePathSeparators(repoPath.ToLower()).StartsWith(
                        searchDir.ToLower())) {
                    var foundPath = FileUtils.FindPathUnderDirectory(
                        searchDir, repoPath.Substring(searchDir.Length));
                    string warningMessage;
                    if (!String.IsNullOrEmpty(foundPath)) {
                        repoPath = searchDir + foundPath;
                        warningMessage = String.Format(
                            "{0}: Repo path '{1}' does not exist, will try using '{2}' instead.",
                            sourceLocation, repoPath, foundPath);
                    } else {
                        warningMessage = String.Format(
                            "{0}: Repo path '{1}' does not exist.", sourceLocation, repoPath);
                    }
                    PlayServicesResolver.Log(warningMessage, level: LogLevel.Warning);
                }

                repoPath = PlayServicesResolver.FILE_SCHEME +
                    FileUtils.PosixPathSeparators(Path.GetFullPath(repoPath));
            }
            // Escape the URI to handle special characters like spaces and percent escape
            // all characters that are interpreted by gradle.
            return EscapeGradlePropertyValue(Uri.EscapeUriString(repoPath),
                                             escapeFunc: Uri.EscapeDataString,
                                             charactersToExclude: GradleUriExcludeEscapeCharacters);
        }

        /// <summary>
        /// Extract the ordered set of repository URIs from the specified dependencies.
        /// </summary>
        /// <param name="dependencies">Dependency instances to query for repos.</param>
        /// <returns>Dictionary of source filenames by repo names.</returns>
        internal static List<KeyValuePair<string, string>> DependenciesToRepoUris(
                IEnumerable<Dependency> dependencies) {
            var sourcesByRepo = new OrderedDictionary();
            Action<string, string> addToSourcesByRepo = (repo, source) => {
                if (!String.IsNullOrEmpty(repo)) {
                    if (sourcesByRepo.Contains(repo)) {
                        var sources = (List<string>)sourcesByRepo[repo];
                        if (!sources.Contains(source)) {
                            sources.Add(source);
                        }
                    } else {
                        sourcesByRepo[repo] = new List<string>() { source };
                    }
                }
            };
            // Add global repos first.
            foreach (var kv in PlayServicesSupport.AdditionalRepositoryPaths) {
                addToSourcesByRepo(RepoPathToUri(kv.Key, sourceLocation: kv.Value), kv.Value);
            }
            // Build array of repos to search, they're interleaved across all dependencies as the
            // order matters.
            int maxNumberOfRepos = 0;
            foreach (var dependency in dependencies) {
                maxNumberOfRepos = Math.Max(maxNumberOfRepos, dependency.Repositories.Length);
            }
            for (int i = 0; i < maxNumberOfRepos; i++) {
                foreach (var dependency in dependencies) {
                    var repos = dependency.Repositories;
                    if (i >= repos.Length) continue;
                    var createdBy = CommandLine.SplitLines(dependency.CreatedBy)[0];
                    addToSourcesByRepo(RepoPathToUri(repos[i], sourceLocation: createdBy),
                                       createdBy);
                }
            }
            var sourcesByRepoList = new List<KeyValuePair<string, string>>();
            var enumerator = sourcesByRepo.GetEnumerator();
            while (enumerator.MoveNext()) {
                sourcesByRepoList.Add(
                    new KeyValuePair<string, string>(
                        (string)enumerator.Key,
                        String.Join(", ", ((List<string>)enumerator.Value).ToArray())));
            }
            return sourcesByRepoList;
        }

        // Holds Gradle resolution state.
        private class ResolutionState {
            public CommandLine.Result commandLineResult = new CommandLine.Result();
            public List<string> copiedArtifacts = new List<string>();
            public HashSet<string> copiedArtifactsSet = new HashSet<string>();
            public List<string> missingArtifacts = new List<string>();
            public List<Dependency> missingArtifactsAsDependencies = new List<Dependency>();
            public List<string> modifiedArtifacts = new List<string>();
            public bool errorOrWarningLogged = false;
            public bool aarsProcessed = false;
        }

        /// <summary>
        /// Perform resolution using Gradle.
        /// </summary>
        /// <param name="destinationDirectory">Directory to copy packages into.</param>
        /// <param name="androidSdkPath">Path to the Android SDK.  This is required as
        /// PlayServicesSupport.SDK can only be called from the main thread.</param>
        /// <param name="logErrorOnMissingArtifacts">Log errors when artifacts are missing.</param>
        /// <param name="closeWindowOnCompletion">Whether to unconditionally close the resolution
        /// window when complete.</param>
        /// <param name="resolutionComplete">Called when resolution is complete with a list of
        /// packages that were not found.</param>
        private void GradleResolution(
                string destinationDirectory, string androidSdkPath,
                bool logErrorOnMissingArtifacts, bool closeWindowOnCompletion,
                System.Action<List<Dependency>> resolutionComplete) {
            // Namespace for resources under the src/scripts directory embedded within this
            // assembly.
            const string EMBEDDED_RESOURCES_NAMESPACE = "PlayServicesResolver.scripts.";
            var gradleWrapper = Path.GetFullPath(Path.Combine(
                gradleBuildDirectory,
                UnityEngine.RuntimePlatform.WindowsEditor == UnityEngine.Application.platform ?
                    "gradlew.bat" : "gradlew"));
            var buildScript = Path.GetFullPath(Path.Combine(
                gradleBuildDirectory, EMBEDDED_RESOURCES_NAMESPACE + "download_artifacts.gradle"));
            // Get all dependencies.
            var allDependencies = PlayServicesSupport.GetAllDependencies();
            var allDependenciesList = new List<Dependency>(allDependencies.Values);

            // Extract Gradle wrapper and the build script to the build directory.
            if (!(Directory.Exists(gradleBuildDirectory) && File.Exists(gradleWrapper) &&
                  File.Exists(buildScript))) {
                var gradleTemplateZip = Path.Combine(
                    gradleBuildDirectory, EMBEDDED_RESOURCES_NAMESPACE + "gradle-template.zip");
                foreach (var resource in new [] { gradleTemplateZip, buildScript }) {
                    ExtractResource(Path.GetFileName(resource), resource);
                }
                if (!PlayServicesResolver.ExtractZip(gradleTemplateZip, new [] {
                            "gradle/wrapper/gradle-wrapper.jar",
                            "gradle/wrapper/gradle-wrapper.properties",
                            "gradlew",
                            "gradlew.bat"}, gradleBuildDirectory)) {
                    PlayServicesResolver.Log(
                       String.Format("Unable to extract Gradle build component {0}\n\n" +
                                     "Resolution failed.", gradleTemplateZip),
                       level: LogLevel.Error);
                    resolutionComplete(allDependenciesList);
                    return;
                }
                // Files extracted from the zip file don't have the executable bit set on some
                // platforms, so set it here.
                // Unfortunately, File.GetAccessControl() isn't implemented, so we'll use
                // chmod (OSX / Linux) and on Windows extracted files are executable by default
                // so we do nothing.
                if (UnityEngine.RuntimePlatform.WindowsEditor !=
                    UnityEngine.Application.platform) {
                    var result = CommandLine.Run("chmod",
                                                 String.Format("ug+x \"{0}\"", gradleWrapper));
                    if (result.exitCode != 0) {
                        PlayServicesResolver.Log(
                            String.Format("Failed to make \"{0}\" executable.\n\n" +
                                          "Resolution failed.\n\n{1}", gradleWrapper,
                                          result.message),
                            level: LogLevel.Error);
                        resolutionComplete(allDependenciesList);
                        return;
                    }
                }
            }

            // Build array of repos to search, they're interleaved across all dependencies as the
            // order matters.
            var repoList = new List<string>();
            foreach (var kv in DependenciesToRepoUris(allDependencies.Values)) repoList.Add(kv.Key);

            // Create an instance of ResolutionState to aggregate the results.
            var resolutionState = new ResolutionState();

            // Window used to display resolution progress.
            var window = CommandLineDialog.CreateCommandLineDialog(
                "Resolving Android Dependencies");

            // Register an event to redirect log messages to the resolution window.
            Google.Logger.LogMessageDelegate logToWindow = (message, logLevel) => {
                string messagePrefix;
                switch (logLevel) {
                    case LogLevel.Error:
                        messagePrefix = "ERROR: ";
                        resolutionState.errorOrWarningLogged = true;
                        break;
                    case LogLevel.Warning:
                        messagePrefix = "WARNING: ";
                        resolutionState.errorOrWarningLogged = true;
                        break;
                    default:
                        messagePrefix = "";
                        break;
                }
                if (!window.RunningCommand) {
                    window.AddBodyText(messagePrefix + message + "\n");
                }
            };
            PlayServicesResolver.logger.LogMessage += logToWindow;

            // When resolution is complete unregister the log redirection event.
            Action resolutionCompleteRestoreLogger = () => {
                PlayServicesResolver.logger.LogMessage -= logToWindow;
                // If the command completed successfully or the log level is info or above close
                // the window, otherwise leave it open for inspection.
                if ((resolutionState.commandLineResult.exitCode == 0 &&
                     PlayServicesResolver.logger.Level >= LogLevel.Info &&
                     !resolutionState.errorOrWarningLogged) || closeWindowOnCompletion) {
                    window.Close();
                }
                resolutionComplete(resolutionState.missingArtifactsAsDependencies);
            };

            // Executed after refreshing the explode cache.
            Action processAars = () => {
                // Find all labeled files that were not copied and delete them.
                var staleArtifacts = new HashSet<string>();
                foreach (var assetPath in PlayServicesResolver.FindLabeledAssets()) {
                    if (!resolutionState.copiedArtifactsSet.Contains(
                            FileUtils.PosixPathSeparators(assetPath))) {
                        staleArtifacts.Add(assetPath);
                    }
                }
                if (staleArtifacts.Count > 0) {
                    PlayServicesResolver.Log(
                        String.Format("Deleting stale dependencies:\n{0}",
                                      String.Join("\n",
                                                  (new List<string>(staleArtifacts)).ToArray())),
                        level: LogLevel.Verbose);
                    var deleteFailures = new List<string>();
                    foreach (var assetPath in staleArtifacts) {
                        deleteFailures.AddRange(FileUtils.DeleteExistingFileOrDirectory(assetPath));
                    }
                    var deleteError = FileUtils.FormatError("Failed to delete stale artifacts",
                                                            deleteFailures);
                    if (!String.IsNullOrEmpty(deleteError)) {
                        PlayServicesResolver.Log(deleteError, level: LogLevel.Error);
                    }
                }
                // Process / explode copied AARs.
                ProcessAars(
                    destinationDirectory, new HashSet<string>(resolutionState.copiedArtifacts),
                    (progress, message) => {
                        window.SetProgress("Processing libraries...", progress, message);
                    },
                    () => {
                        // Look up the original Dependency structure for each missing artifact.
                        resolutionState.missingArtifactsAsDependencies = new List<Dependency>();
                        foreach (var artifact in resolutionState.missingArtifacts) {
                            Dependency dep;
                            if (!allDependencies.TryGetValue(artifact, out dep)) {
                                // If this fails, something may have gone wrong with the Gradle
                                // script.  Rather than failing hard, fallback to recreating the
                                // Dependency class with the partial data we have now.
                                var components = new List<string>(
                                    artifact.Split(new char[] { ':' }));
                                if (components.Count < 2) {
                                    PlayServicesResolver.Log(
                                        String.Format(
                                            "Found invalid missing artifact {0}\n" +
                                            "Something went wrong with the gradle artifact " +
                                            "download script\n." +
                                            "Please report a bug", artifact),
                                        level: LogLevel.Warning);
                                    continue;
                                } else if (components.Count < 3 || components[2] == "+") {
                                    components.Add("LATEST");
                                }
                                dep = new Dependency(components[0], components[1], components[2]);
                            }
                            resolutionState.missingArtifactsAsDependencies.Add(dep);
                        }
                        if (logErrorOnMissingArtifacts) {
                            LogMissingDependenciesError(resolutionState.missingArtifacts);
                        }
                        resolutionCompleteRestoreLogger();
                    });
            };

            // Executed after labeling copied assets.
            Action refreshExplodeCache = () => {
                // Poke the explode cache for each copied file and add the exploded paths to the
                // output list set.
                resolutionState.copiedArtifactsSet =
                    new HashSet<string>(resolutionState.copiedArtifacts);
                var numberOfCopiedArtifacts = resolutionState.copiedArtifacts.Count;
                var artifactsToInspect = new List<string>(resolutionState.copiedArtifacts);
                if (numberOfCopiedArtifacts > 0) {
                    RunOnMainThread.PollOnUpdateUntilComplete(() => {
                            var remaining = artifactsToInspect.Count;
                            if (remaining == 0) {
                                if (!resolutionState.aarsProcessed) {
                                    resolutionState.aarsProcessed = true;
                                    processAars();
                                }
                                return true;
                            }
                            var artifact = artifactsToInspect[0];
                            artifactsToInspect.RemoveAt(0);
                            window.SetProgress("Inspecting libraries...",
                                               (float)(numberOfCopiedArtifacts - remaining) /
                                               (float)numberOfCopiedArtifacts, artifact);
                            if (ShouldExplode(artifact)) {
                                resolutionState.copiedArtifactsSet.Add(
                                    DetermineExplodedAarPath(artifact));
                            }
                            return false;
                        });
                }
            };

            // Executed when Gradle finishes execution.
            CommandLine.CompletionHandler gradleComplete = (commandLineResult) => {
                resolutionState.commandLineResult = commandLineResult;
                if (commandLineResult.exitCode != 0) {
                    resolutionState.missingArtifactsAsDependencies = allDependenciesList;
                    PlayServicesResolver.Log(
                        String.Format("Gradle failed to fetch dependencies.\n\n{0}",
                                      commandLineResult.message), level: LogLevel.Error);
                    resolutionCompleteRestoreLogger();
                    return;
                }
                // Parse stdout for copied and missing artifacts.
                ParseDownloadGradleArtifactsGradleOutput(commandLineResult.stdout,
                                                         destinationDirectory,
                                                         out resolutionState.copiedArtifacts,
                                                         out resolutionState.missingArtifacts,
                                                         out resolutionState.modifiedArtifacts);
                // Display a warning about modified artifact versions.
                if (resolutionState.modifiedArtifacts.Count > 0) {
                    PlayServicesResolver.Log(
                        String.Format(
                            "Some conflicting dependencies were found.\n" +
                            "The following dependency versions were modified:\n" +
                            "{0}\n",
                            String.Join("\n", resolutionState.modifiedArtifacts.ToArray())),
                        level: LogLevel.Warning);
                }
                // Label all copied files.
                PlayServicesResolver.LabelAssets(
                    resolutionState.copiedArtifacts,
                    complete: (unusedUnlabeled) => {
                        // Check copied files for Jetpack (AndroidX) libraries.
                        if (PlayServicesResolver.FilesContainJetpackLibraries(
                            resolutionState.copiedArtifacts)) {
                            bool jetifierEnabled = SettingsDialog.UseJetifier;
                            SettingsDialog.UseJetifier = true;
                            // Make sure Jetpack is supported, prompting the user to configure Unity
                            // in a supported configuration.
                            if (PlayServicesResolver.CanEnableJetifierOrPromptUser(
                                    "Jetpack (AndroidX) libraries detected, ")) {
                                if (jetifierEnabled != SettingsDialog.UseJetifier) {
                                    PlayServicesResolver.Log(
                                        "Detected Jetpack (AndroidX) libraries, enabled the " +
                                        "jetifier and resolving again.");
                                    // Run resolution again with the Jetifier enabled.
                                    PlayServicesResolver.DeleteLabeledAssets();
                                    GradleResolution(destinationDirectory,
                                                     androidSdkPath,
                                                     logErrorOnMissingArtifacts,
                                                     closeWindowOnCompletion,
                                                     resolutionComplete);
                                    return;
                                }
                            } else {
                                // If the user didn't change their configuration, delete all
                                // resolved libraries and abort resolution as the build will fail.
                                PlayServicesResolver.DeleteLabeledAssets();
                                resolutionState.missingArtifactsAsDependencies =
                                    allDependenciesList;
                                resolutionCompleteRestoreLogger();
                                return;
                            }
                        }
                        // Successful, proceed with processing libraries.
                        refreshExplodeCache();
                    },
                    synchronous: false,
                    progressUpdate: (progress, message) => {
                        window.SetProgress("Labeling libraries...", progress, message);
                    });
            };

            var packageSpecs =
                new List<string>(DependenciesToPackageSpecs(allDependencies.Values).Keys);

            var androidGradlePluginVersion = PlayServicesResolver.AndroidGradlePluginVersion;
            // If this version of Unity doesn't support Gradle builds use a relatively
            // recent (June 2019) version of the data binding library.
            if (String.IsNullOrEmpty(androidGradlePluginVersion)) {
                androidGradlePluginVersion = "2.3.0";
            }

            var gradleProjectProperties = new Dictionary<string, string>() {
                { "ANDROID_HOME", androidSdkPath },
                { "TARGET_DIR", Path.GetFullPath(destinationDirectory) },
                { "MAVEN_REPOS", String.Join(";", repoList.ToArray()) },
                { "PACKAGES_TO_COPY", String.Join(";", packageSpecs.ToArray()) },
                { "USE_JETIFIER", SettingsDialog.UseJetifier ? "1" : "0" },
                { "DATA_BINDING_VERSION", androidGradlePluginVersion }
            };
            var gradleArguments = new List<string> {
                String.Format("-b \"{0}\"", buildScript),
                SettingsDialog.UseGradleDaemon ? "--daemon" : "--no-daemon",
            };
            foreach (var kv in gradleProjectProperties) {
                gradleArguments.Add(String.Format("\"-P{0}={1}\"", kv.Key, kv.Value));
            }
            var gradleArgumentsString = String.Join(" ", gradleArguments.ToArray());

            // Generate gradle.properties to set properties in the script rather than using
            // the command line.
            // Some users of Windows 10 systems have had issues running the Gradle resolver
            // which is suspected to be caused by command line argument parsing.
            // Using both gradle.properties and properties specified via command line arguments
            // works fine.
            File.WriteAllText(Path.Combine(gradleBuildDirectory, "gradle.properties"),
                              GenerateGradleProperties(gradleProjectProperties));

            PlayServicesResolver.Log(String.Format("Running dependency fetching script\n" +
                                                   "\n" +
                                                   "{0} {1}\n",
                                                   gradleWrapper, gradleArgumentsString),
                                     level: LogLevel.Verbose);

            // Run the build script to perform the resolution popping up a window in the editor.
            window.summaryText = "Resolving Android Dependencies...";
            window.modal = false;
            window.progressTitle = window.summaryText;
            window.autoScrollToBottom = true;
            window.logger = PlayServicesResolver.logger;
            var maxProgressLines = (allDependenciesList.Count * 10) + 50;
            window.RunAsync(gradleWrapper, gradleArgumentsString,
                            (result) => { RunOnMainThread.Run(() => { gradleComplete(result); }); },
                            workingDirectory: gradleBuildDirectory,
                            maxProgressLines: maxProgressLines);
            window.Show();
        }

        /// <summary>
        /// Search the project for AARs & JARs that could conflict with each other and resolve
        /// the conflicts if possible.
        /// </summary>
        ///
        /// This handles the following cases:
        /// 1. If any libraries present match the name play-services-* and google-play-services.jar
        ///    is included in the project the user will be warned of incompatibility between
        ///    the legacy JAR and the newer AAR libraries.
        /// 2. If a managed (labeled) library conflicting with one or more versions of unmanaged
        ///    (e.g play-services-base-10.2.3.aar (managed) vs. play-services-10.2.2.aar (unmanaged)
        ///     and play-services-base-9.2.4.aar (unmanaged))
        ///    The user is warned about the unmanaged conflicting libraries and, if they're
        ///    older than the managed library, prompted to delete the unmanaged libraries.
        private void FindAndResolveConflicts() {
            Func<string, string> getVersionlessArtifactFilename = (filename) => {
                var basename = Path.GetFileName(filename);
                int split = basename.LastIndexOf("-");
                return split >= 0 ? basename.Substring(0, split) : basename;
            };
            var managedPlayServicesArtifacts = new List<string>();
            // Gather artifacts managed by the resolver indexed by versionless name.
            var managedArtifacts = new Dictionary<string, string>();
            var managedArtifactFilenames = new HashSet<string>();
            foreach (var filename in PlayServicesResolver.FindLabeledAssets()) {
                var artifact = getVersionlessArtifactFilename(filename);
                // Ignore non-existent files as it's possible for the asset database to reference
                // missing files if it hasn't been refreshed or completed a refresh.
                if (File.Exists(filename) || Directory.Exists(filename)) {
                    managedArtifacts[artifact] = filename;
                    if (artifact.StartsWith("play-services-") ||
                        artifact.StartsWith("com.google.android.gms.play-services-")) {
                        managedPlayServicesArtifacts.Add(filename);
                    }
                }
            }
            managedArtifactFilenames.UnionWith(managedArtifacts.Values);

            // Gather all artifacts (AARs, JARs) that are not managed by the resolver.
            var unmanagedArtifacts = new Dictionary<string, List<string>>();
            var packagingExtensions = new HashSet<string>(Dependency.Packaging);
            // srcaar files are ignored by Unity so are not included in the build.
            packagingExtensions.Remove(".srcaar");
            // List of paths to the legacy google-play-services.jar
            var playServicesJars = new List<string>();
            const string playServicesJar = "google-play-services.jar";
            foreach (var assetGuid in AssetDatabase.FindAssets("t:Object")) {
                var filename = AssetDatabase.GUIDToAssetPath(assetGuid);
                // Ignore all assets that are managed by the plugin and, since the asset database
                // could be stale at this point, check the file exists.
                if (!managedArtifactFilenames.Contains(filename) &&
                    (File.Exists(filename) || Directory.Exists(filename))) {
                    if (Path.GetFileName(filename).ToLower() == playServicesJar) {
                        playServicesJars.Add(filename);
                    } else if (packagingExtensions.Contains(
                                   Path.GetExtension(filename).ToLower())) {
                        var versionlessFilename = getVersionlessArtifactFilename(filename);
                        List<string> existing;
                        var unmanaged = unmanagedArtifacts.TryGetValue(
                            versionlessFilename, out existing) ? existing : new List<string>();
                        unmanaged.Add(filename);
                        unmanagedArtifacts[versionlessFilename] = unmanaged;
                    }
                }
            }

            // Check for conflicting Play Services versions.
            // It's not possible to resolve this automatically as google-play-services.jar used to
            // include all libraries so we don't know the set of components the developer requires.
            if (managedPlayServicesArtifacts.Count > 0 && playServicesJars.Count > 0) {
                PlayServicesResolver.Log(
                    String.Format(
                        "Legacy {0} found!\n\n" +
                        "Your application will not build in the current state.\n" +
                        "{0} library (found in the following locations):\n" +
                        "{1}\n" +
                        "\n" +
                        "{0} is incompatible with plugins that use newer versions of Google\n" +
                        "Play services (conflicting libraries in the following locations):\n" +
                        "{2}\n" +
                        "\n" +
                        "To resolve this issue find the plugin(s) that use\n" +
                        "{0} and either add newer versions of the required libraries or\n" +
                        "contact the plugin vendor to do so.\n\n",
                        playServicesJar, String.Join("\n", playServicesJars.ToArray()),
                        String.Join("\n", managedPlayServicesArtifacts.ToArray())),
                    level: LogLevel.Warning);
            }

            // For each managed artifact aggregate the set of conflicting unmanaged artifacts.
            var conflicts = new Dictionary<string, List<string>>();
            foreach (var managed in managedArtifacts) {
                List<string> unmanagedFilenames;
                if (unmanagedArtifacts.TryGetValue(managed.Key, out unmanagedFilenames)) {
                    // Found a conflict
                    List<string> existingConflicts;
                    var unmanagedConflicts = conflicts.TryGetValue(
                            managed.Value, out existingConflicts) ?
                        existingConflicts : new List<string>();
                    unmanagedConflicts.AddRange(unmanagedFilenames);
                    conflicts[managed.Value] = unmanagedConflicts;
                }
            }

            // Warn about each conflicting version and attempt to resolve each conflict by removing
            // older unmanaged versions.
            Func<string, string> getVersionFromFilename = (filename) => {
                string basename = Path.GetFileNameWithoutExtension(Path.GetFileName(filename));
                return basename.Substring(getVersionlessArtifactFilename(basename).Length + 1);
            };
            foreach (var conflict in conflicts) {
                var currentVersion = getVersionFromFilename(conflict.Key);
                var conflictingVersionsSet = new HashSet<string>();
                foreach (var conflictFilename in conflict.Value) {
                    conflictingVersionsSet.Add(getVersionFromFilename(conflictFilename));
                }
                var conflictingVersions = new List<string>(conflictingVersionsSet);
                conflictingVersions.Sort(Dependency.versionComparer);

                var warningMessage = String.Format(
                    "Found conflicting Android library {0}\n" +
                    "\n" +
                    "{1} (managed by the Android Resolver) conflicts with:\n" +
                    "{2}\n",
                    getVersionlessArtifactFilename(conflict.Key),
                    conflict.Key, String.Join("\n", conflict.Value.ToArray()));

                // If the conflicting versions are older than the current version we can
                // possibly clean up the old versions automatically.
                if (Dependency.versionComparer.Compare(conflictingVersions[0],
                                                       currentVersion) >= 0) {
                    if (EditorUtility.DisplayDialog(
                            "Resolve Conflict?",
                            warningMessage +
                            "\n" +
                            "The conflicting libraries are older than the library managed by " +
                            "the Android Resolver.  Would you like to remove the old libraries " +
                            "to resolve the conflict?",
                            "Yes", "No")) {
                        var deleteFailures = new List<string>();
                        foreach (var filename in conflict.Value) {
                            deleteFailures.AddRange(
                                FileUtils.DeleteExistingFileOrDirectory(filename));
                        }
                        var deleteError = FileUtils.FormatError("Unable to delete old libraries",
                                                                deleteFailures);
                        if (!String.IsNullOrEmpty(deleteError)) {
                            PlayServicesResolver.Log(deleteError, level: LogLevel.Error);
                        }
                        warningMessage = null;
                    }
                }

                if (!String.IsNullOrEmpty(warningMessage)) {
                    PlayServicesResolver.Log(
                        warningMessage +
                        "\n" +
                        "Your application is unlikely to build in the current state.\n" +
                        "\n" +
                        "To resolve this problem you can try one of the following:\n" +
                        "* Updating the dependencies managed by the Android Resolver\n" +
                        "  to remove references to old libraries.  Be careful to not\n" +
                        "  include conflicting versions of Google Play services.\n" +
                        "* Contacting the plugin vendor(s) with conflicting\n" +
                        "  dependencies and asking them to update their plugin.\n",
                        level: LogLevel.Warning);
                }
            }
        }

        /// <summary>
        /// Perform the resolution and the exploding/cleanup as needed.
        /// </summary>
        /// <param name="destinationDirectory">Destination directory.</param>
        /// <param name="closeWindowOnCompletion">Whether to unconditionally close the resolution
        /// window when complete.</param>
        /// <param name="resolutionComplete">Delegate called when resolution is complete.</param>
        public void DoResolution(string destinationDirectory, bool closeWindowOnCompletion,
                                 System.Action resolutionComplete) {
            // Run resolution on the main thread to serialize the operation as DoResolutionUnsafe
            // is not thread safe.
            System.Action resolve = () => {
                System.Action unlockResolveAndSignalResolutionComplete = () => {
                    FindAndResolveConflicts();
                    lock (resolveLock) {
                        resolveActionActive = null;
                    }
                    resolutionComplete();
                };
                DoResolutionUnsafe(destinationDirectory, closeWindowOnCompletion,
                                   unlockResolveAndSignalResolutionComplete);
            };
            lock (resolveLock) {
                resolveUpdateQueue.Enqueue(resolve);
                RunOnMainThread.Run(UpdateTryResolution);
            }
        }

        // Try executing a resolution.
        private static void UpdateTryResolution() {
            lock (resolveLock) {
                if (resolveActionActive == null) {
                    if (resolveUpdateQueue.Count > 0) {
                        resolveActionActive = (System.Action)resolveUpdateQueue.Dequeue();
                        resolveActionActive();
                    }
                }
            }
        }

        /// <summary>
        /// Perform the resolution and the exploding/cleanup as needed.
        /// This is *not* thread safe.
        /// </summary>
        /// <param name="destinationDirectory">Directory to store results of resolution.</param>
        /// <param name="closeWindowOnCompletion">Whether to unconditionally close the resolution
        /// window when complete.</param>
        /// <param name="resolutionComplete">Action called when resolution completes.</param>
        private void DoResolutionUnsafe(string destinationDirectory, bool closeWindowOnCompletion,
                                        System.Action resolutionComplete)
        {
            // Cache the setting as it can only be queried from the main thread.
            var sdkPath = PlayServicesResolver.AndroidSdkRoot;
            // If the Android SDK path isn't set or doesn't exist report an error.
            if (String.IsNullOrEmpty(sdkPath) || !Directory.Exists(sdkPath)) {
                PlayServicesResolver.Log(String.Format(
                    "Android dependency resolution failed, your application will probably " +
                    "not run.\n\n" +
                    "Android SDK path must be set to a valid directory ({0})\n" +
                    "This must be configured in the 'Preference > External Tools > Android SDK'\n" +
                    "menu option.\n", String.IsNullOrEmpty(sdkPath) ? "{none}" : sdkPath),
                    level: LogLevel.Error);
                resolutionComplete();
                return;
            }

            System.Action resolve = () => {
                PlayServicesResolver.Log("Performing Android Dependency Resolution",
                                         LogLevel.Verbose);
                GradleResolution(destinationDirectory, sdkPath, true, closeWindowOnCompletion,
                                 (missingArtifacts) => { resolutionComplete(); });
            };

            System.Action<List<Dependency>> reportOrInstallMissingArtifacts =
                    (List<Dependency> requiredDependencies) => {
                // Set of packages that need to be installed.
                var installPackages = new HashSet<AndroidSdkPackageNameVersion>();
                // Retrieve the set of required packages and whether they're installed.
                var requiredPackages = new Dictionary<string, HashSet<string>>();

                if (requiredDependencies.Count == 0) {
                    resolutionComplete();
                    return;
                }
                foreach (Dependency dependency in requiredDependencies) {
                    PlayServicesResolver.Log(
                        String.Format("Missing Android component {0} (Android SDK Packages: {1})",
                                      dependency.Key, dependency.PackageIds != null ?
                                      String.Join(",", dependency.PackageIds) : "(none)"),
                        level: LogLevel.Verbose);
                    if (dependency.PackageIds != null) {
                        foreach (string packageId in dependency.PackageIds) {
                            HashSet<string> dependencySet;
                            if (!requiredPackages.TryGetValue(packageId, out dependencySet)) {
                                dependencySet = new HashSet<string>();
                            }
                            dependencySet.Add(dependency.Key);
                            requiredPackages[packageId] = dependencySet;
                            // Install / update the Android SDK package that hosts this dependency.
                            installPackages.Add(new AndroidSdkPackageNameVersion {
                                    LegacyName = packageId
                                });
                        }
                    }
                }

                // If no packages need to be installed or Android SDK package installation is
                // disabled.
                if (installPackages.Count == 0 || !SettingsDialog.InstallAndroidPackages) {
                    // Report missing packages as warnings and try to resolve anyway.
                    foreach (var pkg in requiredPackages.Keys) {
                        var packageNameVersion = new AndroidSdkPackageNameVersion {
                            LegacyName = pkg };
                        var depString = System.String.Join(
                            "\n", (new List<string>(requiredPackages[pkg])).ToArray());
                        if (installPackages.Contains(packageNameVersion)) {
                            PlayServicesResolver.Log(
                                String.Format(
                                    "Android SDK package {0} is not installed or out of " +
                                    "date.\n\n" +
                                    "This is required by the following dependencies:\n" +
                                    "{1}", pkg, depString),
                                level: LogLevel.Warning);
                        }
                    }
                    // At this point we've already tried resolving with Gradle.  Therefore,
                    // Android SDK package installation is disabled or not required trying
                    // to resolve again only repeats the same operation we've already
                    // performed.  So we just report report the missing artifacts as an error
                    // and abort.
                    var missingArtifacts = new List<string>();
                    foreach (var dep in requiredDependencies) missingArtifacts.Add(dep.Key);
                    LogMissingDependenciesError(missingArtifacts);
                    resolutionComplete();
                    return;
                }
                InstallAndroidSdkPackagesAndResolve(sdkPath, installPackages,
                                                    requiredPackages, resolve);
            };

            GradleResolution(destinationDirectory, sdkPath,
                             !SettingsDialog.InstallAndroidPackages, closeWindowOnCompletion,
                             reportOrInstallMissingArtifacts);
        }

        /// <summary>
        /// Run the SDK manager to install the specified set of packages then attempt resolution
        /// again.
        /// </summary>
        /// <param name="sdkPath">Path to the Android SDK.</param>
        /// <param name="installPackages">Set of Android SDK packages to install.</param>
        /// <param name="requiredPackages">The set dependencies for each Android SDK package.
        /// This is used to report which dependencies can't be installed if Android SDK package
        /// installation fails.</param>
        /// <param name="resolve">Action that performs resolution.</param>
        private void InstallAndroidSdkPackagesAndResolve(
                string sdkPath, HashSet<AndroidSdkPackageNameVersion> installPackages,
                Dictionary<string, HashSet<string>> requiredPackages, System.Action resolve) {
            // Find / upgrade the Android SDK manager.
            AndroidSdkManager.Create(
                sdkPath,
                (IAndroidSdkManager sdkManager) => {
                    if (sdkManager == null) {
                        PlayServicesResolver.Log(
                            String.Format(
                                "Unable to find the Android SDK manager tool.\n\n" +
                                "The following Required Android packages cannot be installed:\n" +
                                "{0}\n" +
                                "\n" +
                                "{1}\n",
                                AndroidSdkPackageNameVersion.ListToString(installPackages),
                                String.IsNullOrEmpty(sdkPath) ?
                                    PlayServicesSupport.AndroidSdkConfigurationError : ""),
                            level: LogLevel.Error);
                        return;
                    }
                    // Get the set of available and installed packages.
                    sdkManager.QueryPackages(
                        (AndroidSdkPackageCollection packages) => {
                            if (packages == null) return;

                            // Filter the set of packages to install by what is available.
                            foreach (var packageName in requiredPackages.Keys) {
                                var pkg = new AndroidSdkPackageNameVersion {
                                    LegacyName = packageName
                                };
                                var depString = System.String.Join(
                                    "\n",
                                    (new List<string>(requiredPackages[packageName])).ToArray());
                                var availablePackage =
                                    packages.GetMostRecentAvailablePackage(pkg.Name);
                                if (availablePackage == null || !availablePackage.Installed) {
                                    PlayServicesResolver.Log(
                                        String.Format(
                                            "Android SDK package {0} ({1}) {2}\n\n" +
                                            "This is required by the following dependencies:\n" +
                                            "{3}\n", pkg.Name, pkg.LegacyName,
                                            availablePackage != null ?
                                                "not installed or out of date." :
                                                "not available for installation.",
                                            depString),
                                        level: LogLevel.Warning);
                                    if (availablePackage == null) {
                                        installPackages.Remove(pkg);
                                    } else if (!availablePackage.Installed) {
                                        installPackages.Add(availablePackage);
                                    }
                                }
                            }
                            if (installPackages.Count == 0) {
                                resolve();
                                return;
                            }
                            // Start installation.
                            sdkManager.InstallPackages(
                                installPackages, (bool success) => { resolve(); });
                        });
                    });
        }


        /// <summary>
        /// Called during Update to allow the resolver to check any build settings of managed
        /// packages to see whether resolution should be triggered again.
        /// </summary>
        /// <returns>Array of packages that should be re-resolved if resolution should occur,
        /// null otherwise.</returns>
        public string[] OnBuildSettings() {
            // Determine which packages need to be updated.
            List<string> packagesToUpdate = new List<string>();
            List<string> aarsToResolve = new List<string>();
            var aarExplodeDataCopy = new Dictionary<string, AarExplodeData>(aarExplodeData);
            foreach (var kv in aarExplodeDataCopy) {
                var aar = kv.Key;
                var aarData = kv.Value;
                // If the cached file has been removed, ditch it from the cache.
                if (!(File.Exists(aarData.path) || Directory.Exists(aarData.path))) {
                    PlayServicesResolver.Log(String.Format("Found missing AAR {0}", aarData.path),
                                             level: LogLevel.Verbose);
                    aarsToResolve.Add(aar);
                } else if (AarExplodeDataIsDirty(aarData)) {
                    PlayServicesResolver.Log(String.Format("{0} needs to be refreshed ({1})",
                                                           aarData.path, aarData.ToString()),
                                             level: LogLevel.Verbose);
                    packagesToUpdate.Add(aarData.path);
                    aarsToResolve.Add(aar);
                }
            }
            // Remove AARs that will be resolved from the dictionary so the next call to
            // OnBundleId triggers another resolution process.
            foreach (string aar in aarsToResolve) aarExplodeData.Remove(aar);
            SaveAarExplodeCache();
            if (packagesToUpdate.Count == 0) return null;
            string[] packagesToUpdateArray = packagesToUpdate.ToArray();
            PlayServicesResolver.Log(
                String.Format("OnBuildSettings, Packages to update ({0})",
                              String.Join(", ", packagesToUpdateArray)),
                level: LogLevel.Verbose);
            return packagesToUpdateArray;
        }

        /// <summary>
        /// Convert an AAR filename to package name.
        /// </summary>
        /// <param name="aarPath">Path of the AAR to convert.</param>
        /// <returns>AAR package name.</returns>
        private string AarPathToPackageName(string aarPath) {
            var aarFilename = Path.GetFileName(aarPath);
            foreach (var extension in Dependency.Packaging) {
                if (aarPath.EndsWith(extension)) {
                    return aarFilename.Substring(0, aarFilename.Length - extension.Length);
                }
            }
            return aarFilename;
        }

        /// <summary>
        /// Search the cache for an entry associated with the specified AAR path.
        /// </summary>
        /// <param name="aarPath">Path of the AAR to query.</param>
        /// <returns>AarExplodeData if the entry is found in the cache, null otherwise.</returns>
        private AarExplodeData FindAarExplodeDataEntry(string aarPath) {
            var aarFilename = AarPathToPackageName(aarPath);
            AarExplodeData aarData = null;
            // The argument to this method could be the exploded folder rather than the source
            // package (e.g some-package rather than some-package.aar).  Therefore search the
            // cache for the original / unexploded package if the specified file isn't found.
            var packageExtensions = new List<string>();
            packageExtensions.Add("");  // Search for aarFilename first.
            packageExtensions.AddRange(Dependency.Packaging);
            foreach (var extension in packageExtensions) {
                AarExplodeData data;
                if (aarExplodeData.TryGetValue(aarFilename + extension, out data)) {
                    aarData = data;
                    break;
                }
            }
            return aarData;
        }

        /// <summary>
        /// Whether an Ant project should be generated for an artifact.
        /// </summary>
        /// <param name="explode">Whether the artifact needs to be exploded so that it can be
        /// modified.</param>
        private static bool GenerateAntProject(bool explode) {
            return explode && !PlayServicesResolver.GradleBuildEnabled;
        }

        /// <summary>
        /// Determine whether a package is dirty in the AAR cache.
        /// </summary>
        /// <param name="aarData">Path of the AAR to query.</param>
        /// <returns>true if the cache entry is dirty, false otherwise.</returns>
        private bool AarExplodeDataIsDirty(AarExplodeData aarData) {
            if (aarData.bundleId != PlayServicesResolver.GetAndroidApplicationId()) {
                PlayServicesResolver.Log(
                    String.Format("{0}: Bundle ID changed {1} --> {2}", aarData.path,
                                  aarData.bundleId,
                                  PlayServicesResolver.GetAndroidApplicationId()),
                    level: LogLevel.Verbose);
                return true;
            }
            var availableAbis = aarData.AvailableAbis;
            var targetAbis = aarData.TargetAbis;
            var currentAbis = AndroidAbis.Current;
            if (targetAbis != null && availableAbis != null &&
                !targetAbis.Equals(AndroidAbis.Current)) {
                PlayServicesResolver.Log(
                    String.Format("{0}: Target ABIs changed {1} --> {2}", aarData.path,
                                  targetAbis.ToString(), currentAbis.ToString()),
                    level: LogLevel.Verbose);
                return true;
            }
            if (aarData.gradleBuildSystem != PlayServicesResolver.GradleBuildEnabled) {
                PlayServicesResolver.Log(
                    String.Format("{0}: Gradle build system enabled changed {1} --> {2}",
                                  aarData.path, aarData.gradleBuildSystem,
                                  PlayServicesResolver.GradleBuildEnabled),
                    level: LogLevel.Verbose);
                return true;
            }
            if (aarData.gradleExport != PlayServicesResolver.GradleProjectExportEnabled) {
                PlayServicesResolver.Log(
                    String.Format("{0}: Gradle export settings changed {1} --> {2}",
                                  aarData.path, aarData.gradleExport,
                                  PlayServicesResolver.GradleProjectExportEnabled),
                    level: LogLevel.Verbose);
                return true;
            }
            if (aarData.gradleTemplate != PlayServicesResolver.GradleTemplateEnabled) {
                PlayServicesResolver.Log(
                    String.Format("{0}: Gradle template changed {1} --> {2}",
                                  aarData.path, aarData.gradleTemplate,
                                  PlayServicesResolver.GradleTemplateEnabled),
                    level: LogLevel.Verbose);
                return true;
            }
            bool generateAntProject = GenerateAntProject(aarData.explode);
            if (generateAntProject && !Directory.Exists(aarData.path)) {
                PlayServicesResolver.Log(
                    String.Format("{0}: Should be exploded but artifact directory missing.",
                                  aarData.path),
                    level: LogLevel.Verbose);
                return true;
            } else if (!generateAntProject && !File.Exists(aarData.path)) {
                PlayServicesResolver.Log(
                    String.Format("{0}: Should *not* be exploded but artifact file missing.",
                                  aarData.path),
                    level: LogLevel.Verbose);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Get the target path for an exploded AAR.
        /// </summary>
        /// <param name="aarPath">AAR file to explode.</param>
        /// <returns>Exploded AAR path.</returns>
        private string DetermineExplodedAarPath(string aarPath) {
            return Path.Combine(GooglePlayServices.SettingsDialog.PackageDir,
                                AarPathToPackageName(aarPath));
        }

        /// <summary>
        /// Get the path of an existing AAR or exploded directory within the target directory.
        /// </summary>
        /// <param name="artifactName">Name of the artifact to search for.</param>
        /// <returns>Path to the artifact if found, null otherwise.</returns>
        private string FindAarInTargetPath(string aarPath) {
            var basePath = DetermineExplodedAarPath(aarPath);
            if (Directory.Exists(basePath)) return basePath;
            foreach (var extension in Dependency.Packaging) {
                var packagePath = basePath + extension;
                if (File.Exists(packagePath)) return packagePath;
            }
            return null;
        }

        /// <summary>
        /// Processes the aars.
        /// </summary>
        /// <remarks>Each aar copied is inspected and determined if it should be
        /// exploded into a directory or not. Unneeded exploded directories are
        /// removed.
        /// <para>
        /// Exploding is needed if the version of Unity is old, or if the artifact
        /// has been explicitly flagged for exploding.  This allows the subsequent
        /// processing of variables in the AndroidManifest.xml file which is not
        /// supported by the current versions of the manifest merging process that
        /// Unity uses.
        /// </para>
        /// <param name="dir">The directory to process.</param>
        /// <param name="updatedFiles">Set of files that were recently updated and should be
        /// processed.</param>
        /// <param name="progressUpdate">Called with the progress (0..1) and message that indicates
        /// processing progress.</param>
        /// <param name="complete">Executed when this process is complete.</param>
        private void ProcessAars(string dir, HashSet<string> updatedFiles,
                                 Action<float, string> progressUpdate, Action complete) {
            // Get set of AAR files and directories we're managing.
            var uniqueAars = new HashSet<string>(PlayServicesResolver.FindLabeledAssets());
            foreach (var aarData in aarExplodeData.Values) uniqueAars.Add(aarData.path);
            var aars = new Queue<string>(uniqueAars);

            int numberOfAars = aars.Count;
            if (numberOfAars == 0) {
                complete();
                return;
            }
            // Processing can be slow so execute incrementally so we don't block the update thread.
            RunOnMainThread.PollOnUpdateUntilComplete(() => {
                int remainingAars = aars.Count;
                bool allAarsProcessed = remainingAars == 0;
                // Since the completion callback can trigger an update, remove this closure from
                // the polling job list if complete.
                if (allAarsProcessed) return true;
                int processedAars = numberOfAars - remainingAars;
                string aarPath = aars.Dequeue();
                remainingAars--;
                allAarsProcessed = remainingAars == 0;
                float progress = (float)processedAars / (float)numberOfAars;
                try {
                    progressUpdate(progress, aarPath);
                    bool explode = ShouldExplode(aarPath);
                    var aarData = FindAarExplodeDataEntry(aarPath);
                    PlayServicesResolver.Log(
                        String.Format("Processing {0} ({1})", aarPath, aarData.ToString()),
                        level: LogLevel.Verbose);
                    if (AarExplodeDataIsDirty(aarData) || updatedFiles.Contains(aarPath)) {
                        if (explode && File.Exists(aarPath)) {
                            AndroidAbis abis = null;
                            if (!ProcessAar(Path.GetFullPath(dir), aarPath,
                                            GenerateAntProject(explode), out abis)) {
                                PlayServicesResolver.Log(String.Format(
                                    "Failed to process {0}, your Android build will fail.\n" +
                                    "See previous error messages for failure details.\n",
                                    aarPath));
                            }
                            aarData.AvailableAbis = abis;
                        } else if (aarPath != aarData.path) {
                            // Clean up previously expanded / exploded versions of the AAR.
                            PlayServicesResolver.Log(
                                String.Format("Cleaning up previously exploded AAR {0}",
                                              aarPath),
                                level: LogLevel.Verbose);
                            var explodedPath = DetermineExplodedAarPath(aarPath);
                            var deleteError = FileUtils.FormatError(
                                String.Format("Failed to delete exploded AAR directory {0}",
                                              explodedPath),
                                FileUtils.DeleteExistingFileOrDirectory(explodedPath));
                            if (!String.IsNullOrEmpty(deleteError)) {
                                PlayServicesResolver.Log(deleteError, level: LogLevel.Error);
                            }
                        }
                        aarData.gradleBuildSystem = PlayServicesResolver.GradleBuildEnabled;
                        aarData.gradleExport = PlayServicesResolver.GradleProjectExportEnabled;
                        aarData.gradleTemplate = PlayServicesResolver.GradleTemplateEnabled;
                        aarData.TargetAbis = AndroidAbis.Current;
                    }
                    SaveAarExplodeCache();
                } finally {
                    if (allAarsProcessed) {
                        progressUpdate(1.0f, "Library processing complete");
                        complete();
                    }
                }
                return allAarsProcessed;
            });
        }

        /// <summary>
        /// Gets a value indicating whether this version of Unity supports aar files.
        /// </summary>
        /// <value><c>true</c> if supports aar files; otherwise, <c>false</c>.</value>
        internal bool SupportsAarFiles
        {
            get
            {
                // Get the version number.
                string majorVersion = Application.unityVersion.Split('.')[0];
                int ver;
                if (!int.TryParse(majorVersion, out ver))
                {
                    ver = 4;
                }
                return ver >= 5;
            }
        }


        /// <summary>
        /// Determines whether an aar file should be exploded (extracted).
        ///
        /// This is required for some aars so that the Unity Jar Resolver can perform variable
        /// expansion on manifests in the package before they're merged by aapt.
        /// </summary>
        /// <returns><c>true</c>, if the aar should be exploded, <c>false</c> otherwise.</returns>
        /// <param name="aarPath">Path of the AAR file to query whether it should be exploded or
        /// the path to the exploded AAR directory to determine whether the AAR should still be
        /// exploded.</param>
        internal virtual bool ShouldExplode(string aarPath) {
            bool newAarData = false;
            AarExplodeData aarData = FindAarExplodeDataEntry(aarPath);
            if (aarData == null) {
                newAarData = true;
                aarData = new AarExplodeData();
                aarData.path = aarPath;
            }
            string explodeDirectory = DetermineExplodedAarPath(aarPath);
            bool explosionEnabled = true;
            // Unfortunately, as of Unity 5.5.0f3, Unity does not set the applicationId variable
            // in the build.gradle it generates.  This results in non-functional libraries that
            // require the ${applicationId} variable to be expanded in their AndroidManifest.xml.
            // To work around this when Gradle builds are enabled, explosion is enabled for all
            // AARs that require variable expansion unless this behavior is explicitly disabled
            // in the settings dialog.
            if (PlayServicesResolver.GradleProjectExportEnabled && !SettingsDialog.ExplodeAars) {
                explosionEnabled = false;
            }
            AndroidAbis availableAbis = null;
            bool explode = false;
            if (explosionEnabled) {
                explode = !SupportsAarFiles;
                bool useCachedExplodeData = false;
                bool aarFile = File.Exists(aarPath);
                if (!explode) {
                    System.DateTime modificationTime =
                        aarFile ? File.GetLastWriteTime(aarPath) : System.DateTime.MinValue;
                    if (modificationTime.CompareTo(aarData.modificationTime) <= 0 &&
                        !AarExplodeDataIsDirty(aarData)) {
                        explode = aarData.explode;
                        useCachedExplodeData = true;
                    }
                }
                if (!explode) {
                    // If the path is a directory then the caller is referencing an AAR that has
                    // already been exploded in which case we keep explosion enabled.
                    string aarDirectory = Directory.Exists(explodeDirectory) ? explodeDirectory :
                        Directory.Exists(aarData.path) ? aarData.path : null;
                    if (!String.IsNullOrEmpty(aarDirectory)) {
                        // If the directory contains native libraries update the target ABI.
                        if (!useCachedExplodeData) {
                            newAarData = true;
                            availableAbis = AarDirectoryFindAbis(aarDirectory);
                        }
                        explode = true;
                    }
                }
                if (!useCachedExplodeData && !explode) {
                    // If the file doesn't exist we can't interrogate it so we can assume it
                    // doesn't need to be exploded.
                    if (!aarFile) return false;

                    string temporaryDirectory = FileUtils.CreateTemporaryDirectory();
                    if (temporaryDirectory == null) return false;
                    try {
                        string manifestFilename = "AndroidManifest.xml";
                        string classesFilename = "classes.jar";
                        if (aarPath.EndsWith(".aar") &&
                            PlayServicesResolver.ExtractZip(
                                aarPath, new string[] {manifestFilename, "jni", classesFilename},
                                temporaryDirectory)) {
                            string manifestPath = Path.Combine(temporaryDirectory,
                                                               manifestFilename);
                            if (File.Exists(manifestPath)) {
                                string manifest = File.ReadAllText(manifestPath);
                                explode = manifest.IndexOf("${applicationId}") >= 0;
                            }
                            // If the AAR is badly formatted (e.g does not contain classes.jar)
                            // explode it so that we can create classes.jar.
                            explode |= !File.Exists(Path.Combine(temporaryDirectory,
                                                                 classesFilename));
                            // If the AAR contains more than one ABI and Unity's build is
                            // targeting a single ABI, explode it so that unused ABIs can be
                            // removed.
                            newAarData = true;
                            availableAbis = AarDirectoryFindAbis(temporaryDirectory);
                            // Unity 2017's internal build system does not support AARs that contain
                            // native libraries so force explosion to pick up native libraries using
                            // Eclipse / Ant style projects.
                            explode |= availableAbis != null &&
                                Google.VersionHandler.GetUnityVersionMajorMinor() >= 2017.0f;
                            // NOTE: Unfortunately as of Unity 5.5 the internal Gradle build
                            // also blindly includes all ABIs from AARs included in the project
                            // so we need to explode the AARs and remove unused ABIs.
                            if (availableAbis != null) {
                                var abisToRemove = availableAbis.ToSet();
                                abisToRemove.ExceptWith(AndroidAbis.Current.ToSet());
                                explode |= abisToRemove.Count > 0;
                            }
                            aarData.modificationTime = File.GetLastWriteTime(aarPath);
                        }
                    }
                    catch (System.Exception e) {
                        PlayServicesResolver.Log(
                            String.Format("Unable to examine AAR file {0}\n\n{1}", aarPath, e),
                            level: LogLevel.Error);
                        throw e;
                    }
                    finally {
                        var deleteError = FileUtils.FormatError(
                            "Failed to clean up temporary directory",
                            FileUtils.DeleteExistingFileOrDirectory(temporaryDirectory));
                        if (!String.IsNullOrEmpty(deleteError)) {
                            PlayServicesResolver.Log(deleteError, level: LogLevel.Warning);
                        }
                    }
                }
            }
            // If this is a new cache entry populate the target ABI and bundle ID fields.
            if (newAarData) {
                aarData.AvailableAbis = availableAbis;
                aarData.TargetAbis = AndroidAbis.Current;
                aarData.bundleId = PlayServicesResolver.GetAndroidApplicationId();
            }
            aarData.path = GenerateAntProject(explode) ? explodeDirectory : aarPath;
            aarData.explode = explode;
            aarExplodeData[AarPathToPackageName(aarPath)] = aarData;
            SaveAarExplodeCache();
            return explode;
        }


        /// <summary>
        /// Create an AAR from the specified directory.
        /// </summary>
        /// <param name="aarFile">AAR file to create.</param>
        /// <param name="inputDirectory">Directory which contains the set of files to store
        /// in the AAR.</param>
        /// <returns>true if successful, false otherwise.</returns>
        internal static bool ArchiveAar(string aarFile, string inputDirectory) {
            try {
                string aarPath = Path.GetFullPath(aarFile);
                CommandLine.Result result = CommandLine.Run(
                    JavaUtilities.JarBinaryPath,
                    String.Format("cvf{0} \"{1}\" -C \"{2}\" .",
                                  aarFile.ToLower().EndsWith(".jar") ? "" : "M", aarPath,
                                  inputDirectory));
                if (result.exitCode != 0) {
                    Debug.LogError(String.Format("Error archiving {0}\n" +
                                                 "Exit code: {1}\n" +
                                                 "{2}\n" +
                                                 "{3}\n",
                                                 aarPath, result.exitCode, result.stdout,
                                                 result.stderr));
                    return false;
                }
            } catch (Exception e) {
                Debug.LogError(e);
                throw e;
            }
            return true;
        }

        // Native library ABI subdirectories supported by Unity.
        // Directories that contain native libraries within a Unity Android library project.
        private static string[] NATIVE_LIBRARY_DIRECTORIES = new string[] { "libs", "jni" };

        /// <summary>
        /// Get the set of native library ABIs in an exploded AAR.
        /// </summary>
        /// <param name="aarDirectory">Directory to search for ABIs.</param>
        /// <returns>Set of ABI directory names in the exploded AAR or null if none are
        /// found.</returns>
        internal static AndroidAbis AarDirectoryFindAbis(string aarDirectory) {
            var foundAbis = new HashSet<string>();
            foreach (var libDirectory in NATIVE_LIBRARY_DIRECTORIES) {
                foreach (var abiDir in AndroidAbis.AllSupported) {
                    if (Directory.Exists(Path.Combine(aarDirectory,
                                                      Path.Combine(libDirectory, abiDir)))) {
                        foundAbis.Add(abiDir);
                    }
                }
            }
            return foundAbis.Count > 0 ? new AndroidAbis(foundAbis) : null;
        }

        /// <summary>
        /// Explodes a single aar file.  This is done by calling the
        /// JDK "jar" command, then moving the classes.jar file.
        /// </summary>
        /// <param name="dir">The directory to unpack / explode the AAR to.  If antProject is true
        /// the ant project will be located in Path.Combine(dir, Path.GetFileName(aarFile)).</param>
        /// <param name="aarFile">Aar file to explode.</param>
        /// <param name="antProject">true to explode into an Ant style project or false
        /// to repack the processed AAR as a new AAR.</param>
        /// <param name="abis">ABIs in the AAR or null if it's universal.</param>
        /// <returns>true if successful, false otherwise.</returns>
        internal static bool ProcessAar(string dir, string aarFile, bool antProject,
                                        out AndroidAbis abis) {
            PlayServicesResolver.Log(String.Format("ProcessAar {0} {1} antProject={2}",
                                                   dir, aarFile, antProject),
                                     level: LogLevel.Verbose);
            abis = null;
            string aarDirName = Path.GetFileNameWithoutExtension(aarFile);
            // Output directory for the contents of the AAR / JAR.
            string outputDir = Path.Combine(dir, aarDirName);
            string stagingDir = FileUtils.CreateTemporaryDirectory();
            if (stagingDir == null) {
                PlayServicesResolver.Log(String.Format(
                        "Unable to create temporary directory to process AAR {0}", aarFile),
                    level: LogLevel.Error);
                return false;
            }
            try {
                string workingDir = Path.Combine(stagingDir, aarDirName);
                var deleteError = FileUtils.FormatError(
                    String.Format("Failed to create working directory to process AAR {0}",
                                  aarFile), FileUtils.DeleteExistingFileOrDirectory(workingDir));
                if (!String.IsNullOrEmpty(deleteError)) {
                    PlayServicesResolver.Log(deleteError, level: LogLevel.Error);
                    return false;
                }
                Directory.CreateDirectory(workingDir);
                if (!PlayServicesResolver.ExtractZip(aarFile, null, workingDir)) return false;
                PlayServicesResolver.ReplaceVariablesInAndroidManifest(
                    Path.Combine(workingDir, "AndroidManifest.xml"),
                    PlayServicesResolver.GetAndroidApplicationId(),
                    new Dictionary<string, string>());

                string nativeLibsDir = null;
                if (antProject) {
                    // Create the libs directory to store the classes.jar and non-Java shared
                    // libraries.
                    string libDir = Path.Combine(workingDir, "libs");
                    nativeLibsDir = libDir;
                    Directory.CreateDirectory(libDir);

                    // Move the classes.jar file to libs.
                    string classesFile = Path.Combine(workingDir, "classes.jar");
                    string targetClassesFile = Path.Combine(libDir, Path.GetFileName(classesFile));
                    if (File.Exists(targetClassesFile)) File.Delete(targetClassesFile);
                    if (File.Exists(classesFile)) {
                        FileUtils.MoveFile(classesFile, targetClassesFile);
                    } else {
                        // Some libraries publish AARs that are poorly formatted (e.g missing
                        // a classes.jar file).  Firebase's license AARs at certain versions are
                        // examples of this.  When Unity's internal build system detects an Ant
                        // project or AAR without a classes.jar, the build is aborted.  This
                        // generates an empty classes.jar file to workaround the issue.
                        string emptyClassesDir = Path.Combine(stagingDir, "empty_classes_jar");
                        Directory.CreateDirectory(emptyClassesDir);
                        if (!ArchiveAar(targetClassesFile, emptyClassesDir)) return false;
                    }
                }

                // Copy non-Java shared libraries (.so) files from the "jni" directory into the
                // lib directory so that Unity's legacy (Ant-like) build system includes them in the
                // built APK.
                string jniLibDir = Path.Combine(workingDir, "jni");
                nativeLibsDir = nativeLibsDir ?? jniLibDir;
                if (Directory.Exists(jniLibDir)) {
                    var abisInArchive = AarDirectoryFindAbis(workingDir);
                    if (jniLibDir != nativeLibsDir) {
                        FileUtils.CopyDirectory(jniLibDir, nativeLibsDir);
                        deleteError = FileUtils.FormatError(
                            String.Format("Unable to delete JNI directory from AAR {0}", aarFile),
                            FileUtils.DeleteExistingFileOrDirectory(jniLibDir));
                        if (!String.IsNullOrEmpty(deleteError)) {
                            PlayServicesResolver.Log(deleteError, level: LogLevel.Error);
                            return false;
                        }
                    }
                    if (abisInArchive != null) {
                        // Remove shared libraries for all ABIs that are not required for the
                        // selected ABIs.
                        var activeAbisSet = AndroidAbis.Current.ToSet();
                        var abisInArchiveSet = abisInArchive.ToSet();
                        var abisInArchiveToRemoveSet = new HashSet<string>(abisInArchiveSet);
                        abisInArchiveToRemoveSet.ExceptWith(activeAbisSet);

                        Func<IEnumerable<string>, string> setToString = (setToConvert) => {
                            return String.Join(", ", (new List<string>(setToConvert)).ToArray());
                        };
                        PlayServicesResolver.Log(
                            String.Format(
                                "Target ABIs [{0}], ABIs [{1}] in {2}, will remove [{3}] ABIs",
                                setToString(activeAbisSet),
                                setToString(abisInArchiveSet),
                                aarFile,
                                setToString(abisInArchiveToRemoveSet)),
                            level: LogLevel.Verbose);

                        foreach (var abiToRemove in abisInArchiveToRemoveSet) {
                            abisInArchiveSet.Remove(abiToRemove);
                            deleteError = FileUtils.FormatError(
                                String.Format("Unable to remove unused ABIs from {0}", aarFile),
                                FileUtils.DeleteExistingFileOrDirectory(
                                    Path.Combine(nativeLibsDir, abiToRemove)));
                            if (!String.IsNullOrEmpty(deleteError)) {
                                PlayServicesResolver.Log(deleteError, LogLevel.Warning);
                            }
                        }
                        abis = new AndroidAbis(abisInArchiveSet);
                    }
                }

                if (antProject) {
                    // Create the project.properties file which indicates to Unity that this
                    // directory is a plugin.
                    string projectProperties = Path.Combine(workingDir, "project.properties");
                    if (!File.Exists(projectProperties)) {
                        File.WriteAllLines(projectProperties, new [] {
                            "# Project target.",
                            "target=android-9",
                            "android.library=true"
                        });
                    }
                    PlayServicesResolver.Log(
                        String.Format("Creating Ant project: Replacing {0} with {1}", aarFile,
                                      outputDir), level: LogLevel.Verbose);
                    // Clean up the aar file.
                    deleteError = FileUtils.FormatError(
                        String.Format("Failed to clean up AAR file {0} after generating " +
                                      "Ant project {1}", aarFile, outputDir),
                        FileUtils.DeleteExistingFileOrDirectory(Path.GetFullPath(aarFile)));
                    if (!String.IsNullOrEmpty(deleteError)) {
                        PlayServicesResolver.Log(deleteError, level: LogLevel.Error);
                        return false;
                    }
                    // Create the output directory.
                    FileUtils.MoveDirectory(workingDir, outputDir);
                    // Add a tracking label to the exploded files.
                    PlayServicesResolver.LabelAssets(new [] { outputDir });
                } else {
                    // Add a tracking label to the exploded files just in-case packaging fails.
                    PlayServicesResolver.Log(String.Format("Repacking {0} from {1}",
                                                           aarFile, workingDir),
                                             level: LogLevel.Verbose);
                    // Create a new AAR file.
                    deleteError = FileUtils.FormatError(
                        String.Format("Failed to replace AAR file {0}", aarFile),
                        FileUtils.DeleteExistingFileOrDirectory(Path.GetFullPath(aarFile)));
                    if (!String.IsNullOrEmpty(deleteError)) {
                        PlayServicesResolver.Log(deleteError, level: LogLevel.Error);
                        return false;
                    }
                    if (!ArchiveAar(aarFile, workingDir)) return false;
                    PlayServicesResolver.LabelAssets(new [] { aarFile });
                }
            } catch (Exception e) {
                PlayServicesResolver.Log(String.Format("Failed to process AAR {0} ({1}",
                                                       aarFile, e),
                                         level: LogLevel.Error);
            } finally {
                // Clean up the temporary directory.
                var deleteError = FileUtils.FormatError(
                    String.Format("Failed to clean up temporary folder while processing {0}",
                                  aarFile), FileUtils.DeleteExistingFileOrDirectory(stagingDir));
                if (!String.IsNullOrEmpty(deleteError)) {
                    PlayServicesResolver.Log(deleteError, level: LogLevel.Warning);
                }
            }
            return true;
        }

        /// <summary>
        /// Extract a list of embedded resources to the specified path creating intermediate
        /// directories if they're required.
        /// </summary>
        /// <param name="resourceNameToTargetPath">Each Key is the resource to extract and each
        /// Value is the path to extract to.</param>
        protected static void ExtractResources(List<KeyValuePair<string, string>>
                                                   resourceNameToTargetPaths) {
            foreach (var kv in resourceNameToTargetPaths) ExtractResource(kv.Key, kv.Value);
        }

        /// <summary>
        /// Extract an embedded resource to the specified path creating intermediate directories
        /// if they're required.
        /// </summary>
        /// <param name="resourceName">Name of the resource to extract.</param>
        /// <param name="targetPath">Target path.</param>
        protected static void ExtractResource(string resourceName, string targetPath) {
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath));
            var stream = typeof(GradleResolver).Assembly.GetManifestResourceStream(resourceName);
            if (stream == null) {
                UnityEngine.Debug.LogError(String.Format("Failed to find resource {0} in assembly",
                                                         resourceName));
                return;
            }
            var data = new byte[stream.Length];
            stream.Read(data, 0, (int)stream.Length);
            File.WriteAllBytes(targetPath, data);
        }
    }
}
