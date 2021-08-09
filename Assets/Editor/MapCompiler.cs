using System;
using System.IO;
using UnityEngine;
using UnityEditor;
using System.Linq;
using System.Xml.Linq;
using System.Diagnostics;
using System.IO.Compression;
using UnityEditor.AnimatedValues;
using System.Collections.Generic;
using UnityEditor.SceneManagement;
using Debug = UnityEngine.Debug;
using Newtonsoft.Json;
using System.Reflection;

public class MapCompiler : EditorWindow
{
    private AnimBool customizeValues;

    private void OnEnable()
    {
        customizeValues = new AnimBool(false);
        customizeValues.valueChanged.AddListener(Repaint);
    }

    // todo: dynamically find msbuild instead of hardcoding it
    private static string tempPath = Path.Combine(Path.GetTempPath(), "temp_CustomMaps");

    [MenuItem("Custom Maps/Compile")]
    public static void GenerateMap()
    {
        Deserialize();

        CustomMapInfo.MapInfo info = GameObject.Find("CUSTOM_MAP_ROOT").GetComponent<CustomMapInfo>().info;

        if (info == null)
        {
            EditorUtility.DisplayDialog("Couldn't find CustomMapInfo on your CUSTOM_MAP_ROOT.", "", "OK");
            return;
        }

        Directory.CreateDirectory(tempPath);
        string[] generatedFiles = BuildMapBundle(tempPath, "map.bcm");
        Debug.Log("Built map bundle!");

        List<string> fileList = new List<string>();

        string json = JsonUtility.ToJson(info);
        // Write some text to the test.txt file
        string filePath = Path.Combine(tempPath, "info.json");
        string zipName = Path.Combine(Application.dataPath, info.mapName + ".cma");
        if (File.Exists(filePath)) File.Delete(filePath);
        if (File.Exists(zipName)) File.Delete(zipName);

        StreamWriter writer = new StreamWriter(filePath, true);
        writer.WriteLine(json);
        writer.Close();

        fileList.Add(filePath);
        fileList.AddRange(generatedFiles);

        string[] files = fileList.ToArray();

        CreateZipFile(zipName, files);

        Directory.Delete(tempPath, true);

        Deserialize();
        EditorUtility.DisplayDialog("Export Successful!", "Export Successful! The compiled CMA will appear shortly...", "OK");
    }

    [MenuItem("Custom Maps/Deserialize Monobehaviours")]
    public static void Deserialize()
    {
        CustomMonoBehaviourHandler.staticDeserializeJsonMethod = (from m in typeof(JsonConvert).GetMethods()
                                                                  where m.IsGenericMethod && m.Name == "DeserializeObject" && m.GetParameters().Length == 1
                                                                  select m).First<MethodInfo>();

        foreach (CustomMonoBehaviourSerializer serializer in Resources.FindObjectsOfTypeAll<CustomMonoBehaviourSerializer>())
        {
            LocalizedText localizedText = serializer.localizedText;
            bool flag = localizedText.key.StartsWith(CustomMonoBehaviourHandler.identifier);
            if (flag)
            {
                string[] array3 = localizedText.key.Split(new string[]
                {
                CustomMonoBehaviourHandler.splitter
                }, StringSplitOptions.None);
                string typeName = array3[1];
                string value = array3[2];

                string[] className = typeName.Split(new string[] { "." }, StringSplitOptions.None);

                typeName = className[className.Length - 1];
                array3[1] = typeName;
                string newKey = "";
                foreach (string str in array3)
                {
                    newKey += str;
                    newKey += CustomMonoBehaviourHandler.splitter;
                }
                localizedText.key = newKey;

                Type type = AppDomain.CurrentDomain.GetAssemblies().Single((a) => a.GetName().Name == "Assembly-CSharp").GetTypes().Single((t) => t.Name == typeName);
                Dictionary<string, object> dictionary = JsonConvert.DeserializeObject<Dictionary<string, object>>(value);

                Component comp = localizedText.gameObject.GetComponent(type);
                if (comp == null)
                {
                    comp = localizedText.gameObject.AddComponent(type);
                    localizedText.gameObject.GetComponent<CustomMonoBehaviourSerializer>().classToSerialize = (MonoBehaviour)comp;
                }

                foreach (FieldInfo fieldInfo in type.GetFields(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    bool flag4 = dictionary.ContainsKey(fieldInfo.Name);
                    if (flag4)
                    {
                        string text2 = JsonConvert.SerializeObject(dictionary[fieldInfo.Name]);
                        object value2 = CustomMonoBehaviourHandler.staticDeserializeJsonMethod.MakeGenericMethod(new Type[]
                        {
                            fieldInfo.FieldType
                        }).Invoke(null, new object[] {
                            text2
                        });

                        fieldInfo.SetValue(comp, value2);
                    }
                }
            }
        }
    }

    public static string[] BuildMapBundle(string savePath, string mapName)
    {
        List<string> paths = new List<string>();

        string bwPath;
        string seperator = ":=:";
        bool compilingBehaviours = false;
        if (!File.Exists(Path.Combine(Directory.GetParent(Application.dataPath).FullName, "compiler_info.txt")))
        {
            bwPath = EditorUtility.OpenFilePanel("Select the ModThatIsNotMod .dll", "", "dll");
            bwPath = new DirectoryInfo(Path.GetDirectoryName(bwPath)).Parent.FullName;

            if (string.IsNullOrEmpty(bwPath))
            {
                EditorUtility.DisplayDialog("BONEWORKS path not found!", "You didn't select the ModThatIsNotMod dll. This means Custom MonoBehaviours will not be exported.", "OK");
            } else
            {
                File.WriteAllText(Path.Combine(Directory.GetParent(Application.dataPath).FullName, "compiler_info.txt"), bwPath);
                compilingBehaviours = true;
            }
        }
        else
        {
            compilingBehaviours = true;
            string readFile = File.ReadAllText(Path.Combine(Directory.GetParent(Application.dataPath).FullName, "compiler_info.txt"));
            bwPath = readFile;
        }

        if (!(Resources.FindObjectsOfTypeAll<CustomMonoBehaviourSerializer>().Length > 0))
            compilingBehaviours = false;

        if (!string.IsNullOrWhiteSpace(savePath))
        {
            if (EditorSceneManager.loadedSceneCount > 1)
            {
                EditorUtility.DisplayDialog("Export Failed!", "Multiple scenes are unsupported. Please close all but the scene you wish to load into the game.", "OK");
                return null;
            }

            string fileName = Path.GetFileName(mapName);
            string folderPath = Path.GetDirectoryName(savePath);
            EditorSceneManager.SaveOpenScenes();

            UnityEngine.SceneManagement.Scene activeScene = EditorSceneManager.GetActiveScene();
            string previousName = activeScene.name;
            if (compilingBehaviours)
            {
                string asmName = MakeAsmSafe(previousName);
                string assemblyPath = ExportMonoBehaviours(asmName, folderPath, bwPath);
                paths.Add(assemblyPath);
            }

            AssetBundleBuild assetBundleBuild = default;
            assetBundleBuild.assetBundleName = fileName;
            assetBundleBuild.assetNames = new string[] {
                activeScene.path
            };

            BuildPipeline.BuildAssetBundles(folderPath, new AssetBundleBuild[] { assetBundleBuild }, BuildAssetBundleOptions.ChunkBasedCompression, BuildTarget.StandaloneWindows64);

            paths.Add(Path.Combine(folderPath, fileName));

            AssetDatabase.Refresh();

            return paths.ToArray();
        }
        else
        {
            EditorUtility.DisplayDialog("Export Failed!", "Path is invalid.", "OK");
            return null;
        }
    }

    private static string ExportMonoBehaviours(string asmName, string exportDir, string bwPath)
    {
        List<Type> exportedTypes = new List<Type>();

        string tempDir = Path.Combine(Path.GetTempPath(), asmName);
        Directory.CreateDirectory(tempDir);

        // not very proud of this but hey if it works it works
        string projTemplateDir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "BehaviourProjectTemplate");

        Debug.Log(bwPath);
        XDocument csproj = XDocument.Parse(File.ReadAllText(Path.Combine(projTemplateDir, "CustomMonoBehaviour.csproj")).Replace("$safeprojectname$", asmName).Replace("$boneworksdir$", bwPath));
        XElement compile = csproj.Root.Elements().Single((e) => e.ToString().Contains("Compile"));

        var newScriptFile = File.ReadAllLines(Path.Combine(projTemplateDir, "CustomMonoBehaviour.cs")).ToList();
        for (int i = 0; i < newScriptFile.Count; i++) newScriptFile[i] = newScriptFile[i].Replace("$safeprojectname$", asmName);
        int lastIndex = newScriptFile.IndexOf(newScriptFile.Single((s) => s.Contains("newshithere")));

        string[] scriptFiles = Directory.GetFiles(Application.dataPath, "*.cs", SearchOption.AllDirectories);
        foreach (CustomMonoBehaviourSerializer serializer in Resources.FindObjectsOfTypeAll<CustomMonoBehaviourSerializer>())
        {
            //Do some treating for when we export the bundle
            string[] array3 = serializer.localizedText.key.Split(new string[] { CustomMonoBehaviourHandler.splitter }, StringSplitOptions.None);

            string typeName = array3[1];
            string value = array3[2];

            typeName = asmName + "." + typeName;
            array3[1] = typeName;

            string newKey = "";
            foreach (string str in array3)
            {
                newKey += str;
                newKey += CustomMonoBehaviourHandler.splitter;
            }

            serializer.localizedText.key = newKey;

            Type type = serializer.classToSerialize.GetType();
            if (exportedTypes.Contains(type))
            {
                Debug.Log("Found duplicate script, skipping");
                continue;
            }

            //Debug.Log("Searching for script of " + type.Name);
            string scriptPath = scriptFiles.Single((f) => Path.GetFileNameWithoutExtension(f) == type.Name);
            if (!string.IsNullOrEmpty(scriptPath))
            {
                //Debug.Log("Found it!");
                XElement newCompile = new XElement("Compile");
                newCompile.SetAttributeValue("Include", Path.GetFileName(scriptPath));
                compile.Add(newCompile);

                bool changedNamespace = false;
                var scriptLines = File.ReadAllLines(scriptPath).ToList();
                for (int i = 0; i < scriptLines.Count; i++)
                {
                    if (scriptLines[i].Contains("namespace"))
                    {
                        changedNamespace = true;
                        if (scriptLines[i].Contains("{"))
                            scriptLines[i] = $"namespace {asmName} {{";
                        else
                            scriptLines[i] = $"namespace {asmName}";
                    }
                }

                if (!changedNamespace)
                    scriptLines.Insert(0, $"namespace {asmName} {{");

                string classDefinition = scriptLines.Single((f) => f.Contains(" : MonoBehaviour"));
                int currentIndex = scriptLines.IndexOf(classDefinition);
                if (classDefinition.Contains("{"))
                    currentIndex += 1;
                else
                    currentIndex += 2;

                scriptLines.Insert(currentIndex, $"public {type.Name}(System.IntPtr ptr) : base(ptr) {{ }}");

                bool changedAwake = false;
                for (int i = 0; i < scriptLines.Count; i++)
                {
                    if (scriptLines[i].Contains("void Awake"))
                    {
                        changedAwake = true;
                        if (scriptLines[i].Contains("{"))
                            scriptLines.Insert(i + 1, $"ModThatIsNotMod.MonoBehaviours.CustomMonoBehaviourHandler.SetFieldValues(this);");
                        else
                            scriptLines.Insert(i + 2, $"ModThatIsNotMod.MonoBehaviours.CustomMonoBehaviourHandler.SetFieldValues(this);");
                    }
                }

                if (!changedAwake)
                {
                    if (changedNamespace)
                        scriptLines.Insert(currentIndex, "private void Awake() => ModThatIsNotMod.MonoBehaviours.CustomMonoBehaviourHandler.SetFieldValues(this);");
                    else
                        scriptLines.Insert(currentIndex, "private void Awake() => ModThatIsNotMod.MonoBehaviours.CustomMonoBehaviourHandler.SetFieldValues(this);");
                }

                if (!changedNamespace)
                    scriptLines.Insert(scriptLines.Count, "}");
                File.WriteAllLines(Path.Combine(tempDir, Path.GetFileName(scriptPath)), scriptLines);
                newScriptFile.Insert(lastIndex += 1, $"CustomMonoBehaviourHandler.RegisterMonoBehaviourInIl2Cpp<{type.Name}>();");

                exportedTypes.Add(type);
            }
            else
                Debug.LogError("FAILED TO FIND SCRIPT FOR " + type.Name + ". SKIPPING");
        }

        // xml stuff is weird so uh heres this
        string finalCsproj = csproj.ToString().Replace("xmlns=\"\" ", "");
        File.WriteAllText(Path.Combine(tempDir, "CustomMonoBehaviour.csproj"), finalCsproj);
        File.WriteAllLines(Path.Combine(tempDir, "CustomMonoBehaviour.cs"), newScriptFile);
        File.WriteAllText(Path.Combine(tempDir, "AssemblyInfo.cs"), File.ReadAllText(Path.Combine(projTemplateDir, "AssemblyInfo.cs")).Replace("$safeprojectname$", asmName));

        // if they aint got vs 2019 too bad, might be able to use vsfind or whatever its called to make this more dyanmic
        Process proc = new Process();
        proc.StartInfo.FileName = @"C:\Program Files (x86)\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\MSBuild.exe";
        proc.StartInfo.WorkingDirectory = tempDir;
        proc.StartInfo.UseShellExecute = false;
        proc.StartInfo.RedirectStandardError = true;
        proc.StartInfo.RedirectStandardOutput = true;
        proc.Start();
        string stdoutx = proc.StandardOutput.ReadToEnd();
        string stderrx = proc.StandardError.ReadToEnd();
        proc.WaitForExit();

        File.WriteAllText(Path.Combine(Directory.GetParent(Application.dataPath).FullName, "msbuild_out.txt"), stdoutx);

        if (!Directory.Exists(exportDir))
            Directory.CreateDirectory(exportDir);

        if (File.Exists(Path.Combine(exportDir, "map.dll")))
            File.Delete(Path.Combine(exportDir, "map.dll"));

        File.Copy(Path.Combine(tempDir, "bin", "Debug", asmName + ".dll"), Path.Combine(exportDir, "map.dll"));
        Directory.Delete(tempDir, true);

        return Path.Combine(exportDir, "map.dll");
    }

    public static void CreateZipFile(string zipDir, string[] files)
    {
        var zip = ZipFile.Open(zipDir, ZipArchiveMode.Create);
        foreach (var file in files)
        {
            Debug.Log(Path.GetFileName(file));
            zip.CreateEntryFromFile(file, Path.GetFileName(file), System.IO.Compression.CompressionLevel.Optimal);
        }
        zip.Dispose();
    }

    public static string MakeAsmSafe(string _str)
    {
        string str = _str;
        // this wont work in some specific edge cases but for the most part it should be fine
        foreach (char c in Path.GetInvalidFileNameChars())
            str = str.Replace(c, '_');

        str = str.Trim('_');
        return str;
    }

    public class CustomMonoBehaviourHandler
    {
        public static readonly string identifier = "CUSTOM_MONOBEHAVIOUR";
        public static readonly string splitter = "-::-";
        public static MethodInfo staticDeserializeJsonMethod;
    }
}